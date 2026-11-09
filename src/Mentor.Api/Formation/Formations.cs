using System.Data;
using System.Security.Claims;
using Mentor.Api.Audit;
using Mentor.Api.Authorisation;
using Mentor.Api.Identity;
using Mentor.Api.Notifications;
using Mentor.Api.Requests;
using Mentor.Core.Agreements;
using Mentor.Core.Audit;
using Mentor.Core.Formation;
using Mentor.Core.Goals;
using Mentor.Core.Notifications;
using Mentor.Core.Relationships;
using Mentor.Core.Requests;
using Mentor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Mentor.Api.Formation;

/// <summary>
/// Formation approval and relationship formation (COMP-FORMATION, ADR-005): workflow Steps 3 and 4. A Talent Management
/// administrator's decision names the current revision and the content hash they reviewed, and is one versioned candidate
/// command: after the version claim it re-checks, under that lock, current mutual willingness, the currency of the revision
/// and its hash, and both identified participants' acceptances of exactly that revision (<see cref="FormationRules"/>).
/// An approval then records the APPROVED decision, forms the relationship (State In Progress, Status blank, IsActive N),
/// writes the formed agreement with the accepted content byte for byte and the annex beside it, instantiates the goals,
/// marks the candidate FORMED and the request FORMED, and writes the audit entries and both participants' notifications,
/// all in one transaction: a failed check persists nothing. A rejection records the REJECTED decision, the audit entry
/// and the notifications, and forms nothing. Nothing here updates or deletes a decision, a formed agreement or a goal.
/// </summary>
public sealed class Formations(MentorDbContext db, CallerResolver callers, IAuthorizationService authorization, AuditTrail audit, NotificationEvents notifications)
{
    /// <summary>The database refuses any write inside a view's transaction; a view only ever reads.</summary>
    private const string ReadOnlyTransaction = "SET TRANSACTION READ ONLY";

    /// <summary>The formation outcome of the candidate: its decisions and, once formed, the relationship (ADR-003: the parties read the outcome).</summary>
    public async Task<FormationResult<FormationView>> ReadAsync(ClaimsPrincipal user, Guid candidateId, CancellationToken cancellationToken)
    {
        var candidate = await db.MentorSelections.AsNoTracking().SingleOrDefaultAsync(row => row.Id == candidateId, cancellationToken);
        if (candidate is null)
        {
            return FormationResult<FormationView>.Refused(FormationRefusal.NotFound);
        }

        return await authorization.IsPermittedAsync(user, AuthorisationMatrix.Formation.Read, ProtectedRecord.Of(candidate))
            ? FormationResult<FormationView>.Of(await ViewAsync(candidateId, cancellationToken))
            : FormationResult<FormationView>.Refused(FormationRefusal.Forbidden);
    }

    /// <summary>
    /// A Talent Management administrator's formation decision on <paramref name="revisionId"/>, naming the content hash
    /// they reviewed (FR-030, FR-031, FR-070, BR-003): an approval forms the relationship, a rejection forms nothing.
    /// </summary>
    public async Task<FormationResult<FormationView>> DecideAsync(
        ClaimsPrincipal user, Guid candidateId, Guid revisionId, long? version, FormationOutcome outcome, string contentHash, CancellationToken cancellationToken)
    {
        var candidate = await db.MentorSelections.AsNoTracking().SingleOrDefaultAsync(row => row.Id == candidateId, cancellationToken);
        if (candidate is null)
        {
            return FormationResult<FormationView>.Refused(FormationRefusal.NotFound);
        }

        if (!await authorization.IsPermittedAsync(user, AuthorisationMatrix.Formation.Decide, ProtectedRecord.Of(candidate))
            || await callers.ResolveAsync(user, cancellationToken) is not { } actor)
        {
            return FormationResult<FormationView>.Refused(FormationRefusal.Forbidden);
        }

        // The decision names the approving administrator by their employee record (DR-008).
        if (actor.EmployeeId is not { } administrator)
        {
            return FormationResult<FormationView>.Refused(FormationRefusal.NoEmployeeRecord);
        }

        if (version is not { } decidedOn)
        {
            return FormationResult<FormationView>.Refused(FormationRefusal.VersionRequired);
        }

        var refusal = await CommitAsync(candidateId, decidedOn,
            head => DecideAsync(actor, administrator, head, revisionId, outcome, contentHash, cancellationToken), cancellationToken);
        return refusal == FormationRefusal.None
            ? FormationResult<FormationView>.Of(await ViewAsync(candidateId, cancellationToken))
            : FormationResult<FormationView>.Refused(refusal);
    }

    private async Task<FormationRefusal> DecideAsync(
        Caller actor, Guid administrator, MentorSelection head, Guid revisionId, FormationOutcome outcome, string contentHash, CancellationToken cancellationToken)
    {
        var revision = await db.AgreementRevisions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == revisionId && row.CandidateId == head.Id, cancellationToken);
        if (revision is null)
        {
            return FormationRefusal.NotFound;
        }

        // Read under the version lock: no acceptance, replacement or withdrawal can commit between this check and the commit.
        var reviewed = RevisionReference.Of(revision);
        var acceptances = await db.AgreementAcceptances.AsNoTracking()
            .Where(acceptance => acceptance.RevisionId == revision.Id)
            .ToListAsync(cancellationToken);
        var block = FormationRules.Check(head, reviewed, contentHash, acceptances);
        if (block != FormationBlock.None)
        {
            return block switch
            {
                FormationBlock.NotDrafting => FormationRefusal.NotInState,
                FormationBlock.RevisionSuperseded => FormationRefusal.RevisionSuperseded,
                FormationBlock.ContentMismatch => FormationRefusal.ContentMismatch,
                _ => FormationRefusal.NotAccepted,
            };
        }

        var decision = new FormationDecision
        {
            CandidateId = head.Id,
            RevisionId = reviewed.RevisionId,
            ContentHash = reviewed.ContentHash,
            Outcome = outcome,
            AdministratorEmployeeId = administrator,
        };
        db.FormationDecisions.Add(decision);
        var auditOutcome = outcome == FormationOutcome.Approved ? AuditOutcome.Approved : AuditOutcome.Rejected;
        audit.Append(actor, new AuditEvent(AuditAction.FormationDecided, AuditRecordType.FormationDecision, decision.Id)
        {
            CandidateId = head.Id,
            Revision = reviewed,
            NewValues = new AuditValues().Set("outcome", outcome),
            Outcome = auditOutcome,
        });

        if (outcome == FormationOutcome.Approved)
        {
            await FormAsync(actor, head, revision, reviewed, cancellationToken);
        }

        notifications.Raise(WorkflowNotifications.FormationDecided(head, outcome));
        return FormationRefusal.None;
    }

    /// <summary>
    /// Step 4 (ADR-005): the relationship, its formed agreement and its goals, composed from the candidate and the approved
    /// revision alone; the candidate and its request become FORMED. Each affected record has its own audit entry, so the
    /// relationship's history starts with the values formation gave it (FR-033).
    /// </summary>
    private async Task FormAsync(Caller actor, MentorSelection head, AgreementRevision revision, RevisionReference reviewed, CancellationToken cancellationToken)
    {
        var content = AgreementContent.Parse(revision.Content);
        var relationship = FormationRecords.Relationship(head, content);
        db.MentorshipRelationships.Add(relationship);
        var agreement = FormationRecords.Agreement(head, revision, relationship.Id);
        db.FormedAgreements.Add(agreement);
        var goals = FormationRecords.Goals(content, relationship.Id, agreement.Id);
        db.DevelopmentGoals.AddRange(goals);

        await db.MentorSelections.Where(row => row.Id == head.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.Status, CandidateStatus.Formed), cancellationToken);
        await db.MentorshipRequests.Where(row => row.Id == head.RequestId)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.Disposition, RequestDisposition.Formed), cancellationToken);

        audit.Append(actor, new AuditEvent(AuditAction.FormationDecided, AuditRecordType.Candidate, head.Id)
        {
            CandidateId = head.Id,
            Revision = reviewed,
            PriorValues = new AuditValues().Set("status", head.Status),
            NewValues = new AuditValues().Set("status", CandidateStatus.Formed),
            Outcome = AuditOutcome.Approved,
        });
        audit.Append(actor, new AuditEvent(AuditAction.FormationDecided, AuditRecordType.Relationship, relationship.Id)
        {
            CandidateId = head.Id,
            Revision = reviewed,
            NewValues = new AuditValues()
                .Set("provenance", relationship.Provenance)
                .Set("protegeEmployeeId", relationship.ProtegeEmployeeId)
                .Set("mentorEmployeeId", relationship.MentorEmployeeId)
                .Set("state", relationship.State)
                .Set("status", relationship.Status)
                .Set("isActive", relationship.IsActive)
                .Set("durationCategory", relationship.DurationCategory)
                .Set("startDate", relationship.StartDate)
                .Set("endDate", relationship.EndDate)
                .Set("formedAgreementId", agreement.Id)
                .Set("goalCount", goals.Count),
            Outcome = AuditOutcome.Approved,
        });
        audit.Append(actor, new AuditEvent(AuditAction.FormationDecided, AuditRecordType.MentorshipRequest, head.RequestId)
        {
            CandidateId = head.Id,
            Revision = reviewed,
            PriorValues = new AuditValues().Set("disposition", RequestDisposition.Open),
            NewValues = new AuditValues().Set("disposition", RequestDisposition.Formed),
            Outcome = AuditOutcome.Approved,
        });
    }

    /// <summary>
    /// The command protocol of ADR-005: one transaction whose first statement claims the version the administrator decided
    /// on, then the checks and the decision on the head as that claim leaves it, committed together or not at all.
    /// </summary>
    private async Task<FormationRefusal> CommitAsync(Guid id, long decidedOn, Func<MentorSelection, Task<FormationRefusal>> command, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await CandidateVersion.ClaimAsync(db, id, decidedOn, cancellationToken))
        {
            return FormationRefusal.Stale;
        }

        // A refusal leaves the transaction uncommitted, so the claimed version rolls back with it.
        var refusal = await command(await db.MentorSelections.AsNoTracking().SingleAsync(row => row.Id == id, cancellationToken));
        if (refusal != FormationRefusal.None)
        {
            return refusal;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return FormationRefusal.None;
    }

    /// <summary>
    /// The outcome as one read-only snapshot (REPEATABLE READ): the head the applicability of every decision and the step
    /// are derived from, the decisions and the relationship describe the same moment.
    /// </summary>
    private async Task<FormationView> ViewAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await db.Database.ExecuteSqlRawAsync(ReadOnlyTransaction, cancellationToken);
        var head = await db.MentorSelections.AsNoTracking().SingleAsync(row => row.Id == id, cancellationToken);
        List<AgreementAcceptance> acceptances = head.CurrentRevisionId is { } current
            ? await db.AgreementAcceptances.AsNoTracking().Where(acceptance => acceptance.RevisionId == current).ToListAsync(cancellationToken)
            : [];
        var decisions = await db.FormationDecisions.AsNoTracking()
            .Where(decision => decision.CandidateId == id)
            .Join(db.AgreementRevisions, decision => decision.RevisionId, revision => revision.Id, (decision, revision) => new { decision, revision.RevisionNumber })
            .OrderBy(row => row.decision.DecidedAt).ThenBy(row => row.decision.Id)
            .ToListAsync(cancellationToken);
        var relationship = await db.MentorshipRelationships.AsNoTracking().SingleOrDefaultAsync(row => row.CandidateId == id, cancellationToken);
        FormedRelationshipView? formed = null;
        if (relationship is not null)
        {
            var agreement = await db.FormedAgreements.AsNoTracking().SingleAsync(row => row.RelationshipId == relationship.Id, cancellationToken);
            var goals = await db.DevelopmentGoals.AsNoTracking()
                .Where(goal => goal.RelationshipId == relationship.Id)
                .OrderBy(goal => goal.Position)
                .ToListAsync(cancellationToken);
            formed = RelationshipOf(relationship, agreement, goals);
        }

        await snapshot.CommitAsync(cancellationToken);

        return new FormationView(
            head.Id,
            head.Status,
            AgreementWorkflow.StepOf(head, acceptances),
            head.CurrentRevisionId,
            [
                .. decisions.Select(row => new FormationDecisionView(
                    row.decision.Id, row.decision.RevisionId, row.RevisionNumber, row.decision.ContentHash, row.decision.Outcome,
                    row.decision.AdministratorEmployeeId, row.decision.DecidedAt,
                    ApplicabilityRule.Of(head.Status, head.CurrentRevisionId, row.decision.RevisionId))),
            ],
            formed,
            head.Version);
    }

    private static FormedRelationshipView RelationshipOf(MentorshipRelationship relationship, FormedAgreement agreement, IEnumerable<DevelopmentGoal> goals) => new(
        relationship.Id,
        relationship.Provenance,
        relationship.ProtegeEmployeeId,
        relationship.MentorEmployeeId,
        relationship.State,
        relationship.Status,
        relationship.IsActive,
        relationship.DurationCategory,
        relationship.StartDate,
        relationship.EndDate,
        relationship.CreatedAt,
        new FormedAgreementView(
            agreement.Id,
            agreement.RevisionId,
            agreement.ContentHash,
            agreement.FormedAt,
            AgreementContent.Parse(agreement.Content),
            new RetainedAnnexView(agreement.AdminReference, agreement.AdminRemarks)),
        [.. goals.Select(goal => new FormedGoalView(goal.Id, goal.Position, goal.AgreedDescription, goal.CompletionStatus))]);
}

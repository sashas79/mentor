using System.Data;
using System.Security.Claims;
using Mentor.Api.Audit;
using Mentor.Api.Authorisation;
using Mentor.Api.Identity;
using Mentor.Api.Notifications;
using Mentor.Api.Requests;
using Mentor.Core.Agreements;
using Mentor.Core.Audit;
using Mentor.Core.Notifications;
using Mentor.Core.Requests;
using Mentor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Mentor.Api.Agreements;

/// <summary>
/// The agreement draft of a DRAFTING candidate (COMP-AGREEMENT-DRAFTING, ADR-004): workflow Steps 1 and 2. Each
/// participant submits complete replacement revisions, which are immutable, content-addressed evidence; each accepts
/// for themselves exactly the current revision whose content hash was displayed to them; and a Talent Management
/// administrator alone changes the administrative annex, the one change that resets nothing. Every command is a
/// versioned candidate command (ADR-005) that writes its audit entry (ADR-007) and, for a revision or an acceptance, the
/// other participant's notification (ADR-009) in its own transaction. Nothing here updates or deletes a revision, an
/// acceptance or a decision: a replacement moves the candidate's current revision, and what that makes inapplicable
/// follows by derivation.
/// </summary>
public sealed class AgreementDrafts(MentorDbContext db, CallerResolver callers, IAuthorizationService authorization, AuditTrail audit, NotificationEvents notifications)
{
    /// <summary>The database refuses any write inside a view's transaction; a view only ever reads.</summary>
    private const string ReadOnlyTransaction = "SET TRANSACTION READ ONLY";

    public async Task<DraftOutcome<AgreementDraftView>> ReadAsync(ClaimsPrincipal user, Guid candidateId, CancellationToken cancellationToken)
    {
        var refusal = await ReadableAsync(user, candidateId, cancellationToken);
        return refusal == DraftRefusal.None
            ? DraftOutcome<AgreementDraftView>.Of(await ViewAsync(candidateId, cancellationToken))
            : DraftOutcome<AgreementDraftView>.Refused(refusal);
    }

    /// <summary>Any revision of the candidate, current or superseded, in full: the participants read every revision of their draft (ADR-003).</summary>
    public async Task<DraftOutcome<AgreementRevisionView>> ReadRevisionAsync(ClaimsPrincipal user, Guid candidateId, Guid revisionId, CancellationToken cancellationToken)
    {
        var refusal = await ReadableAsync(user, candidateId, cancellationToken);
        if (refusal != DraftRefusal.None)
        {
            return DraftOutcome<AgreementRevisionView>.Refused(refusal);
        }

        return await RevisionViewAsync(candidateId, revisionId, cancellationToken) is { } revision
            ? DraftOutcome<AgreementRevisionView>.Of(revision)
            : DraftOutcome<AgreementRevisionView>.Refused(DraftRefusal.NotFound);
    }

    /// <summary>
    /// A participant's replacement revision (FR-067, FR-029): the complete content, with the candidate's participants and
    /// the fixed policy text written by the server, inserted as a new revision that replaces the current one. Every
    /// participant submission is substantive, so the acceptances and formation decisions of the replaced revision stop
    /// applying and the draft returns to Step 1; the audit entry names them with outcome RESET_TO_STEP_1. The replaced
    /// revision and its evidence stay exactly as they were. Content identical to the current revision replaces nothing.
    /// </summary>
    public Task<DraftOutcome<AgreementDraftView>> ReviseAsync(ClaimsPrincipal user, Guid candidateId, long? version, AgreementDraftDetails details, CancellationToken cancellationToken) =>
        RunAsync(user, candidateId, version, AuthorisationMatrix.AgreementDraft.CreateRevision,
            (actor, employee, head) => ReviseAsync(actor, employee, head, details, cancellationToken), cancellationToken);

    /// <summary>
    /// The caller's own acceptance of the current revision (FR-027, FR-028, BR-012): once per participant and revision,
    /// naming the content hash displayed to them, which must be the revision's. Nobody accepts for someone else, and a
    /// revision's creation accepts nothing.
    /// </summary>
    public Task<DraftOutcome<AgreementDraftView>> AcceptAsync(
        ClaimsPrincipal user, Guid candidateId, Guid revisionId, long? version, string contentHash, CancellationToken cancellationToken) =>
        RunAsync(user, candidateId, version, AuthorisationMatrix.AgreementDraft.Accept,
            (actor, employee, head) => AcceptAsync(actor, employee, head, revisionId, contentHash, cancellationToken), cancellationToken);

    /// <summary>
    /// A Talent Management administrator's change of the administrative annex (FR-068, BR-020, BR-023): recorded as a
    /// permitted change with its basis, the prior and new value, the administrator and the server time, and audited. It
    /// touches no revision, hash or acceptance, so the step is unchanged.
    /// </summary>
    public Task<DraftOutcome<AgreementDraftView>> ChangeAnnexAsync(ClaimsPrincipal user, Guid candidateId, long? version, AnnexChange change, CancellationToken cancellationToken) =>
        RunAsync(user, candidateId, version, AuthorisationMatrix.AgreementDraft.ChangeAnnex,
            (actor, employee, head) => ChangeAnnexAsync(actor, employee, head, change, cancellationToken), cancellationToken);

    private async Task<DraftRefusal> ReviseAsync(Caller actor, Guid employee, MentorSelection head, AgreementDraftDetails details, CancellationToken cancellationToken)
    {
        if (!AgreementWorkflow.AdmitsDraftChange(head))
        {
            return DraftRefusal.NotInState;
        }

        var names = await NamesAsync(head, cancellationToken);
        var content = AgreementContent.Compose(
            new AgreementParties(
                new AgreementParty(head.ProtegeEmployeeId, names[head.ProtegeEmployeeId]),
                new AgreementParty(head.MentorEmployeeId, names[head.MentorEmployeeId])),
            details).ToCanonicalJson();
        var hash = AgreementContentHash.Compute(content);
        var current = head.CurrentRevisionId is { } currentId
            ? await db.AgreementRevisions.AsNoTracking().Where(revision => revision.Id == currentId)
                .Select(revision => new { revision.Id, revision.RevisionNumber, revision.ContentHash })
                .SingleAsync(cancellationToken)
            : null;
        if (current is not null && current.ContentHash == hash)
        {
            return DraftRefusal.ContentUnchanged;
        }

        // Read under the version lock, so the evidence named is exactly the evidence that stops applying.
        var invalidated = await audit.InapplicableEvidenceAsync(current?.Id, cancellationToken);
        var replacement = new AgreementRevision
        {
            CandidateId = head.Id,
            RevisionNumber = (current?.RevisionNumber ?? 0) + 1,
            ParentRevisionId = current?.Id,
            Content = content,
            ContentHash = hash,
            CreatedByEmployeeId = employee,
        };
        db.AgreementRevisions.Add(replacement);
        // The head may name only a revision of its own that exists, so the revision is written first.
        await db.SaveChangesAsync(cancellationToken);
        await db.MentorSelections.Where(row => row.Id == head.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.CurrentRevisionId, (Guid?)replacement.Id), cancellationToken);

        audit.Append(actor, new AuditEvent(AuditAction.RevisionCreated, AuditRecordType.AgreementRevision, replacement.Id)
        {
            CandidateId = head.Id,
            Revision = RevisionReference.Of(replacement),
            PriorValues = new AuditValues().Set("currentRevisionId", current?.Id),
            NewValues = new AuditValues().Set("currentRevisionId", replacement.Id).Set("revisionNumber", replacement.RevisionNumber),
            // The first revision replaces nothing; every later one returns the draft to Step 1.
            Outcome = current is null ? null : AuditOutcome.ResetToStep1,
            Invalidated = current is null ? null : invalidated,
        });
        notifications.Raise(WorkflowNotifications.RevisionCreated(head, employee));
        return DraftRefusal.None;
    }

    private async Task<DraftRefusal> AcceptAsync(Caller actor, Guid employee, MentorSelection head, Guid revisionId, string contentHash, CancellationToken cancellationToken)
    {
        var revision = await db.AgreementRevisions.AsNoTracking()
            .Where(row => row.Id == revisionId && row.CandidateId == head.Id)
            .Select(row => new RevisionReference(row.Id, row.ContentHash))
            .SingleOrDefaultAsync(cancellationToken);
        if (revision is null)
        {
            return DraftRefusal.NotFound;
        }

        if (!AgreementWorkflow.AdmitsDraftChange(head))
        {
            return DraftRefusal.NotInState;
        }

        if (head.CurrentRevisionId != revision.RevisionId)
        {
            return DraftRefusal.RevisionSuperseded;
        }

        if (!string.Equals(revision.ContentHash, contentHash, StringComparison.Ordinal))
        {
            return DraftRefusal.ContentMismatch;
        }

        var accepted = await db.AgreementAcceptances.AsNoTracking()
            .Where(acceptance => acceptance.RevisionId == revision.RevisionId)
            .Select(acceptance => acceptance.ParticipantEmployeeId)
            .ToListAsync(cancellationToken);
        if (accepted.Contains(employee))
        {
            return DraftRefusal.NotInState;
        }

        var acceptance = new AgreementAcceptance { RevisionId = revision.RevisionId, ContentHash = revision.ContentHash, ParticipantEmployeeId = employee };
        db.AgreementAcceptances.Add(acceptance);
        audit.Append(actor, new AuditEvent(AuditAction.AcceptanceRecorded, AuditRecordType.AgreementAcceptance, acceptance.Id)
        {
            CandidateId = head.Id,
            Revision = revision,
            NewValues = new AuditValues().Set("participantEmployeeId", employee),
            Outcome = AuditOutcome.Accepted,
        });
        var otherParticipant = employee == head.ProtegeEmployeeId ? head.MentorEmployeeId : head.ProtegeEmployeeId;
        notifications.Raise(WorkflowNotifications.AcceptanceRecorded(head, employee, accepted.Contains(otherParticipant)));
        return DraftRefusal.None;
    }

    private async Task<DraftRefusal> ChangeAnnexAsync(Caller actor, Guid employee, MentorSelection head, AnnexChange change, CancellationToken cancellationToken)
    {
        if (!AgreementWorkflow.AdmitsDraftChange(head))
        {
            return DraftRefusal.NotInState;
        }

        var field = change.Field;
        var prior = field == AnnexField.AdminReference ? head.AdminReference : head.AdminRemarks;
        if (string.Equals(prior, change.Value, StringComparison.Ordinal))
        {
            return DraftRefusal.AnnexUnchanged;
        }

        var permitted = new PermittedChange
        {
            CandidateId = head.Id,
            ActorEmployeeId = employee,
            Basis = change.Basis,
            Field = field,
            PriorValue = prior,
            NewValue = change.Value,
        };
        db.PermittedChanges.Add(permitted);
        await db.MentorSelections.Where(row => row.Id == head.Id).ExecuteUpdateAsync(set =>
        {
            if (field == AnnexField.AdminReference)
            {
                set.SetProperty(row => row.AdminReference, change.Value);
            }
            else
            {
                set.SetProperty(row => row.AdminRemarks, change.Value);
            }
        }, cancellationToken);

        var name = field == AnnexField.AdminReference ? "adminReference" : "adminRemarks";
        audit.Append(actor, new AuditEvent(AuditAction.AnnexChanged, AuditRecordType.PermittedChange, permitted.Id)
        {
            // An annex change references the candidate, never a revision: it is not part of any content (ADR-007).
            CandidateId = head.Id,
            PriorValues = new AuditValues().Set(name, prior),
            NewValues = new AuditValues().Set(name, change.Value).Set("basis", change.Basis),
        });
        return DraftRefusal.None;
    }

    /// <summary>
    /// The command protocol of ADR-005: the record check; the employee record the action is attributed to; the version
    /// the caller decided on; one transaction whose first statement claims that version, then the command, its audit
    /// entry and its notification, committed together or not at all. The view is read after the commit.
    /// </summary>
    private async Task<DraftOutcome<AgreementDraftView>> RunAsync(
        ClaimsPrincipal user, Guid id, long? version, Permission permission, Func<Caller, Guid, MentorSelection, Task<DraftRefusal>> command, CancellationToken cancellationToken)
    {
        var candidate = await db.MentorSelections.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (candidate is null)
        {
            return DraftOutcome<AgreementDraftView>.Refused(DraftRefusal.NotFound);
        }

        if (!await authorization.IsPermittedAsync(user, permission, ProtectedRecord.Of(candidate))
            || await callers.ResolveAsync(user, cancellationToken) is not { } actor)
        {
            return DraftOutcome<AgreementDraftView>.Refused(DraftRefusal.Forbidden);
        }

        // A participant is related to the candidate by their employee record; an administrator may not have one yet.
        if (actor.EmployeeId is not { } employee)
        {
            return DraftOutcome<AgreementDraftView>.Refused(DraftRefusal.NoEmployeeRecord);
        }

        if (version is not { } decidedOn)
        {
            return DraftOutcome<AgreementDraftView>.Refused(DraftRefusal.VersionRequired);
        }

        var refusal = await CommitAsync(id, decidedOn, head => command(actor, employee, head), cancellationToken);
        return refusal == DraftRefusal.None
            ? DraftOutcome<AgreementDraftView>.Of(await ViewAsync(id, cancellationToken))
            : DraftOutcome<AgreementDraftView>.Refused(refusal);
    }

    private async Task<DraftRefusal> CommitAsync(Guid id, long decidedOn, Func<MentorSelection, Task<DraftRefusal>> command, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await CandidateVersion.ClaimAsync(db, id, decidedOn, cancellationToken))
        {
            return DraftRefusal.Stale;
        }

        // A refusal leaves the transaction uncommitted, so the claimed version rolls back with it.
        var refusal = await command(await db.MentorSelections.AsNoTracking().SingleAsync(row => row.Id == id, cancellationToken));
        if (refusal != DraftRefusal.None)
        {
            return refusal;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return DraftRefusal.None;
    }

    private async Task<DraftRefusal> ReadableAsync(ClaimsPrincipal user, Guid candidateId, CancellationToken cancellationToken)
    {
        var candidate = await db.MentorSelections.AsNoTracking().SingleOrDefaultAsync(row => row.Id == candidateId, cancellationToken);
        if (candidate is null)
        {
            return DraftRefusal.NotFound;
        }

        return await authorization.IsPermittedAsync(user, AuthorisationMatrix.AgreementDraft.Read, ProtectedRecord.Of(candidate))
            ? DraftRefusal.None
            : DraftRefusal.Forbidden;
    }

    /// <summary>
    /// The draft as one read-only snapshot (REPEATABLE READ): the head every applicability and the step are derived from
    /// and every list it holds describe the same moment, whatever command commits meanwhile.
    /// </summary>
    private async Task<AgreementDraftView> ViewAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await db.Database.ExecuteSqlRawAsync(ReadOnlyTransaction, cancellationToken);
        var head = await db.MentorSelections.AsNoTracking().SingleAsync(row => row.Id == id, cancellationToken);
        var names = await NamesAsync(head, cancellationToken);
        var revisions = await db.AgreementRevisions.AsNoTracking()
            .Where(revision => revision.CandidateId == id)
            .OrderBy(revision => revision.RevisionNumber)
            .Select(revision => new RevisionSummaryView(
                revision.Id, revision.RevisionNumber, revision.ParentRevisionId, revision.ContentHash, revision.CreatedByEmployeeId, revision.CreatedAt, false))
            .ToListAsync(cancellationToken);
        var acceptances = await AcceptancesAsync(db.AgreementRevisions.Where(revision => revision.CandidateId == id), cancellationToken);
        var current = head.CurrentRevisionId is { } currentId
            ? await db.AgreementRevisions.AsNoTracking().SingleAsync(revision => revision.Id == currentId, cancellationToken)
            : null;
        var changes = await db.PermittedChanges.AsNoTracking()
            .Where(change => change.CandidateId == id)
            .OrderBy(change => change.ChangedAt).ThenBy(change => change.Id)
            .Select(change => new PermittedChangeView(change.Id, change.Basis, change.Field, change.PriorValue, change.NewValue, change.ActorEmployeeId, change.ChangedAt))
            .ToListAsync(cancellationToken);
        await snapshot.CommitAsync(cancellationToken);

        var acceptanceViews = acceptances.Select(row => AcceptanceOf(head, row.Acceptance, row.RevisionNumber)).ToList();
        return new AgreementDraftView(
            head.Id,
            head.Status,
            AgreementWorkflow.StepOf(head, acceptances.Select(row => row.Acceptance)),
            new DraftParticipantView(head.ProtegeEmployeeId, names[head.ProtegeEmployeeId]),
            new DraftParticipantView(head.MentorEmployeeId, names[head.MentorEmployeeId]),
            current is null ? null : RevisionOf(head, current, acceptanceViews),
            [.. revisions.Select(revision => revision with { IsCurrent = revision.Id == head.CurrentRevisionId })],
            acceptanceViews,
            new AdministrativeAnnexView(head.AdminReference, head.AdminRemarks, changes),
            head.Version);
    }

    private async Task<AgreementRevisionView?> RevisionViewAsync(Guid candidateId, Guid revisionId, CancellationToken cancellationToken)
    {
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await db.Database.ExecuteSqlRawAsync(ReadOnlyTransaction, cancellationToken);
        var head = await db.MentorSelections.AsNoTracking().SingleAsync(row => row.Id == candidateId, cancellationToken);
        var revision = await db.AgreementRevisions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == revisionId && row.CandidateId == candidateId, cancellationToken);
        var acceptances = revision is null
            ? []
            : await AcceptancesAsync(db.AgreementRevisions.Where(row => row.Id == revisionId), cancellationToken);
        await snapshot.CommitAsync(cancellationToken);

        return revision is null
            ? null
            : RevisionOf(head, revision, [.. acceptances.Select(row => AcceptanceOf(head, row.Acceptance, row.RevisionNumber))]);
    }

    /// <summary>The acceptances of <paramref name="revisions"/>, oldest first, each with the number of the revision it accepted.</summary>
    private async Task<List<(AgreementAcceptance Acceptance, int RevisionNumber)>> AcceptancesAsync(IQueryable<AgreementRevision> revisions, CancellationToken cancellationToken)
    {
        var rows = await db.AgreementAcceptances.AsNoTracking()
            .Join(revisions, acceptance => acceptance.RevisionId, revision => revision.Id, (acceptance, revision) => new { acceptance, revision.RevisionNumber })
            .OrderBy(row => row.acceptance.AcceptedAt).ThenBy(row => row.acceptance.Id)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(row => (row.acceptance, row.RevisionNumber))];
    }

    private Task<Dictionary<Guid, string>> NamesAsync(MentorSelection head, CancellationToken cancellationToken) =>
        db.Employees.AsNoTracking()
            .Where(employee => employee.Id == head.ProtegeEmployeeId || employee.Id == head.MentorEmployeeId)
            .ToDictionaryAsync(employee => employee.Id, employee => employee.DisplayName, cancellationToken);

    private static AgreementRevisionView RevisionOf(MentorSelection head, AgreementRevision revision, IEnumerable<AcceptanceView> acceptances) => new(
        revision.Id,
        revision.CandidateId,
        revision.RevisionNumber,
        revision.ParentRevisionId,
        revision.ContentHash,
        revision.CreatedByEmployeeId,
        revision.CreatedAt,
        revision.Id == head.CurrentRevisionId,
        AgreementContent.Parse(revision.Content),
        [.. acceptances.Where(acceptance => acceptance.RevisionId == revision.Id)]);

    private static AcceptanceView AcceptanceOf(MentorSelection head, AgreementAcceptance acceptance, int revisionNumber) => new(
        acceptance.Id,
        acceptance.RevisionId,
        revisionNumber,
        acceptance.ContentHash,
        acceptance.ParticipantEmployeeId,
        CandidateLifecycle.ParticipantOf(head, acceptance.ParticipantEmployeeId),
        acceptance.AcceptedAt,
        ApplicabilityRule.Of(head.Status, head.CurrentRevisionId, acceptance.RevisionId));
}

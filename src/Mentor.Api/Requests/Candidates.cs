using System.Security.Claims;
using Mentor.Api.Audit;
using Mentor.Api.Authorisation;
using Mentor.Api.Identity;
using Mentor.Api.Notifications;
using Mentor.Core.Agreements;
using Mentor.Core.Audit;
using Mentor.Core.MentorRegistry;
using Mentor.Core.Notifications;
using Mentor.Core.Requests;
using Mentor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Mentor.Api.Requests;

/// <summary>
/// Candidate pairing (COMP-MENTORSHIP-REQUEST, ADR-015): the protégé selects a discoverable mentor for their request,
/// the mentor accepts or declines, each participant records only their own affirmative willingness to proceed, and
/// either participant withdraws before formation. A candidate's participants are fixed, so substituting one is a
/// withdrawal and a new selection with fresh decisions (BR-021). Every decision is a versioned command (ADR-005) that
/// writes its audit entry (ADR-007) and the other participant's notification (ADR-009) in its own transaction.
/// </summary>
public sealed class Candidates(MentorDbContext db, CallerResolver callers, IAuthorizationService authorization, AuditTrail audit, NotificationEvents notifications)
{
    /// <summary>
    /// The protégé's selection of a discoverable mentor for their own request (FR-018): a new INVITED candidate, with
    /// nothing about the mentor's existing relationships taken into account (FR-009, BR-002). A request holds one
    /// candidate at a time, and none once a relationship has been formed from it (FR-032).
    /// </summary>
    public async Task<PairingOutcome<CandidateView>> SelectAsync(ClaimsPrincipal user, Guid requestId, Guid mentorEmployeeId, CancellationToken cancellationToken)
    {
        var request = await db.MentorshipRequests.AsNoTracking().SingleOrDefaultAsync(row => row.Id == requestId, cancellationToken);
        if (request is null)
        {
            return Refused(PairingRefusal.NotFound);
        }

        if (!await authorization.IsPermittedAsync(user, AuthorisationMatrix.Candidate.Create, ProtectedRecord.Of(request, []))
            || await callers.ResolveAsync(user, cancellationToken) is not { } caller)
        {
            return Refused(PairingRefusal.Forbidden);
        }

        if (mentorEmployeeId == request.ProtegeEmployeeId)
        {
            return Refused(PairingRefusal.OwnRequest);
        }

        // Discoverable means approved (FR-008); an approval is never taken back, so this holds for the whole command.
        if (!await db.MentorProfiles.AnyAsync(profile => profile.EmployeeId == mentorEmployeeId && profile.ApprovalStatus == MentorApprovalStatus.Approved, cancellationToken))
        {
            return Refused(PairingRefusal.NotDiscoverable);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Selections on one request queue on its row, so the second of two racing selections sees the first's candidate.
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM mentor.mentorship_request WHERE id = {requestId} FOR UPDATE", cancellationToken);
        var disposition = await db.MentorshipRequests.Where(row => row.Id == requestId).Select(row => row.Disposition).SingleAsync(cancellationToken);
        var standing = await db.MentorSelections
            .Where(CandidateQueries.Standing)
            .Where(candidate => candidate.RequestId == requestId)
            .Select(candidate => candidate.Status)
            .ToListAsync(cancellationToken);
        if (disposition != RequestDisposition.Open || standing.Contains(CandidateStatus.Formed))
        {
            return Refused(PairingRefusal.RequestClosed);
        }

        if (standing.Count > 0)
        {
            return Refused(PairingRefusal.CandidateInProgress);
        }

        var candidate = new MentorSelection
        {
            RequestId = requestId,
            ProtegeEmployeeId = request.ProtegeEmployeeId,
            MentorEmployeeId = mentorEmployeeId,
            Status = CandidateStatus.Invited,
            MentorResponse = MentorResponse.Pending,
        };
        db.MentorSelections.Add(candidate);
        audit.Append(caller, new AuditEvent(AuditAction.InvitationSent, AuditRecordType.Candidate, candidate.Id)
        {
            CandidateId = candidate.Id,
            NewValues = new AuditValues()
                .Set("requestId", candidate.RequestId)
                .Set("protegeEmployeeId", candidate.ProtegeEmployeeId)
                .Set("mentorEmployeeId", candidate.MentorEmployeeId)
                .Set("status", candidate.Status)
                .Set("mentorResponse", candidate.MentorResponse),
        });
        notifications.Raise(WorkflowNotifications.InvitationSent(candidate));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return PairingOutcome<CandidateView>.Of(await ViewAsync(candidate.Id, cancellationToken));
    }

    public async Task<PairingOutcome<CandidateView>> ReadAsync(ClaimsPrincipal user, Guid id, CancellationToken cancellationToken)
    {
        var candidate = await db.MentorSelections.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (candidate is null)
        {
            return Refused(PairingRefusal.NotFound);
        }

        if (!await authorization.IsPermittedAsync(user, AuthorisationMatrix.Candidate.Read, ProtectedRecord.Of(candidate)))
        {
            return Refused(PairingRefusal.Forbidden);
        }

        return PairingOutcome<CandidateView>.Of(await ViewAsync(id, cancellationToken));
    }

    /// <summary>The candidates the caller takes part in, as protégé or as mentor, oldest first.</summary>
    public async Task<IReadOnlyList<CandidateView>> ListOwnAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (await callers.ResolveAsync(user, cancellationToken) is not { EmployeeId: { } employee })
        {
            return [];
        }

        return await CandidateQueries.ReadableAsync(db, authorization, user,
            db.MentorSelections.Where(candidate => candidate.ProtegeEmployeeId == employee || candidate.MentorEmployeeId == employee), cancellationToken);
    }

    /// <summary>The mentor accepts or declines the pending invitation (FR-019). A declined candidate has ended.</summary>
    public Task<PairingOutcome<CandidateView>> RespondAsync(ClaimsPrincipal user, Guid id, long? version, bool accept, CancellationToken cancellationToken) =>
        DecideAsync(user, id, version, AuthorisationMatrix.Candidate.Respond, async (actor, _, head) =>
        {
            if (!CandidateLifecycle.AdmitsResponse(head))
            {
                return false;
            }

            var (status, response) = accept
                ? (CandidateStatus.Accepted, MentorResponse.Accepted)
                : (CandidateStatus.Declined, MentorResponse.Declined);
            await db.MentorSelections.Where(row => row.Id == id).ExecuteUpdateAsync(set => set
                .SetProperty(row => row.Status, status)
                .SetProperty(row => row.MentorResponse, response)
                .SetProperty(row => row.MentorRespondedAt, _ => DateTimeOffset.UtcNow), cancellationToken);
            var after = await HeadAsync(id, cancellationToken);
            audit.Append(actor, new AuditEvent(AuditAction.MentorResponded, AuditRecordType.Candidate, id)
            {
                CandidateId = id,
                PriorValues = new AuditValues().Set("status", head.Status).Set("mentorResponse", head.MentorResponse).Set("mentorRespondedAt", head.MentorRespondedAt),
                NewValues = new AuditValues().Set("status", after.Status).Set("mentorResponse", after.MentorResponse).Set("mentorRespondedAt", after.MentorRespondedAt),
                Outcome = accept ? AuditOutcome.Accepted : AuditOutcome.Declined,
            });
            notifications.Raise(WorkflowNotifications.MentorResponded(after));
            return true;
        }, cancellationToken);

    /// <summary>
    /// The caller records their own affirmative willingness to proceed (FR-020), once; the second participant's moves the
    /// candidate on to DRAFTING in the same command (ADR-015). Nobody records it for someone else.
    /// </summary>
    public Task<PairingOutcome<CandidateView>> RecordWillingnessAsync(ClaimsPrincipal user, Guid id, long? version, CancellationToken cancellationToken) =>
        DecideAsync(user, id, version, AuthorisationMatrix.Candidate.RecordWillingness, async (actor, employee, head) =>
        {
            var participant = CandidateLifecycle.ParticipantOf(head, employee);
            if (!CandidateLifecycle.AdmitsWillingness(head, participant))
            {
                return false;
            }

            var drafting = CandidateLifecycle.CompletesMutualWillingness(head, participant);
            await db.MentorSelections.Where(row => row.Id == id).ExecuteUpdateAsync(set =>
            {
                if (participant == Participant.Protege)
                {
                    set.SetProperty(row => row.ProtegeWillingAt, _ => DateTimeOffset.UtcNow);
                }
                else
                {
                    set.SetProperty(row => row.MentorWillingAt, _ => DateTimeOffset.UtcNow);
                }

                // Both willingness columns set while ACCEPTED would break ck_mentor_selection_status_response: the
                // status moves in the same statement.
                if (drafting)
                {
                    set.SetProperty(row => row.Status, CandidateStatus.Drafting);
                }
            }, cancellationToken);
            var after = await HeadAsync(id, cancellationToken);
            var (field, willingAt) = participant == Participant.Protege
                ? ("protegeWillingAt", after.ProtegeWillingAt)
                : ("mentorWillingAt", after.MentorWillingAt);
            audit.Append(actor, new AuditEvent(AuditAction.WillingnessRecorded, AuditRecordType.Candidate, id)
            {
                CandidateId = id,
                PriorValues = new AuditValues().Set("status", head.Status).Set(field, (DateTimeOffset?)null),
                NewValues = new AuditValues().Set("status", after.Status).Set(field, willingAt),
            });
            notifications.Raise(WorkflowNotifications.WillingnessRecorded(after, employee));
            return true;
        }, cancellationToken);

    /// <summary>
    /// Either participant withdraws before formation (ADR-015). Withdrawal is terminal: the candidate keeps its withdrawn
    /// disposition, never becomes a relationship, and every acceptance and decision of its current revision stops
    /// applying, which the audit entry names (ADR-004, ADR-007).
    /// </summary>
    public Task<PairingOutcome<CandidateView>> WithdrawAsync(ClaimsPrincipal user, Guid id, long? version, CancellationToken cancellationToken) =>
        DecideAsync(user, id, version, AuthorisationMatrix.Candidate.Withdraw, async (actor, employee, head) =>
        {
            if (!CandidateLifecycle.AdmitsWithdrawal(head))
            {
                return false;
            }

            // Read under the version lock, so the evidence named is exactly the evidence that stops applying.
            var invalidated = await audit.InapplicableEvidenceAsync(head.CurrentRevisionId, cancellationToken);
            RevisionReference? revision = head.CurrentRevisionId is { } revisionId
                ? await db.AgreementRevisions.Where(row => row.Id == revisionId).Select(row => new RevisionReference(row.Id, row.ContentHash)).SingleAsync(cancellationToken)
                : null;
            await db.MentorSelections.Where(row => row.Id == id).ExecuteUpdateAsync(set => set
                .SetProperty(row => row.Status, CandidateStatus.Withdrawn)
                .SetProperty(row => row.WithdrawnByEmployeeId, (Guid?)employee)
                .SetProperty(row => row.WithdrawnAt, _ => DateTimeOffset.UtcNow), cancellationToken);
            var after = await HeadAsync(id, cancellationToken);
            audit.Append(actor, new AuditEvent(AuditAction.CandidateWithdrawn, AuditRecordType.Candidate, id)
            {
                CandidateId = id,
                Revision = revision,
                PriorValues = new AuditValues().Set("status", head.Status).Set("withdrawnByEmployeeId", head.WithdrawnByEmployeeId).Set("withdrawnAt", head.WithdrawnAt),
                NewValues = new AuditValues().Set("status", after.Status).Set("withdrawnByEmployeeId", after.WithdrawnByEmployeeId).Set("withdrawnAt", after.WithdrawnAt),
                Outcome = AuditOutcome.Withdrawn,
                Invalidated = invalidated,
            });
            notifications.Raise(WorkflowNotifications.CandidateWithdrawn(after));
            return true;
        }, cancellationToken);

    /// <summary>
    /// The command protocol of ADR-005: the record check; the version the caller decided on; one transaction whose first
    /// statement takes the candidate's version, so a decision taken first by someone else refuses this one; then the
    /// transition, its audit entry and its notification, committed together or not at all.
    /// </summary>
    private async Task<PairingOutcome<CandidateView>> DecideAsync(
        ClaimsPrincipal user, Guid id, long? version, Permission permission, Func<Caller, Guid, MentorSelection, Task<bool>> transition, CancellationToken cancellationToken)
    {
        var candidate = await db.MentorSelections.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (candidate is null)
        {
            return Refused(PairingRefusal.NotFound);
        }

        // Every grant of these decisions is a participant's, so a caller the matrix admits has the employee record the
        // audit entry names.
        if (!await authorization.IsPermittedAsync(user, permission, ProtectedRecord.Of(candidate))
            || await callers.ResolveAsync(user, cancellationToken) is not { EmployeeId: { } employee } actor)
        {
            return Refused(PairingRefusal.Forbidden);
        }

        if (version is not { } decidedOn)
        {
            return Refused(PairingRefusal.VersionRequired);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Zero rows means another decision committed first. Otherwise the row lock this takes holds every later read of
        // the transaction to a state no other command can change before it commits.
        var claimed = await db.MentorSelections
            .Where(row => row.Id == id && row.Version == decidedOn)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.Version, row => row.Version + 1), cancellationToken);
        if (claimed == 0)
        {
            return Refused(PairingRefusal.Stale);
        }

        if (!await transition(actor, employee, await HeadAsync(id, cancellationToken)))
        {
            return Refused(PairingRefusal.NotInState);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return PairingOutcome<CandidateView>.Of(await ViewAsync(id, cancellationToken));
    }

    private Task<MentorSelection> HeadAsync(Guid id, CancellationToken cancellationToken) =>
        db.MentorSelections.AsNoTracking().SingleAsync(row => row.Id == id, cancellationToken);

    private async Task<CandidateView> ViewAsync(Guid id, CancellationToken cancellationToken) =>
        (await CandidateQueries.WithNamesAsync(db, db.MentorSelections.Where(candidate => candidate.Id == id), cancellationToken)).Single().View;

    private static PairingOutcome<CandidateView> Refused(PairingRefusal refusal) => PairingOutcome<CandidateView>.Refused(refusal);
}

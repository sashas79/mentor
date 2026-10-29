using Mentor.Api.Identity;
using Mentor.Core.Audit;
using Mentor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Mentor.Api.Audit;

/// <summary>
/// The write side of the audit trail (COMP-AUDIT-TRAIL, ADR-007). A command appends its entries to the same
/// <see cref="MentorDbContext"/> it changes business state through, inside its transaction, so that one SaveChanges
/// commits the action and its evidence together or neither. An entry takes its actor and identity scheme from the
/// authenticated caller, its time from the database and its provenance from the application; a command supplies only
/// what it did (<see cref="AuditEvent"/>). Nothing updates or deletes an entry: the database refuses it (ADR-013).
/// </summary>
public sealed class AuditTrail(MentorDbContext db)
{
    /// <summary>
    /// Adds the entry of an action by <paramref name="actor"/> to the unit of work. It is written when the command saves
    /// its changes and committed with them, never alone. The entry carries its identifier at once and its time after
    /// the save.
    /// </summary>
    public AuditEntry Append(Caller actor, AuditEvent @event)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(@event);
        if (actor.EmployeeId is not { } employee)
        {
            throw new InvalidOperationException(
                "The caller has no employee record, so no action of theirs can be attributed; a command creates the record before it changes anything.");
        }

        if (@event.Outcome is AuditOutcome.ResetToStep1 or AuditOutcome.Withdrawn && @event.Invalidated is null)
        {
            throw new ArgumentException(
                $"An entry with outcome {@event.Outcome} names the evidence it made inapplicable; give InapplicableEvidence.None when there was none.",
                nameof(@event));
        }

        var entry = new AuditEntry
        {
            ActorEmployeeId = employee,
            IdentityScheme = actor.Scheme,
            Action = @event.Action,
            RecordType = @event.RecordType,
            RecordId = @event.RecordId,
            PriorValues = AuditValuesJson.Serialize(@event.PriorValues),
            NewValues = AuditValuesJson.Serialize(@event.NewValues),
            CandidateId = @event.CandidateId,
            RevisionId = @event.Revision?.RevisionId,
            ContentHash = @event.Revision?.ContentHash,
            Outcome = @event.Outcome,
            InvalidatedAcceptanceIds = @event.Invalidated?.AcceptanceIds.ToArray(),
            InvalidatedDecisionIds = @event.Invalidated?.DecisionIds.ToArray(),
            Provenance = AuditProvenance.Application,
        };
        db.AuditEntries.Add(entry);
        return entry;
    }

    /// <summary>
    /// The acceptances and formation decisions bound to a revision: what replacing it, or withdrawing its candidate,
    /// makes inapplicable (ADR-004). A command reads it inside its transaction, after taking the candidate's version
    /// lock, and gives it to its RESET_TO_STEP_1 or WITHDRAWN entry. None for a candidate without a revision yet.
    /// </summary>
    public async Task<InapplicableEvidence> InapplicableEvidenceAsync(Guid? revisionId, CancellationToken cancellationToken)
    {
        if (revisionId is null)
        {
            return InapplicableEvidence.None;
        }

        var acceptances = await db.AgreementAcceptances.AsNoTracking()
            .Where(acceptance => acceptance.RevisionId == revisionId)
            .OrderBy(acceptance => acceptance.AcceptedAt).ThenBy(acceptance => acceptance.Id)
            .Select(acceptance => acceptance.Id)
            .ToListAsync(cancellationToken);
        var decisions = await db.FormationDecisions.AsNoTracking()
            .Where(decision => decision.RevisionId == revisionId)
            .OrderBy(decision => decision.DecidedAt).ThenBy(decision => decision.Id)
            .Select(decision => decision.Id)
            .ToListAsync(cancellationToken);
        return new InapplicableEvidence(acceptances, decisions);
    }
}

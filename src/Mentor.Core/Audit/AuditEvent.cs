using Mentor.Core.Agreements;

namespace Mentor.Core.Audit;

/// <summary>
/// What a command asks the trail to record about a business-critical action (ADR-007): the action, the record it
/// affected, the changed fields before and after, the decision context, the outcome and the evidence it made
/// inapplicable. The actor, the identity scheme, the time and the provenance are not the command's to say: the trail
/// takes the actor from the authenticated caller, the time from the database and the provenance from the fact that the
/// application is writing.
/// </summary>
public sealed record AuditEvent(AuditAction Action, AuditRecordType RecordType, Guid? RecordId)
{
    public AuditValues? PriorValues { get; init; }

    public AuditValues? NewValues { get; init; }

    /// <summary>The candidate the action concerns: every candidate, draft and formation action names it, an annex change included.</summary>
    public Guid? CandidateId { get; init; }

    /// <summary>The revision, with its content hash, the action was taken on.</summary>
    public RevisionReference? Revision { get; init; }

    /// <summary>An <see cref="AuditOutcome"/>, or none when the action has only one.</summary>
    public string? Outcome { get; init; }

    /// <summary>
    /// What a replacement revision or a withdrawal made inapplicable (ADR-004). Required with <see cref="AuditOutcome.ResetToStep1"/>
    /// and <see cref="AuditOutcome.Withdrawn"/>, as <see cref="InapplicableEvidence.None"/> when there was nothing.
    /// </summary>
    public InapplicableEvidence? Invalidated { get; init; }
}

/// <summary>The acceptances and formation decisions a replacement revision or a withdrawal made inapplicable (ADR-004).</summary>
public sealed record InapplicableEvidence(IReadOnlyList<Guid> AcceptanceIds, IReadOnlyList<Guid> DecisionIds)
{
    public static readonly InapplicableEvidence None = new([], []);
}

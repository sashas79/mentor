namespace Mentor.Core.Audit;

/// <summary>The business-critical actions the trail records (ADR-007, ADR-011).</summary>
public enum AuditAction
{
    MentorApplicationDecided,
    InvitationSent,
    MentorResponded,
    WillingnessRecorded,
    CandidateWithdrawn,
    RevisionCreated,
    AcceptanceRecorded,
    AnnexChanged,
    FormationDecided,
    AttributeChanged,
    GoalProgressSubmitted,
    GoalCompletionDecided,
    LegacyEntry,
    RegisterExported,
}

/// <summary>Whether an entry records an actual application event or a claim taken from the legacy source.</summary>
public enum AuditProvenance
{
    Application,
    LegacySource,
}

/// <summary>How the actor was authenticated: Entra or the labelled simulation in the application, a database account for legacy entry.</summary>
public enum IdentityScheme
{
    Entra,
    Simulated,
    Database,
}

/// <summary>
/// One append-only entry of the administration audit (DR-014, CONTRACT-AUDIT-ENTRY), written in the same transaction
/// as the action it records.
/// </summary>
public sealed class AuditEntry
{
    public Guid Id { get; init; }

    public Guid ActorEmployeeId { get; init; }

    public IdentityScheme IdentityScheme { get; init; }

    public AuditAction Action { get; init; }

    public AuditRecordType RecordType { get; init; }

    public Guid? RecordId { get; init; }

    /// <summary>The changed fields before the action, as JSON.</summary>
    public string? PriorValues { get; init; }

    /// <summary>The changed fields after the action, as JSON.</summary>
    public string? NewValues { get; init; }

    public Guid? CandidateId { get; init; }

    public Guid? RevisionId { get; init; }

    public string? ContentHash { get; init; }

    /// <summary>For example RESET_TO_STEP_1, APPROVED, REJECTED or WITHDRAWN.</summary>
    public string? Outcome { get; init; }

    /// <summary>The acceptances a replacement revision or a withdrawal made inapplicable.</summary>
    public Guid[]? InvalidatedAcceptanceIds { get; init; }

    /// <summary>The formation decisions a replacement revision or a withdrawal made inapplicable.</summary>
    public Guid[]? InvalidatedDecisionIds { get; init; }

    public AuditProvenance Provenance { get; init; }

    public string? SourceFile { get; init; }

    public int? SourceRow { get; init; }

    public DateTimeOffset OccurredAt { get; private set; }
}

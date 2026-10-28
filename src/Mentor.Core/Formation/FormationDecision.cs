namespace Mentor.Core.Formation;

public enum FormationOutcome
{
    Approved,
    Rejected,
}

/// <summary>
/// A Talent Management administrator's formation decision, bound to one candidate and the exact revision reviewed
/// (DR-008). At most one approval exists per candidate (ADR-005).
/// </summary>
public sealed class FormationDecision
{
    public Guid Id { get; init; }

    public Guid CandidateId { get; init; }

    public Guid RevisionId { get; init; }

    public required string ContentHash { get; init; }

    public FormationOutcome Outcome { get; init; }

    public Guid AdministratorEmployeeId { get; init; }

    public DateTimeOffset DecidedAt { get; private set; }
}

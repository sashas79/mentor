namespace Mentor.Core.Legacy;

/// <summary>
/// Maps the database account of a Talent Management administrator to their employee identity, so that every legacy
/// entry made under that account is attributed to them (ADR-008).
/// </summary>
public sealed class LegacyEntryAuthorisation
{
    public Guid Id { get; init; }

    public Guid EmployeeId { get; init; }

    public required string DatabaseAccount { get; init; }

    public DateTimeOffset AuthorisedAt { get; private set; }
}

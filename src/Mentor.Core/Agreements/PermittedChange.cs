namespace Mentor.Core.Agreements;

/// <summary>The permission basis of a no-reset change (ADR-004, BR-023).</summary>
public enum PermittedChangeBasis
{
    AdministrativeReference,
    AdministrativeRemark,
}

/// <summary>The annex field a permitted change writes; each basis allows exactly one field.</summary>
public enum AnnexField
{
    AdminReference,
    AdminRemarks,
}

/// <summary>
/// The record of a Talent Management administrator's change to a candidate's administrative annex, the only change that
/// does not reset the draft workflow (FR-068). It touches no revision, hash or acceptance.
/// </summary>
public sealed class PermittedChange
{
    public Guid Id { get; init; }

    public Guid CandidateId { get; init; }

    public Guid ActorEmployeeId { get; init; }

    public PermittedChangeBasis Basis { get; init; }

    public AnnexField Field { get; init; }

    public string? PriorValue { get; init; }

    public string? NewValue { get; init; }

    public DateTimeOffset ChangedAt { get; private set; }
}

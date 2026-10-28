namespace Mentor.Core.Relationships;

/// <summary>Whether a relationship was formed in the application or entered from the historical tracker (ADR-008).</summary>
public enum Provenance
{
    Digital,
    Legacy,
}

public enum RelationshipState
{
    InProgress,
    OnHold,
    Complete,
}

/// <summary>Blank is a business value, not an absent one (FR-045).</summary>
public enum RelationshipStatus
{
    Blank,
    NeedsReassigning,
}

/// <summary>The IsActive values (FR-046).</summary>
public enum ActiveFlag
{
    Y,
    N,
}

public enum DurationCategory
{
    SixMonths,
    TwelveMonths,
    TwelvePlusMonths,
}

/// <summary>
/// A mentorship relationship (DR-005). State, Status and IsActive are independent attributes (ADR-006): a digital row
/// always holds all three, a legacy row holds null for a value its source left unresolved. A legacy row has no
/// candidate and carries its source reference, verbatim source text and the administrator who entered it.
/// </summary>
public sealed class MentorshipRelationship
{
    public Guid Id { get; init; }

    public Provenance Provenance { get; init; }

    /// <summary>The candidate a digital relationship was formed from; none for a legacy row.</summary>
    public Guid? CandidateId { get; init; }

    public Guid ProtegeEmployeeId { get; init; }

    public Guid MentorEmployeeId { get; init; }

    public RelationshipState? State { get; set; }

    public RelationshipStatus? Status { get; set; }

    public ActiveFlag? IsActive { get; set; }

    public DurationCategory? DurationCategory { get; init; }

    public DateOnly? StartDate { get; init; }

    public DateOnly? EndDate { get; init; }

    public string? SourceFile { get; init; }

    public int? SourceRow { get; init; }

    public string? SourceMentorText { get; init; }

    public string? SourceProtegeText { get; init; }

    public string? SourceActiveText { get; init; }

    public string? SourceDurationText { get; init; }

    public string? SourceStartText { get; init; }

    public string? SourceEndText { get; init; }

    public Guid? EnteredByEmployeeId { get; init; }

    public long Version { get; set; }

    /// <summary>Formation time of a digital row; actual entry time of a legacy row.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }
}

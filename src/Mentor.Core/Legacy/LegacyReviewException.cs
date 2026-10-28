namespace Mentor.Core.Legacy;

public enum LegacyReviewReason
{
    /// <summary>A participant could not be resolved against the directory; the row is kept out of the relationship table.</summary>
    UnresolvedPerson,

    /// <summary>The source row names no mentor; the row is kept out of the relationship table.</summary>
    NoMentor,

    /// <summary>An entered relationship left State, Status or IsActive unresolved.</summary>
    UnresolvedValue,
}

public enum RelationshipAttribute
{
    State,
    Status,
    IsActive,
}

/// <summary>
/// Insert-only evidence of a historical source condition that could not be entered as fact (ADR-008, DR-016). Whether
/// an unresolved value is still unresolved is read from the relationship, never from this row.
/// </summary>
public sealed class LegacyReviewException
{
    public Guid Id { get; init; }

    public required string SourceFile { get; init; }

    public int SourceRow { get; init; }

    public LegacyReviewReason Reason { get; init; }

    public Guid? RelationshipId { get; init; }

    public RelationshipAttribute? Attribute { get; init; }

    public string? SourceMentorText { get; init; }

    public string? SourceProtegeText { get; init; }

    public string? SourceActiveText { get; init; }

    public string? SourceDurationText { get; init; }

    public string? SourceStartText { get; init; }

    public string? SourceEndText { get; init; }

    public Guid EnteredByEmployeeId { get; init; }

    public DateTimeOffset CreatedAt { get; private set; }
}

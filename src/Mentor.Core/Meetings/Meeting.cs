namespace Mentor.Core.Meetings;

public enum MeetingKind
{
    /// <summary>A proposed introduction between the participants of a candidate.</summary>
    Introductory,

    /// <summary>A planned session of a formed relationship.</summary>
    RecurringSession,
}

/// <summary>
/// A proposed or planned meeting (DR-013, ADR-010). There is deliberately no field for notes, summaries or anything
/// said in the meeting, and no calendar link.
/// </summary>
public sealed class Meeting
{
    public Guid Id { get; init; }

    public MeetingKind Kind { get; init; }

    public Guid? CandidateId { get; init; }

    public Guid? RelationshipId { get; init; }

    public DateTimeOffset? ScheduledAt { get; set; }

    public bool ArrangedOutsideApplication { get; set; }

    /// <summary>The agreed frequency, copied from the agreement content.</summary>
    public int? FrequencyWeeks { get; init; }

    /// <summary>The agreed approximate duration, copied from the agreement content.</summary>
    public int? DurationMinutes { get; init; }

    public Guid CreatedByEmployeeId { get; init; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }
}

namespace Mentor.Core.Goals;

public enum GoalDecision
{
    Approved,
    Rejected,
}

/// <summary>
/// The mentor's decision on a completion request (DR-011). A request without one is pending; a rejection leaves the
/// goal incomplete and carries no reason.
/// </summary>
public sealed class GoalApproval
{
    public Guid Id { get; init; }

    /// <summary>The progress entry that asked for completion.</summary>
    public Guid ProgressId { get; init; }

    public Guid RelationshipId { get; init; }

    public Guid MentorEmployeeId { get; init; }

    public GoalDecision Decision { get; init; }

    /// <summary>
    /// Always true. Stored with the progress reference, it lets the database refuse a decision on an entry that is not a
    /// completion request.
    /// </summary>
    public bool CompletionRequested { get; private set; } = true;

    public DateTimeOffset DecidedAt { get; private set; }
}

namespace Mentor.Core.Goals;

/// <summary>
/// A dated progress update the protégé submits against a goal, optionally asking for completion (DR-010). Submitting it
/// never completes the goal.
/// </summary>
public sealed class GoalProgress
{
    public Guid Id { get; init; }

    public Guid GoalId { get; init; }

    public Guid RelationshipId { get; init; }

    public Guid ProtegeEmployeeId { get; init; }

    public required string ProgressUpdate { get; init; }

    public bool CompletionRequested { get; init; }

    public DateTimeOffset SubmittedAt { get; private set; }
}

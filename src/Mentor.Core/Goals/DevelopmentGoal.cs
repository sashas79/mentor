namespace Mentor.Core.Goals;

public enum GoalCompletionStatus
{
    Incomplete,
    Complete,
}

/// <summary>
/// One agreed development goal of a formed relationship (DR-009). Its wording is fixed; only the completion status
/// changes, and it becomes Complete only with the mentor's approval of a completion request.
/// </summary>
public sealed class DevelopmentGoal
{
    public Guid Id { get; init; }

    public Guid RelationshipId { get; init; }

    public Guid FormedAgreementId { get; init; }

    /// <summary>The goal's place in the agreed goals, from 1.</summary>
    public int Position { get; init; }

    public required string AgreedDescription { get; init; }

    public GoalCompletionStatus CompletionStatus { get; set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }
}

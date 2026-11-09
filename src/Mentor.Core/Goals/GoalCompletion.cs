namespace Mentor.Core.Goals;

/// <summary>
/// The standing of a completion request (DR-011), derived from its decision and never stored: pending while the mentor
/// has not decided it, then approved or rejected for good.
/// </summary>
public enum CompletionStanding
{
    Pending,
    Approved,
    Rejected,
}

/// <summary>Why the protégé cannot ask for a goal's completion now.</summary>
public enum CompletionRequestBlock
{
    None,

    /// <summary>The goal is complete; formal completion is given once.</summary>
    GoalComplete,

    /// <summary>A completion request of the goal awaits the mentor's decision.</summary>
    RequestPending,
}

/// <summary>Why the mentor cannot decide a progress entry.</summary>
public enum CompletionDecisionBlock
{
    None,

    /// <summary>The entry is ordinary progress, which needs no decision (BR-005).</summary>
    NotACompletionRequest,

    /// <summary>The request has its decision; a decision is recorded once and never changed.</summary>
    AlreadyDecided,
}

/// <summary>
/// The completion rules of a development goal (BR-005, DR-009, DR-010, DR-011). Progress needs no approval. The protégé
/// asks for completion with a progress entry flagged as a completion request, while the goal is incomplete and none of
/// its requests is pending. The mentor decides a request once, approving or rejecting it with no reason. Only an approval
/// completes the goal, and a rejection leaves it as it was. Nothing here is stored: each answer is derived from the
/// goal, its entries and their decisions as the caller read them.
/// </summary>
public static class GoalCompletion
{
    /// <summary>
    /// The standing of <paramref name="entry"/> given <paramref name="decision"/>, the decision recorded on it if any:
    /// none for ordinary progress, otherwise pending until decided.
    /// </summary>
    public static CompletionStanding? StandingOf(GoalProgress entry, GoalApproval? decision)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (decision is not null && decision.ProgressId != entry.Id)
        {
            throw new ArgumentException("The decision is not a decision on the entry.", nameof(decision));
        }

        if (!entry.CompletionRequested)
        {
            return null;
        }

        return decision?.Decision switch
        {
            null => CompletionStanding.Pending,
            GoalDecision.Approved => CompletionStanding.Approved,
            GoalDecision.Rejected => CompletionStanding.Rejected,
            var unknown => throw new ArgumentOutOfRangeException(nameof(decision), unknown, "Not a goal decision."),
        };
    }

    /// <summary>
    /// The completion request of <paramref name="goal"/> that awaits the mentor's decision, if any: a request among
    /// <paramref name="entries"/> that none of <paramref name="decisions"/> decides.
    /// </summary>
    public static GoalProgress? PendingRequestOf(DevelopmentGoal goal, IEnumerable<GoalProgress> entries, IEnumerable<GoalApproval> decisions)
    {
        ArgumentNullException.ThrowIfNull(goal);
        var decided = decisions.Select(decision => decision.ProgressId).ToHashSet();
        return entries
            .Where(entry => entry.GoalId == goal.Id && entry.CompletionRequested && !decided.Contains(entry.Id))
            .OrderBy(entry => entry.SubmittedAt).ThenBy(entry => entry.Id)
            .FirstOrDefault();
    }

    /// <summary>Whether the protégé may ask for the goal's completion now.</summary>
    public static CompletionRequestBlock CheckRequest(DevelopmentGoal goal, IEnumerable<GoalProgress> entries, IEnumerable<GoalApproval> decisions)
    {
        ArgumentNullException.ThrowIfNull(goal);
        if (goal.CompletionStatus == GoalCompletionStatus.Complete)
        {
            return CompletionRequestBlock.GoalComplete;
        }

        return PendingRequestOf(goal, entries, decisions) is null ? CompletionRequestBlock.None : CompletionRequestBlock.RequestPending;
    }

    /// <summary>Whether the mentor may decide <paramref name="entry"/>, given the decision already recorded on it if any.</summary>
    public static CompletionDecisionBlock CheckDecision(GoalProgress entry, GoalApproval? decision) =>
        StandingOf(entry, decision) switch
        {
            null => CompletionDecisionBlock.NotACompletionRequest,
            CompletionStanding.Pending => CompletionDecisionBlock.None,
            _ => CompletionDecisionBlock.AlreadyDecided,
        };

    /// <summary>The completion status a decision leaves the goal in: complete on approval, unchanged on rejection.</summary>
    public static GoalCompletionStatus StatusAfter(DevelopmentGoal goal, GoalDecision decision)
    {
        ArgumentNullException.ThrowIfNull(goal);
        return decision == GoalDecision.Approved ? GoalCompletionStatus.Complete : goal.CompletionStatus;
    }
}

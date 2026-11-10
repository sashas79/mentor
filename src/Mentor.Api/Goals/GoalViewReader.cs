using System.Linq.Expressions;
using Mentor.Core.Goals;
using Mentor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Mentor.Api.Goals;

/// <summary>
/// Reads goals as their readers see them (<see cref="GoalView"/>): each goal in its agreed order with its progress
/// entries, oldest first, and the mentor's decisions on them. It opens no transaction of its own. A caller reads inside
/// one read-only snapshot, so that each goal's status, entries and decisions describe the same moment as whatever else
/// the caller reads in it.
/// </summary>
internal static class GoalViewReader
{
    /// <summary>The goals <paramref name="which"/> selects, ordered by position and then id.</summary>
    public static async Task<IReadOnlyList<GoalView>> ReadAsync(MentorDbContext db, Expression<Func<DevelopmentGoal, bool>> which, CancellationToken cancellationToken)
    {
        var selected = db.DevelopmentGoals.Where(which);
        var goals = await selected.AsNoTracking()
            .OrderBy(goal => goal.Position).ThenBy(goal => goal.Id)
            .ToListAsync(cancellationToken);
        var entriesOfGoals = db.GoalProgress.Where(entry => selected.Any(goal => goal.Id == entry.GoalId));
        var entries = await entriesOfGoals.AsNoTracking()
            .OrderBy(entry => entry.SubmittedAt).ThenBy(entry => entry.Id)
            .ToListAsync(cancellationToken);
        var decisions = await DecisionsOfAsync(db, entriesOfGoals, cancellationToken);

        var decisionOf = decisions.ToDictionary(decision => decision.ProgressId);
        var entriesOf = entries.ToLookup(entry => entry.GoalId);
        return [.. goals.Select(goal => ViewOf(goal, [.. entriesOf[goal.Id]], decisionOf))];
    }

    /// <summary>The mentor's decisions on <paramref name="entries"/>; at most one per entry.</summary>
    public static Task<List<GoalApproval>> DecisionsOfAsync(MentorDbContext db, IQueryable<GoalProgress> entries, CancellationToken cancellationToken) =>
        db.GoalApprovals.AsNoTracking()
            .Where(decision => entries.Any(entry => entry.Id == decision.ProgressId))
            .ToListAsync(cancellationToken);

    private static GoalView ViewOf(DevelopmentGoal goal, IReadOnlyList<GoalProgress> entries, IReadOnlyDictionary<Guid, GoalApproval> decisions) => new(
        goal.Id,
        goal.RelationshipId,
        goal.FormedAgreementId,
        goal.Position,
        goal.AgreedDescription,
        goal.CompletionStatus,
        GoalCompletion.PendingRequestOf(goal, entries, decisions.Values)?.Id,
        [.. entries.Select(entry => ProgressOf(entry, decisions.GetValueOrDefault(entry.Id)))],
        goal.CreatedAt,
        goal.UpdatedAt);

    private static GoalProgressView ProgressOf(GoalProgress entry, GoalApproval? decision) => new(
        entry.Id,
        entry.ProtegeEmployeeId,
        entry.ProgressUpdate,
        entry.CompletionRequested,
        entry.SubmittedAt,
        GoalCompletion.StandingOf(entry, decision),
        decision is null ? null : new GoalDecisionView(decision.Id, decision.Decision, decision.MentorEmployeeId, decision.DecidedAt));
}

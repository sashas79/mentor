using System.Data;
using System.Linq.Expressions;
using System.Security.Claims;
using Mentor.Api.Audit;
using Mentor.Api.Authorisation;
using Mentor.Api.Identity;
using Mentor.Api.Notifications;
using Mentor.Core.Audit;
using Mentor.Core.Goals;
using Mentor.Core.Notifications;
using Mentor.Core.Relationships;
using Mentor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Mentor.Api.Goals;

/// <summary>
/// Development goals, progress and completion decisions (COMP-GOALS-PROGRESS, BR-005). The goals are those formation
/// instantiated from the formed agreement, worded as agreed. Nothing here changes their wording, the agreement or the
/// relationship. Only the completion status changes, by the mentor's approval.
/// <list type="bullet">
/// <item>The protégé submits dated progress, which needs no approval.</item>
/// <item>The protégé asks for completion with a progress entry flagged as a request, while the goal is incomplete and no
/// other request of it is pending.</item>
/// <item>The relationship's mentor alone decides a request, once. An approval completes the goal in the same transaction.
/// A rejection leaves the goal incomplete, with no reason, resubmission or escalation. Talent Management has no step and
/// reads goals and progress only (ADR-003).</item>
/// </list>
/// Entries and decisions are insert-only evidence. A request's standing is derived from its decision, never stored. Every
/// command writes its audit entries (ADR-007) and the notifications of a request or a decision (ADR-009) in its own
/// transaction. That transaction first takes the goal's row lock, so commands on one goal run one after another, and each
/// check reads a state that no concurrent command can change.
/// </summary>
public sealed class GoalsAndProgress(MentorDbContext db, CallerResolver callers, IAuthorizationService authorization, AuditTrail audit, NotificationEvents notifications)
{
    /// <summary>The database refuses any write inside a view's transaction; a view only ever reads.</summary>
    private const string ReadOnlyTransaction = "SET TRANSACTION READ ONLY";

    /// <summary>The goals of the relationship in their agreed order, each with its progress (FR-034, FR-040, FR-066).</summary>
    public async Task<GoalResult<IReadOnlyList<GoalView>>> ListAsync(ClaimsPrincipal user, Guid relationshipId, CancellationToken cancellationToken)
    {
        var relationship = await db.MentorshipRelationships.AsNoTracking().SingleOrDefaultAsync(row => row.Id == relationshipId, cancellationToken);
        if (relationship is null)
        {
            return GoalResult<IReadOnlyList<GoalView>>.Refused(GoalRefusal.NotFound);
        }

        return await authorization.IsPermittedAsync(user, AuthorisationMatrix.Goals.Read, ProtectedRecord.Of(relationship))
            ? GoalResult<IReadOnlyList<GoalView>>.Of(await ViewsAsync(goal => goal.RelationshipId == relationshipId, cancellationToken))
            : GoalResult<IReadOnlyList<GoalView>>.Refused(GoalRefusal.Forbidden);
    }

    public async Task<GoalResult<GoalView>> ReadAsync(ClaimsPrincipal user, Guid goalId, CancellationToken cancellationToken)
    {
        if (await RelationshipOfAsync(goalId, cancellationToken) is not { } relationship)
        {
            return GoalResult<GoalView>.Refused(GoalRefusal.NotFound);
        }

        return await authorization.IsPermittedAsync(user, AuthorisationMatrix.Goals.Read, ProtectedRecord.Of(relationship))
            ? GoalResult<GoalView>.Of(await ViewAsync(goalId, cancellationToken))
            : GoalResult<GoalView>.Refused(GoalRefusal.Forbidden);
    }

    /// <summary>The protégé's ordinary progress against the goal (FR-035), which needs no approval and completes nothing.</summary>
    public Task<GoalResult<GoalView>> SubmitProgressAsync(ClaimsPrincipal user, Guid goalId, ProgressSubmission submission, CancellationToken cancellationToken) =>
        SubmitAsync(user, goalId, submission, completionRequested: false, cancellationToken);

    /// <summary>
    /// The protégé's request for the goal's formal completion (FR-036): a progress entry that stays pending, with the goal
    /// incomplete, until the mentor decides it. The mentor is told to decide.
    /// </summary>
    public Task<GoalResult<GoalView>> RequestCompletionAsync(ClaimsPrincipal user, Guid goalId, ProgressSubmission submission, CancellationToken cancellationToken) =>
        SubmitAsync(user, goalId, submission, completionRequested: true, cancellationToken);

    /// <summary>
    /// The mentor's decision on a pending completion request of the goal (FR-037, FR-039). An approval completes the goal.
    /// A rejection leaves it incomplete. The protégé is told either way.
    /// </summary>
    public async Task<GoalResult<GoalView>> DecideAsync(ClaimsPrincipal user, Guid goalId, Guid requestId, GoalDecision decision, CancellationToken cancellationToken)
    {
        if (await RelationshipOfAsync(goalId, cancellationToken) is not { } relationship)
        {
            return GoalResult<GoalView>.Refused(GoalRefusal.NotFound);
        }

        if (!await authorization.IsPermittedAsync(user, AuthorisationMatrix.Goals.DecideCompletion, ProtectedRecord.Of(relationship))
            || await callers.ResolveAsync(user, cancellationToken) is not { EmployeeId: { } mentor } actor)
        {
            return GoalResult<GoalView>.Refused(GoalRefusal.Forbidden);
        }

        var refusal = await CommitAsync(goalId, async locked =>
        {
            var request = await db.GoalProgress.AsNoTracking()
                .SingleOrDefaultAsync(entry => entry.Id == requestId && entry.GoalId == goalId, cancellationToken);
            if (request is null)
            {
                return GoalRefusal.NotFound;
            }

            var recorded = await db.GoalApprovals.AsNoTracking().SingleOrDefaultAsync(approval => approval.ProgressId == requestId, cancellationToken);
            switch (GoalCompletion.CheckDecision(request, recorded))
            {
                case CompletionDecisionBlock.NotACompletionRequest:
                    return GoalRefusal.NotACompletionRequest;
                case CompletionDecisionBlock.AlreadyDecided:
                    return GoalRefusal.CompletionDecided;
            }

            var approval = new GoalApproval
            {
                ProgressId = request.Id,
                RelationshipId = relationship.Id,
                MentorEmployeeId = mentor,
                Decision = decision,
            };
            db.GoalApprovals.Add(approval);
            // The database completes a goal only once an approval of one of its requests exists, so the decision is written first.
            await db.SaveChangesAsync(cancellationToken);
            var outcome = decision == GoalDecision.Approved ? AuditOutcome.Approved : AuditOutcome.Rejected;
            audit.Append(actor, new AuditEvent(AuditAction.GoalCompletionDecided, AuditRecordType.GoalApproval, approval.Id)
            {
                NewValues = new AuditValues().Set("progressId", request.Id).Set("decision", decision),
                Outcome = outcome,
            });

            var status = GoalCompletion.StatusAfter(locked, decision);
            if (status != locked.CompletionStatus)
            {
                await db.DevelopmentGoals.Where(row => row.Id == goalId)
                    .ExecuteUpdateAsync(set => set.SetProperty(row => row.CompletionStatus, status), cancellationToken);
                audit.Append(actor, new AuditEvent(AuditAction.GoalCompletionDecided, AuditRecordType.Goal, goalId)
                {
                    PriorValues = new AuditValues().Set("completionStatus", locked.CompletionStatus),
                    NewValues = new AuditValues().Set("completionStatus", status),
                    Outcome = outcome,
                });
            }

            notifications.Raise(WorkflowNotifications.GoalCompletionDecided(locked, relationship, decision));
            return GoalRefusal.None;
        }, cancellationToken);

        return refusal == GoalRefusal.None
            ? GoalResult<GoalView>.Of(await ViewAsync(goalId, cancellationToken))
            : GoalResult<GoalView>.Refused(refusal);
    }

    private async Task<GoalResult<GoalView>> SubmitAsync(
        ClaimsPrincipal user, Guid goalId, ProgressSubmission submission, bool completionRequested, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submission);
        if (await RelationshipOfAsync(goalId, cancellationToken) is not { } relationship)
        {
            return GoalResult<GoalView>.Refused(GoalRefusal.NotFound);
        }

        var permission = completionRequested ? AuthorisationMatrix.Goals.RequestCompletion : AuthorisationMatrix.Goals.SubmitProgress;
        if (!await authorization.IsPermittedAsync(user, permission, ProtectedRecord.Of(relationship))
            || await callers.ResolveAsync(user, cancellationToken) is not { EmployeeId: { } protege } actor)
        {
            return GoalResult<GoalView>.Refused(GoalRefusal.Forbidden);
        }

        var refusal = await CommitAsync(goalId, async locked =>
        {
            if (completionRequested)
            {
                var entries = await db.GoalProgress.AsNoTracking().Where(entry => entry.GoalId == goalId).ToListAsync(cancellationToken);
                var decisions = await DecisionsOfAsync(db.GoalProgress.Where(entry => entry.GoalId == goalId), cancellationToken);
                switch (GoalCompletion.CheckRequest(locked, entries, decisions))
                {
                    case CompletionRequestBlock.GoalComplete:
                        return GoalRefusal.GoalComplete;
                    case CompletionRequestBlock.RequestPending:
                        return GoalRefusal.CompletionPending;
                }
            }

            var entry = new GoalProgress
            {
                GoalId = goalId,
                RelationshipId = relationship.Id,
                ProtegeEmployeeId = protege,
                ProgressUpdate = submission.ProgressUpdate,
                CompletionRequested = completionRequested,
            };
            db.GoalProgress.Add(entry);
            // The structured fields of the entry only: the trail holds no narrative text (ADR-007).
            audit.Append(actor, new AuditEvent(AuditAction.GoalProgressSubmitted, AuditRecordType.GoalProgress, entry.Id)
            {
                NewValues = new AuditValues().Set("goalId", goalId).Set("completionRequested", completionRequested),
            });
            if (completionRequested)
            {
                notifications.Raise(WorkflowNotifications.GoalCompletionRequested(locked, relationship));
            }

            return GoalRefusal.None;
        }, cancellationToken);

        return refusal == GoalRefusal.None
            ? GoalResult<GoalView>.Of(await ViewAsync(goalId, cancellationToken))
            : GoalResult<GoalView>.Refused(refusal);
    }

    /// <summary>
    /// One goal command in one transaction at READ COMMITTED. Its first statement takes the goal's row lock, which is held
    /// until commit, and the command then runs on the goal as that lock leaves it. A refusal leaves the transaction
    /// uncommitted, so nothing it did persists.
    /// </summary>
    private async Task<GoalRefusal> CommitAsync(Guid goalId, Func<DevelopmentGoal, Task<GoalRefusal>> command, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var locked = (await db.DevelopmentGoals
                .FromSql($"SELECT * FROM mentor.development_goal WHERE id = {goalId} FOR NO KEY UPDATE")
                .AsNoTracking()
                .ToListAsync(cancellationToken))
            .Single();
        var refusal = await command(locked);
        if (refusal != GoalRefusal.None)
        {
            return refusal;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return GoalRefusal.None;
    }

    /// <summary>The relationship the goal belongs to, which every goal action is checked against (ADR-003); none for an unknown goal.</summary>
    private Task<MentorshipRelationship?> RelationshipOfAsync(Guid goalId, CancellationToken cancellationToken) =>
        db.MentorshipRelationships.AsNoTracking()
            .SingleOrDefaultAsync(relationship => db.DevelopmentGoals.Any(goal => goal.Id == goalId && goal.RelationshipId == relationship.Id), cancellationToken);

    private async Task<GoalView> ViewAsync(Guid goalId, CancellationToken cancellationToken) =>
        (await ViewsAsync(goal => goal.Id == goalId, cancellationToken)).Single();

    /// <summary>
    /// The goals <paramref name="which"/> selects as one read-only snapshot (REPEATABLE READ), so that each goal's status,
    /// entries and decisions describe the same moment.
    /// </summary>
    private async Task<IReadOnlyList<GoalView>> ViewsAsync(Expression<Func<DevelopmentGoal, bool>> which, CancellationToken cancellationToken)
    {
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await db.Database.ExecuteSqlRawAsync(ReadOnlyTransaction, cancellationToken);
        var selected = db.DevelopmentGoals.Where(which);
        var goals = await selected.AsNoTracking()
            .OrderBy(goal => goal.Position).ThenBy(goal => goal.Id)
            .ToListAsync(cancellationToken);
        var entriesOfGoals = db.GoalProgress.Where(entry => selected.Any(goal => goal.Id == entry.GoalId));
        var entries = await entriesOfGoals.AsNoTracking()
            .OrderBy(entry => entry.SubmittedAt).ThenBy(entry => entry.Id)
            .ToListAsync(cancellationToken);
        var decisions = await DecisionsOfAsync(entriesOfGoals, cancellationToken);
        await snapshot.CommitAsync(cancellationToken);

        var decisionOf = decisions.ToDictionary(decision => decision.ProgressId);
        return [.. goals.Select(goal => ViewOf(goal, [.. entries.Where(entry => entry.GoalId == goal.Id)], decisionOf))];
    }

    private Task<List<GoalApproval>> DecisionsOfAsync(IQueryable<GoalProgress> entries, CancellationToken cancellationToken) =>
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

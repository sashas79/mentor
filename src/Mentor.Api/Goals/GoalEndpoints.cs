using System.Security.Claims;
using Mentor.Api.Authorisation;
using Mentor.Core.Goals;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Mentor.Api.Goals;

/// <summary>
/// The endpoints of development goals, progress and completion decisions (COMP-GOALS-PROGRESS, CONTRACT-MENTORSHIP-API).
/// Each requires a permission of the authorisation matrix, checked against the goal's relationship (ADR-003):
/// <list type="bullet">
/// <item>the parties and Talent Management read goals and progress;</item>
/// <item>the protégé submits progress and asks for completion;</item>
/// <item>the relationship's mentor alone decides a completion request.</item>
/// </list>
/// Progress entries and decisions are evidence of their goal, as acceptances are of a draft, so each command answers
/// with the goal as it leaves it. No route changes a goal's agreed wording or removes a goal, an entry or a decision.
/// </summary>
public static class GoalEndpoints
{
    public const string RelationshipGoalsRoute = "/api/relationships/{relationshipId:guid}/goals";
    public const string GoalRoute = "/api/goals/{goalId:guid}";
    public const string ProgressRoute = "/api/goals/{goalId:guid}/progress";
    public const string CompletionRequestsRoute = "/api/goals/{goalId:guid}/completion-requests";
    public const string DecisionRoute = "/api/goals/{goalId:guid}/completion-requests/{requestId:guid}/decision";

    /// <summary>The problem (409) of a completion request for a goal that is already complete.</summary>
    public const string GoalComplete = "Goal complete";

    /// <summary>The problem (409) of a completion request while another request of the goal awaits the mentor's decision.</summary>
    public const string CompletionPending = "Completion pending";

    /// <summary>The problem (409) of a decision on an entry that does not ask for completion.</summary>
    public const string NotACompletionRequest = "Not a completion request";

    /// <summary>The problem (409) of a decision on a request that has already been decided.</summary>
    public const string CompletionDecided = "Completion decided";

    private const string Tag = "Goals";

    public static IServiceCollection AddGoalsAndProgress(this IServiceCollection services)
    {
        services.AddScoped<GoalsAndProgress>();
        return services;
    }

    public static IEndpointRouteBuilder MapGoalsAndProgress(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(RelationshipGoalsRoute, ListAsync)
            .WithName("ListRelationshipGoals")
            .WithTags(Tag)
            .RequirePermission(AuthorisationMatrix.Goals.Read);
        endpoints.MapGet(GoalRoute, GetAsync)
            .WithName("GetGoal")
            .WithTags(Tag)
            .RequirePermission(AuthorisationMatrix.Goals.Read);
        endpoints.MapPost(ProgressRoute, SubmitProgressAsync)
            .WithName("SubmitGoalProgress")
            .WithTags(Tag)
            .RequirePermission(AuthorisationMatrix.Goals.SubmitProgress);
        endpoints.MapPost(CompletionRequestsRoute, RequestCompletionAsync)
            .WithName("RequestGoalCompletion")
            .WithTags(Tag)
            .RequirePermission(AuthorisationMatrix.Goals.RequestCompletion);
        endpoints.MapPost(DecisionRoute, DecideAsync)
            .WithName("DecideGoalCompletion")
            .WithTags(Tag)
            .RequirePermission(AuthorisationMatrix.Goals.DecideCompletion);
        return endpoints;
    }

    private static async Task<Results<Ok<IReadOnlyList<GoalView>>, NotFound, ForbidHttpResult>> ListAsync(
        Guid relationshipId, ClaimsPrincipal user, GoalsAndProgress goals, CancellationToken cancellationToken)
    {
        var result = await goals.ListAsync(user, relationshipId, cancellationToken);
        if (result.Value is { } listed)
        {
            return TypedResults.Ok(listed);
        }

        return result.Refusal == GoalRefusal.NotFound ? TypedResults.NotFound() : TypedResults.Forbid();
    }

    private static async Task<Results<Ok<GoalView>, NotFound, ForbidHttpResult>> GetAsync(
        Guid goalId, ClaimsPrincipal user, GoalsAndProgress goals, CancellationToken cancellationToken)
    {
        var result = await goals.ReadAsync(user, goalId, cancellationToken);
        if (result.Value is { } goal)
        {
            return TypedResults.Ok(goal);
        }

        return result.Refusal == GoalRefusal.NotFound ? TypedResults.NotFound() : TypedResults.Forbid();
    }

    private static async Task<Results<Ok<GoalView>, ValidationProblem, NotFound, ForbidHttpResult, ProblemHttpResult>> SubmitProgressAsync(
        Guid goalId, GoalProgressRequest progress, ClaimsPrincipal user, GoalsAndProgress goals, CancellationToken cancellationToken)
    {
        if (!ProgressSubmission.TryCreate(progress.ProgressUpdate, out var submission, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        return Answer(await goals.SubmitProgressAsync(user, goalId, submission, cancellationToken));
    }

    private static async Task<Results<Ok<GoalView>, ValidationProblem, NotFound, ForbidHttpResult, ProblemHttpResult>> RequestCompletionAsync(
        Guid goalId, GoalProgressRequest progress, ClaimsPrincipal user, GoalsAndProgress goals, CancellationToken cancellationToken)
    {
        if (!ProgressSubmission.TryCreate(progress.ProgressUpdate, out var submission, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        return Answer(await goals.RequestCompletionAsync(user, goalId, submission, cancellationToken));
    }

    private static async Task<Results<Ok<GoalView>, ValidationProblem, NotFound, ForbidHttpResult, ProblemHttpResult>> DecideAsync(
        Guid goalId, Guid requestId, GoalDecisionRequest decision, ClaimsPrincipal user, GoalsAndProgress goals, CancellationToken cancellationToken)
    {
        if (decision.Decision is not { } decided || !Enum.IsDefined(decided))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["decision"] = ["The decision, Approved or Rejected, is required."],
            });
        }

        return Answer(await goals.DecideAsync(user, goalId, requestId, decided, cancellationToken));
    }

    private static Results<Ok<GoalView>, ValidationProblem, NotFound, ForbidHttpResult, ProblemHttpResult> Answer(GoalResult<GoalView> result)
    {
        if (result.Value is { } goal)
        {
            return TypedResults.Ok(goal);
        }

        return result.Refusal switch
        {
            GoalRefusal.NotFound => TypedResults.NotFound(),
            GoalRefusal.Forbidden => TypedResults.Forbid(),
            GoalRefusal.GoalComplete => Problem(GoalComplete,
                "The goal is complete; completion is requested only while a goal is incomplete."),
            GoalRefusal.CompletionPending => Problem(CompletionPending,
                "A completion request of this goal awaits the mentor's decision; another is made only once it is decided."),
            GoalRefusal.NotACompletionRequest => Problem(NotACompletionRequest,
                "The progress entry named does not ask for completion; ordinary progress needs no decision."),
            GoalRefusal.CompletionDecided => Problem(CompletionDecided,
                "The completion request has been decided; a decision is recorded once and never changed."),
            _ => throw new InvalidOperationException($"Refusal {result.Refusal} is not answered."),
        };
    }

    private static ProblemHttpResult Problem(string title, string detail) =>
        TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: title, detail: detail);
}

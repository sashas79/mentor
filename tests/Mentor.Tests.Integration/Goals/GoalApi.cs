using System.Text.Json;
using System.Text.Json.Nodes;
using Mentor.Api.Goals;
using Mentor.Tests.Integration.Persistence;
using Mentor.Tests.Integration.Relationships;
using Mentor.Tests.Integration.Support;

namespace Mentor.Tests.Integration.Goals;

/// <summary>
/// The routes, bodies and steps the goal tests take through the API, and the rows they read back. Relationships are formed
/// through the API (<see cref="RelationshipApi"/>) from the draft form's two goals: the Employee caller is the protégé, the
/// Mentor caller the mentor and the Administrator caller Talent Management.
/// </summary>
internal static class GoalApi
{
    public const string FirstUpdate = "Drafted the review checklist with the team";
    public const string RequestUpdate = "Led the design review of the billing service";

    public static string OfRelationship(Guid relationship) =>
        GoalEndpoints.RelationshipGoalsRoute.Replace("{relationshipId:guid}", relationship.ToString(), StringComparison.Ordinal);

    public static string Goal(Guid goal) => Route(GoalEndpoints.GoalRoute, goal);

    public static string Progress(Guid goal) => Route(GoalEndpoints.ProgressRoute, goal);

    public static string CompletionRequests(Guid goal) => Route(GoalEndpoints.CompletionRequestsRoute, goal);

    public static string Decision(Guid goal, Guid request) =>
        Route(GoalEndpoints.DecisionRoute, goal).Replace("{requestId:guid}", request.ToString(), StringComparison.Ordinal);

    /// <summary>A relationship formed through the API from a new request, and its goals as the protégé reads them, in their agreed order.</summary>
    public static async Task<(Guid Relationship, List<JsonElement> Goals)> FormedAsync(WorkflowApi api, JsonObject? form = null)
    {
        var relationship = (await RelationshipApi.FormedAsync(api, form)).Id();
        return (relationship, await ListAsync(api, relationship, Profile.Employee));
    }

    public static async Task<List<JsonElement>> ListAsync(WorkflowApi api, Guid relationship, Profile caller) =>
        [.. (await api.JsonAsync(HttpMethod.Get, OfRelationship(relationship), caller)).EnumerateArray()];

    public static Task<JsonElement> ReadAsync(WorkflowApi api, Guid goal, Profile caller) =>
        api.JsonAsync(HttpMethod.Get, Goal(goal), caller);

    /// <summary>The protégé's ordinary progress, which must succeed; it returns the goal as the entry leaves it.</summary>
    public static Task<JsonElement> SubmitAsync(WorkflowApi api, Guid goal, string update = FirstUpdate) =>
        api.JsonAsync(HttpMethod.Post, Progress(goal), Profile.Employee, new { progressUpdate = update });

    /// <summary>The protégé's completion request, which must succeed; it returns the goal as the request leaves it.</summary>
    public static Task<JsonElement> RequestAsync(WorkflowApi api, Guid goal, string update = RequestUpdate) =>
        api.JsonAsync(HttpMethod.Post, CompletionRequests(goal), Profile.Employee, new { progressUpdate = update });

    /// <summary>The mentor's decision on the request, which must succeed; it returns the goal as the decision leaves it.</summary>
    public static Task<JsonElement> DecideAsync(WorkflowApi api, Guid goal, Guid request, string decision) =>
        api.JsonAsync(HttpMethod.Post, Decision(goal, request), Profile.Mentor, new { decision });

    public static Task<HttpResponseMessage> TrySubmitAsync(WorkflowApi api, Guid goal, Profile? caller, object? body = null) =>
        api.SendAsync(HttpMethod.Post, Progress(goal), caller, body ?? new { progressUpdate = FirstUpdate });

    public static Task<HttpResponseMessage> TryRequestAsync(WorkflowApi api, Guid goal, Profile? caller, object? body = null) =>
        api.SendAsync(HttpMethod.Post, CompletionRequests(goal), caller, body ?? new { progressUpdate = RequestUpdate });

    public static Task<HttpResponseMessage> TryDecideAsync(WorkflowApi api, Guid goal, Guid request, Profile? caller, object body) =>
        api.SendAsync(HttpMethod.Post, Decision(goal, request), caller, body);

    /// <summary>The request of the goal that awaits the mentor's decision, if any.</summary>
    public static Guid? Pending(JsonElement goal) => goal.OptionalGuid("pendingCompletionRequestId");

    /// <summary>The goal's newest progress entry.</summary>
    public static JsonElement Latest(JsonElement goal) => goal.Items("progress")[^1];

    /// <summary>The goal's progress entries, oldest first, as update, whether each asks for completion, and its standing.</summary>
    public static List<(string Update, bool CompletionRequested, string? Completion)> EntriesOf(JsonElement goal) =>
        [.. goal.Items("progress").Select(entry => (entry.Text("progressUpdate"), entry.GetProperty("completionRequested").GetBoolean(),
            entry.GetProperty("completion").ValueKind == JsonValueKind.Null ? null : entry.Text("completion")))];

    /// <summary>The trail of a record as Talent Management reads it, oldest first.</summary>
    public static async Task<List<JsonElement>> TrailAsync(WorkflowApi api, string recordType, Guid record) =>
        [.. (await api.JsonAsync(HttpMethod.Get, $"/api/audit-trail/records/{recordType}/{record}", Profile.Administrator)).EnumerateArray()];

    public static Guid ActorOf(JsonElement entry) => entry.GetProperty("actor").GetProperty("employeeId").GetGuid();

    public static Task<long> ProgressCountAsync(TestDatabase db) =>
        db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.goal_progress");

    public static Task<long> DecisionCountAsync(TestDatabase db) =>
        db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.goal_approval");

    public static Task<string> StatusAsync(TestDatabase db, Guid goal) =>
        db.ScalarAsync<string>(Login.Owner, "SELECT completion_status FROM mentor.development_goal WHERE id = $1", goal);

    private static string Route(string template, Guid goal) => template.Replace("{goalId:guid}", goal.ToString(), StringComparison.Ordinal);
}

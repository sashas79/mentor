using System.Text.Json;
using System.Text.Json.Nodes;
using Mentor.Api.Relationships;
using Mentor.Tests.Integration.Formation;
using Mentor.Tests.Integration.Persistence;
using Mentor.Tests.Integration.Support;

namespace Mentor.Tests.Integration.Relationships;

/// <summary>
/// The routes, bodies and steps the relationship administration tests take through the API. Relationships are formed
/// through the API from the Employee caller's requests paired with the Mentor caller (<see cref="FormationApi"/>); Talent
/// Management is the Administrator caller.
/// </summary>
internal static class RelationshipApi
{
    /// <summary>The caller's own relationships.</summary>
    public const string Own = RelationshipEndpoints.RelationshipsRoute;

    public static string Route(Guid relationship) =>
        RelationshipEndpoints.RelationshipRoute.Replace("{relationshipId:guid}", relationship.ToString(), StringComparison.Ordinal);

    /// <summary>The If-Match of a relationship as last read: its version as a strong entity tag.</summary>
    public static string Tag(JsonElement relationship) => $"\"{relationship.GetProperty("version").GetInt64()}\"";

    /// <summary>A relationship formed through the API from a new request, as Talent Management reads it.</summary>
    public static async Task<JsonElement> FormedAsync(WorkflowApi api, JsonObject? form = null)
    {
        var formed = await FormationApi.DecideAsync(api, await FormationApi.BothAcceptedAsync(api, form));
        return await ReadAsync(api, FormationApi.Relationship(formed).Id(), Profile.Administrator);
    }

    public static Task<JsonElement> ReadAsync(WorkflowApi api, Guid relationship, Profile caller) =>
        api.JsonAsync(HttpMethod.Get, Route(relationship), caller);

    /// <summary>Talent Management's edit of the relationship as last read, which must succeed; it returns the relationship as the edit leaves it.</summary>
    public static Task<JsonElement> EditAsync(WorkflowApi api, JsonElement relationship, object attributes) =>
        api.JsonAsync(HttpMethod.Patch, Route(relationship.Id()), Profile.Administrator, attributes, ifMatch: Tag(relationship));

    /// <summary>An edit by <paramref name="caller"/> of the relationship as last read, or on <paramref name="ifMatch"/>, whatever it answers.</summary>
    public static Task<HttpResponseMessage> TryEditAsync(WorkflowApi api, JsonElement relationship, Profile? caller, object attributes, string? ifMatch = null) =>
        api.SendAsync(HttpMethod.Patch, Route(relationship.Id()), caller, attributes, ifMatch ?? Tag(relationship));

    public static (string? State, string? Status, string? IsActive) AttributesOf(JsonElement relationship) =>
        (relationship.GetProperty("state").GetString(), relationship.GetProperty("status").GetString(), relationship.GetProperty("isActive").GetString());

    /// <summary>The ids of the caller's own relationships, oldest first.</summary>
    public static async Task<List<Guid>> OwnAsync(WorkflowApi api, Profile caller) =>
        [.. (await api.JsonAsync(HttpMethod.Get, Own, caller)).EnumerateArray().Select(JsonElementExtensions.Id)];

    /// <summary>The trail of the relationship as Talent Management reads it, oldest first.</summary>
    public static async Task<List<JsonElement>> HistoryAsync(WorkflowApi api, Guid relationship) =>
        [.. (await api.JsonAsync(HttpMethod.Get, $"/api/audit-trail/records/Relationship/{relationship}", Profile.Administrator)).EnumerateArray()];

    /// <summary>An attribute change of the trail as field, prior value and new value, as the database stores them.</summary>
    public static (string Field, string? Prior, string? New) ChangeOf(JsonElement entry)
    {
        var field = Assert.Single(entry.GetProperty("newValues").EnumerateObject()).Name;
        return (field, entry.GetProperty("priorValues").GetProperty(field).GetString(), entry.GetProperty("newValues").GetProperty(field).GetString());
    }

    public static Task<long> RelationshipCountAsync(TestDatabase db) =>
        db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.mentorship_relationship");

    public static Task<long> NotificationCountAsync(TestDatabase db) =>
        db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.notification_event");
}

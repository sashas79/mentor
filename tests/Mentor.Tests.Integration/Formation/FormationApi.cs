using System.Text.Json;
using System.Text.Json.Nodes;
using Mentor.Api.Formation;
using Mentor.Tests.Integration.Agreements;
using Mentor.Tests.Integration.Persistence;
using Mentor.Tests.Integration.Requests;
using Mentor.Tests.Integration.Support;

namespace Mentor.Tests.Integration.Formation;

/// <summary>
/// The routes, bodies and steps the formation tests take through the API, and the rows they read back. Drafts are the
/// Employee caller's requests paired with the Mentor caller (<see cref="DraftApi"/>); Talent Management is the
/// Administrator caller.
/// </summary>
internal static class FormationApi
{
    /// <summary>The route of the candidate's formation outcome.</summary>
    public static string Outcome(Guid candidate) =>
        FormationEndpoints.FormationRoute.Replace("{candidateId:guid}", candidate.ToString(), StringComparison.Ordinal);

    public static string Decisions(Guid candidate, Guid revision) => FormationEndpoints.DecisionsRoute
        .Replace("{candidateId:guid}", candidate.ToString(), StringComparison.Ordinal)
        .Replace("{revisionId:guid}", revision.ToString(), StringComparison.Ordinal);

    /// <summary>A new drafting candidate whose current revision both participants accepted: the draft at Step 3, as the mentor's acceptance leaves it.</summary>
    public static async Task<JsonElement> BothAcceptedAsync(WorkflowApi api, JsonObject? form = null)
    {
        var revised = await DraftApi.ReviseAsync(api, await DraftApi.DraftingAsync(api), Profile.Employee, form);
        return await BothAcceptAsync(api, revised, Profile.Mentor);
    }

    /// <summary>The Employee caller's and then <paramref name="mentor"/>'s acceptance of the current revision of the draft as last read.</summary>
    public static async Task<JsonElement> BothAcceptAsync(WorkflowApi api, JsonElement draft, Profile mentor)
    {
        var bothAccepted = await DraftApi.AcceptAsync(api, await DraftApi.AcceptAsync(api, draft, Profile.Employee), mentor);
        Assert.Equal(3, DraftApi.Step(bothAccepted));
        return bothAccepted;
    }

    /// <summary>
    /// <paramref name="mentor"/>, an approved mentor, paired with <paramref name="request"/> of the Employee caller and
    /// brought through the API to DRAFTING: the draft as the protégé reads it.
    /// </summary>
    public static async Task<JsonElement> DraftingWithAsync(WorkflowApi api, Guid request, Profile mentor)
    {
        var candidate = await PairingApi.SelectAsync(api, request, Profile.Employee, api.Employee(mentor));
        candidate = await PairingApi.DecideAsync(api, PairingApi.Accept(candidate.Id()), mentor, candidate);
        candidate = await PairingApi.DecideAsync(api, PairingApi.Willingness(candidate.Id()), Profile.Employee, candidate);
        candidate = await PairingApi.DecideAsync(api, PairingApi.Willingness(candidate.Id()), mentor, candidate);
        return await DraftApi.ReadAsync(api, candidate.Id(), Profile.Employee);
    }

    /// <summary>Talent Management's decision on the current revision of the draft as last read, naming its hash; it must succeed.</summary>
    public static Task<JsonElement> DecideAsync(WorkflowApi api, JsonElement draft, string outcome = "Approved") =>
        api.JsonAsync(HttpMethod.Post, Decisions(DraftApi.CandidateOf(draft), DraftApi.Current(draft).Id()), Profile.Administrator,
            new { outcome, contentHash = DraftApi.Current(draft).Text("contentHash") }, ifMatch: DraftApi.Tag(draft));

    /// <summary>
    /// A decision by <paramref name="caller"/> on <paramref name="revision"/> naming <paramref name="contentHash"/>, by
    /// default the current revision and its hash, on the draft as last read, whatever it answers.
    /// </summary>
    public static Task<HttpResponseMessage> TryDecideAsync(
        WorkflowApi api, JsonElement draft, Profile? caller, string outcome = "Approved", Guid? revision = null, string? contentHash = null) =>
        api.SendAsync(HttpMethod.Post, Decisions(DraftApi.CandidateOf(draft), revision ?? DraftApi.Current(draft).Id()), caller,
            new { outcome, contentHash = contentHash ?? DraftApi.Current(draft).Text("contentHash") }, DraftApi.Tag(draft));

    public static Task<JsonElement> ReadAsync(WorkflowApi api, Guid candidate, Profile caller) =>
        api.JsonAsync(HttpMethod.Get, Outcome(candidate), caller);

    public static JsonElement Relationship(JsonElement formation) => formation.GetProperty("relationship");

    /// <summary>The decisions of the formation view as outcome, number of the revision decided on and applicability, oldest first.</summary>
    public static List<(string Outcome, int RevisionNumber, string Applicability)> DecisionsOf(JsonElement formation) =>
        [.. formation.Items("decisions").Select(decision => (decision.Text("outcome"), decision.GetProperty("revisionNumber").GetInt32(), decision.Text("applicability")))];

    public static List<string> GoalsOf(JsonElement formation) =>
        [.. Relationship(formation).Items("goals").Select(goal => goal.Text("agreedDescription"))];

    public static Task<long> DecisionCountAsync(TestDatabase db, Guid candidate) =>
        db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.formation_decision WHERE candidate_id = $1", candidate);

    public static Task<long> FormedAgreementCountAsync(TestDatabase db, Guid candidate) =>
        db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.formed_agreement WHERE candidate_id = $1", candidate);

    public static Task<long> GoalCountAsync(TestDatabase db, Guid candidate) =>
        db.ScalarAsync<long>(Login.Owner, """
            SELECT count(*) FROM mentor.development_goal AS goal
            JOIN mentor.mentorship_relationship AS relationship ON relationship.id = goal.relationship_id
            WHERE relationship.candidate_id = $1
            """, candidate);

    public static Task<Guid> RequestOfAsync(TestDatabase db, Guid candidate) =>
        db.ScalarAsync<Guid>(Login.Owner, "SELECT request_id FROM mentor.mentor_selection WHERE id = $1", candidate);

    public static Task<string> DispositionAsync(TestDatabase db, Guid request) =>
        db.ScalarAsync<string>(Login.Owner, "SELECT disposition FROM mentor.mentorship_request WHERE id = $1", request);
}

using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mentor.Api.Agreements;
using Mentor.Tests.Integration.Persistence;
using Mentor.Tests.Integration.Requests;
using Mentor.Tests.Integration.Support;

namespace Mentor.Tests.Integration.Agreements;

/// <summary>
/// The routes, bodies and steps the agreement draft tests take through the API, and the rows they read back. The
/// candidates are the Employee caller's requests paired with the Mentor caller (<see cref="PairingApi"/>).
/// </summary>
internal static class DraftApi
{
    public static string Draft(Guid candidate) => Route(AgreementDraftEndpoints.DraftRoute, candidate);

    public static string Revisions(Guid candidate) => Route(AgreementDraftEndpoints.RevisionsRoute, candidate);

    public static string Revision(Guid candidate, Guid revision) => Route(AgreementDraftEndpoints.RevisionRoute, candidate, revision);

    public static string Acceptances(Guid candidate, Guid revision) => Route(AgreementDraftEndpoints.AcceptancesRoute, candidate, revision);

    public static string Annex(Guid candidate) => Route(AgreementDraftEndpoints.AnnexRoute, candidate);

    /// <summary>A complete revision body with every term supplied; a test changes what it needs to.</summary>
    public static JsonObject Form(string firstGoal = "Lead a design review by June 2027") => new()
    {
        ["terms"] = new JsonObject
        {
            ["durationCategory"] = "TwelveMonths",
            ["startDate"] = "2026-12-01",
            ["endDate"] = "2027-11-30",
            ["meetingIntervalWeeks"] = 2,
            ["approximateMinutesPerMeeting"] = 45,
            ["betweenMeetingContact"] = new JsonObject { ["interval"] = 1, ["unit"] = "Weeks" },
            ["missedMeetingArrangements"] = "Whoever cannot attend tells the other a day ahead and proposes a new date.",
            ["reviewDate"] = "2027-03-01",
        },
        ["goals"] = new JsonArray(firstGoal, "Present at the architecture forum"),
        ["barriers"] = new JsonArray("Delivery deadlines"),
        ["contingencies"] = new JsonArray("Move the meeting rather than skip it"),
        ["responsibilities"] = new JsonObject
        {
            ["mentor"] = new JsonArray("Share experience of leading design reviews"),
            ["protege"] = new JsonArray("Prepare an agenda for each meeting"),
        },
    };

    /// <summary>The If-Match of a draft as last read: the candidate's version as a strong entity tag.</summary>
    public static string Tag(JsonElement draft) => $"\"{draft.GetProperty("version").GetInt64()}\"";

    /// <summary>A new request of the Employee caller paired with the Mentor caller, brought to DRAFTING, and its draft as read by the protégé.</summary>
    public static async Task<JsonElement> DraftingAsync(WorkflowApi api)
    {
        var candidate = await PairingApi.CandidateAsync(api, "Drafting");
        return await ReadAsync(api, candidate.Id(), Profile.Employee);
    }

    public static Task<JsonElement> ReadAsync(WorkflowApi api, Guid candidate, Profile caller) =>
        api.JsonAsync(HttpMethod.Get, Draft(candidate), caller);

    /// <summary>A revision by <paramref name="author"/> on the draft as last read, which must succeed; it returns the draft as the revision leaves it.</summary>
    public static Task<JsonElement> ReviseAsync(WorkflowApi api, JsonElement draft, Profile author, JsonObject? form = null) =>
        api.JsonAsync(HttpMethod.Post, Revisions(CandidateOf(draft)), author, form ?? Form(), HttpStatusCode.Created, Tag(draft));

    public static Task<HttpResponseMessage> TryReviseAsync(WorkflowApi api, JsonElement draft, Profile? author, JsonObject? form = null) =>
        api.SendAsync(HttpMethod.Post, Revisions(CandidateOf(draft)), author, form ?? Form(), Tag(draft));

    /// <summary>The participant's acceptance of the current revision of the draft as last read, naming its hash; it must succeed.</summary>
    public static Task<JsonElement> AcceptAsync(WorkflowApi api, JsonElement draft, Profile participant) =>
        api.JsonAsync(HttpMethod.Post, Acceptances(CandidateOf(draft), Current(draft).Id()), participant,
            new { contentHash = Current(draft).Text("contentHash") }, ifMatch: Tag(draft));

    /// <summary>An acceptance of <paramref name="revision"/> naming <paramref name="contentHash"/>, on the draft as last read, whatever it answers.</summary>
    public static Task<HttpResponseMessage> TryAcceptAsync(WorkflowApi api, JsonElement draft, Profile? participant, Guid revision, string contentHash) =>
        api.SendAsync(HttpMethod.Post, Acceptances(CandidateOf(draft), revision), participant, new { contentHash }, Tag(draft));

    /// <summary>An administrator's annex change on the draft as last read, whatever it answers.</summary>
    public static Task<HttpResponseMessage> TryChangeAnnexAsync(WorkflowApi api, JsonElement draft, Profile? caller, string basis, string? value) =>
        api.SendAsync(HttpMethod.Post, Annex(CandidateOf(draft)), caller, new { basis, value }, Tag(draft));

    public static Guid CandidateOf(JsonElement draft) => draft.GetProperty("candidateId").GetGuid();

    public static JsonElement Current(JsonElement draft) => draft.GetProperty("currentRevision");

    public static int? Step(JsonElement draft) => draft.OptionalInt("step");

    /// <summary>The acceptances of the draft as participant role and applicability, oldest first.</summary>
    public static List<(string Participant, int RevisionNumber, string Applicability)> AcceptancesOf(JsonElement draft) =>
        [.. draft.Items("acceptances").Select(acceptance => (acceptance.Text("participant"), acceptance.GetProperty("revisionNumber").GetInt32(), acceptance.Text("applicability")))];

    public static Task<string> ContentAsync(TestDatabase db, Guid revision) =>
        db.ScalarAsync<string>(Login.Owner, "SELECT content FROM mentor.agreement_revision WHERE id = $1", revision);

    public static Task<long> RevisionCountAsync(TestDatabase db, Guid candidate) =>
        db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.agreement_revision WHERE candidate_id = $1", candidate);

    public static Task<long> AcceptanceCountAsync(TestDatabase db, Guid candidate) =>
        db.ScalarAsync<long>(Login.Owner, """
            SELECT count(*) FROM mentor.agreement_acceptance AS acceptance
            JOIN mentor.agreement_revision AS revision ON revision.id = acceptance.revision_id
            WHERE revision.candidate_id = $1
            """, candidate);

    public static Task<long> PermittedChangeCountAsync(TestDatabase db, Guid candidate) =>
        db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.permitted_change WHERE candidate_id = $1", candidate);

    /// <summary>
    /// The candidate formed on its current revision exactly as formation leaves it (ADR-005), written directly: the
    /// approval, the FORMED head, the relationship and the formed agreement copying the revision. Formation itself is
    /// COMP-FORMATION's; the draft tests need only its outcome. Returns the formed agreement.
    /// </summary>
    public static async Task<Guid> FormAsync(WorkflowApi api, JsonElement draft)
    {
        var candidate = CandidateOf(draft);
        var current = Current(draft);
        var revision = new Revision(current.Id(), await ContentAsync(api.Db, current.Id()), current.Text("contentHash"));
        var seed = new Seed(api.Db);
        await seed.FormationDecisionAsync(candidate, revision, api.Employee(Profile.Administrator));
        await api.Db.ExecuteAsync(Login.Owner, "UPDATE mentor.mentor_selection SET status = 'FORMED', version = version + 1 WHERE id = $1", candidate);
        var relationship = await seed.DigitalRelationshipAsync(candidate, api.Employee(Profile.Employee), api.Employee(Profile.Mentor));
        return await seed.FormedAgreementAsync(relationship, candidate, revision);
    }

    private static string Route(string template, Guid candidate, Guid? revision = null)
    {
        var route = template.Replace("{candidateId:guid}", candidate.ToString(), StringComparison.Ordinal);
        return revision is { } id ? route.Replace("{revisionId:guid}", id.ToString(), StringComparison.Ordinal) : route;
    }
}

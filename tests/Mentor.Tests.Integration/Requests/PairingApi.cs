using System.Net;
using System.Text.Json;
using Mentor.Api.Requests;
using Mentor.Tests.Integration.MentorRegistry;
using Mentor.Tests.Integration.Persistence;
using Mentor.Tests.Integration.Support;

namespace Mentor.Tests.Integration.Requests;

/// <summary>The routes, bodies and steps the request and candidate tests take through the API, and the rows they read back.</summary>
internal static class PairingApi
{
    public const string RequestsRoute = MentorshipRequestEndpoints.RequestsRoute;
    public const string CandidatesRoute = MentorshipRequestEndpoints.CandidatesRoute;
    public const string Inbox = "/api/notifications";

    public static string Request(Guid id) => Route(MentorshipRequestEndpoints.RequestRoute, "requestId", id);

    public static string RequestCandidates(Guid id) => Route(MentorshipRequestEndpoints.RequestCandidatesRoute, "requestId", id);

    public static string Candidate(Guid id) => Route(MentorshipRequestEndpoints.CandidateRoute, "candidateId", id);

    public static string Accept(Guid id) => Route(MentorshipRequestEndpoints.AcceptRoute, "candidateId", id);

    public static string Decline(Guid id) => Route(MentorshipRequestEndpoints.DeclineRoute, "candidateId", id);

    public static string Willingness(Guid id) => Route(MentorshipRequestEndpoints.WillingnessRoute, "candidateId", id);

    public static string Withdraw(Guid id) => Route(MentorshipRequestEndpoints.WithdrawRoute, "candidateId", id);

    public static string Introductions(Guid id) => $"/api/candidates/{id}/meetings";

    public static string Evidence(Guid id) => $"/api/audit-trail/candidates/{id}";

    /// <summary>A complete request body.</summary>
    public static object Statement(
        string? motivation = "To grow into a technical lead",
        string? developmentGoals = "Lead a design review; present at the architecture forum",
        string? nontechnicalExpectations = "Confidence in difficult conversations") =>
        new { motivation, developmentGoals, nontechnicalExpectations };

    /// <summary>A request of <paramref name="protege"/>, submitted through the API.</summary>
    public static async Task<Guid> SubmitAsync(WorkflowApi api, Profile protege) =>
        (await api.JsonAsync(HttpMethod.Post, RequestsRoute, protege, Statement(), HttpStatusCode.Created)).Id();

    /// <summary>The protégé's selection of <paramref name="mentor"/> for the request, through the API.</summary>
    public static Task<JsonElement> SelectAsync(WorkflowApi api, Guid request, Profile protege, Guid mentor) =>
        api.JsonAsync(HttpMethod.Post, RequestCandidates(request), protege, new { mentorEmployeeId = mentor }, HttpStatusCode.Created);

    /// <summary>The If-Match of a candidate as last read: its version as a strong entity tag.</summary>
    public static string Tag(JsonElement candidate) => $"\"{candidate.GetProperty("version").GetInt64()}\"";

    /// <summary>A decision on the candidate as last read, which must succeed; it returns the candidate as the decision leaves it.</summary>
    public static Task<JsonElement> DecideAsync(WorkflowApi api, string route, Profile caller, JsonElement candidate) =>
        api.JsonAsync(HttpMethod.Post, route, caller, ifMatch: Tag(candidate));

    /// <summary>A decision on the candidate as last read, whatever it answers.</summary>
    public static Task<HttpResponseMessage> TryAsync(WorkflowApi api, string route, Profile? caller, JsonElement candidate) =>
        api.SendAsync(HttpMethod.Post, route, caller, ifMatch: Tag(candidate));

    /// <summary>The Employee caller's new request paired with the Mentor caller, brought through the API to <paramref name="status"/>.</summary>
    public static async Task<JsonElement> CandidateAsync(WorkflowApi api, string status = "Invited")
    {
        var request = await SubmitAsync(api, Profile.Employee);
        var candidate = await SelectAsync(api, request, Profile.Employee, api.Employee(Profile.Mentor));
        if (status == "Invited")
        {
            return candidate;
        }

        candidate = await DecideAsync(api, Accept(candidate.Id()), Profile.Mentor, candidate);
        if (status == "Accepted")
        {
            return candidate;
        }

        candidate = await DecideAsync(api, Willingness(candidate.Id()), Profile.Employee, candidate);
        candidate = await DecideAsync(api, Willingness(candidate.Id()), Profile.Mentor, candidate);
        Assert.Equal("Drafting", candidate.Text("status"));
        return candidate;
    }

    /// <summary>A second approved mentor who can sign in: the Applicant caller, approved by Talent Management through the registry.</summary>
    public static async Task<Guid> ApproveApplicantAsync(WorkflowApi api)
    {
        var profile = await MentorRegistryApi.ProfileIdAsync(api.Db, api.Employee(Profile.Applicant));
        await api.JsonAsync(HttpMethod.Post, MentorRegistryApi.ApproveRoute(profile), Profile.Administrator);
        return api.Employee(Profile.Applicant);
    }

    /// <summary>The caller's inbox, newest first, as event type, subject and required action.</summary>
    public static async Task<List<(string EventType, Guid Subject, string RequiredAction)>> InboxAsync(WorkflowApi api, Profile recipient) =>
        [.. (await api.JsonAsync(HttpMethod.Get, Inbox, recipient)).Items("events")
            .Select(notification => (notification.Text("eventType"), notification.GetProperty("subjectId").GetGuid(), notification.Text("requiredAction")))];

    public static Task<string> StatusAsync(TestDatabase db, Guid candidate) =>
        db.ScalarAsync<string>(Login.Owner, "SELECT status FROM mentor.mentor_selection WHERE id = $1", candidate);

    public static Task<long> VersionAsync(TestDatabase db, Guid candidate) =>
        db.ScalarAsync<long>(Login.Owner, "SELECT version FROM mentor.mentor_selection WHERE id = $1", candidate);

    public static Task<long> RelationshipsAsync(TestDatabase db, Guid candidate) =>
        db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.mentorship_relationship WHERE candidate_id = $1", candidate);

    public static Task<long> AuditEntriesAsync(TestDatabase db, Guid candidate) =>
        db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.audit_entry WHERE candidate_id = $1", candidate);

    public static Task<long> NotificationsAsync(TestDatabase db, Guid candidate) =>
        db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.notification_event WHERE subject_id = $1", candidate);

    public static Guid RequestOf(JsonElement candidate) => candidate.GetProperty("requestId").GetGuid();

    private static string Route(string template, string parameter, Guid id) =>
        template.Replace($"{{{parameter}:guid}}", id.ToString(), StringComparison.Ordinal);
}

using System.Net;
using System.Text.Json;
using Mentor.Api.Employees;
using Mentor.Api.Hosting;
using Mentor.Core.Requests;
using Mentor.Tests.Integration.Persistence;
using Mentor.Tests.Integration.Support;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using static Mentor.Tests.Integration.Requests.PairingApi;

namespace Mentor.Tests.Integration.Requests;

// A protégé's request records who submitted it and what they stated, the goals apart from the expectations beyond
// technical skills (FR-014 to FR-016, DR-003), and stays a record of its own before and after formation (FR-017). Its
// protégé reads it, a mentor only while a candidate of it that has not ended names them, Talent Management any (ADR-003).
// COMP-MENTORSHIP-REQUEST.
public sealed class MentorshipRequestTests(PostgresServer server)
{
    private static readonly string[] RequestFields =
    [
        .. new[]
        {
            "developmentGoals", "disposition", "id", "motivation", "nontechnicalExpectations", "protegeDisplayName", "protegeEmployeeId",
            "submittedAt", "updatedAt",
        }.Order(),
    ];

    // AC-FR-014-01, AC-FR-015-01, AC-FR-016-01, AC-DR-003-01
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task A_protege_submits_a_request_that_records_who_they_are_and_keeps_their_goals_apart_from_their_other_expectations(IdentityMode mode)
    {
        await using var api = await WorkflowApi.StartAsync(server, mode);

        var response = await api.SendAsync(HttpMethod.Post, RequestsRoute, Profile.Employee,
            Statement(" To grow into a technical lead ", "Lead a design review; present at the architecture forum", "Confidence in difficult conversations "));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(Request(created.Id()), response.Headers.Location!.PathAndQuery);
        Assert.Equal(RequestFields, created.PropertyNames());
        Assert.Equal(api.Employee(Profile.Employee), created.GetProperty("protegeEmployeeId").GetGuid());
        Assert.Equal("Employee", created.Text("protegeDisplayName"));
        Assert.Equal("To grow into a technical lead", created.Text("motivation"));
        Assert.Equal("Lead a design review; present at the architecture forum", created.Text("developmentGoals"));
        Assert.Equal("Confidence in difficult conversations", created.Text("nontechnicalExpectations"));
        Assert.Equal("Open", created.Text("disposition"));
        Assert.NotNull(created.OptionalTime("submittedAt"));
        Assert.Equal(created.OptionalTime("submittedAt"), created.OptionalTime("updatedAt"));
        // Three statements, three columns.
        Assert.Equal(
            "To grow into a technical lead|Lead a design review; present at the architecture forum|Confidence in difficult conversations|OPEN",
            await api.Db.ScalarAsync<string>(Login.Owner,
                "SELECT motivation || '|' || development_goals || '|' || nontechnical_expectations || '|' || disposition FROM mentor.mentorship_request WHERE id = $1",
                created.Id()));
        Assert.Equal(created.GetRawText(), (await api.JsonAsync(HttpMethod.Get, Request(created.Id()), Profile.Employee)).GetRawText());
        Assert.Equal([created.Id()], (await api.JsonAsync(HttpMethod.Get, RequestsRoute, Profile.Employee)).EnumerateArray().Select(JsonElementExtensions.Id));
        Assert.Empty((await api.JsonAsync(HttpMethod.Get, RequestsRoute, Profile.Mentor)).EnumerateArray());
        await ProblemAssert.IsProblem(HttpStatusCode.Unauthorized, await api.SendAsync(HttpMethod.Post, RequestsRoute, profile: null, Statement()));
    }

    // AC-FR-014-01: the protégé is the authenticated caller; a body naming one, or a disposition, is malformed. Invalid
    // statements are refused without being repeated (NFR-002), and nothing is submitted either way.
    [Fact]
    public async Task A_request_names_no_protege_or_disposition_and_invalid_statements_submit_nothing()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var motivation = new string('m', RequestDetails.MaxLength + 1);

        var forAnother = await api.SendAsync(HttpMethod.Post, RequestsRoute, Profile.Employee,
            new { motivation = "To grow", developmentGoals = "Goals", nontechnicalExpectations = "Expectations", protegeEmployeeId = api.Employee(Profile.Mentor) });
        var formed = await api.SendAsync(HttpMethod.Post, RequestsRoute, Profile.Employee,
            new { motivation = "To grow", developmentGoals = "Goals", nontechnicalExpectations = "Expectations", disposition = "Formed" });
        var blank = await ProblemAssert.IsProblem(HttpStatusCode.BadRequest,
            await api.SendAsync(HttpMethod.Post, RequestsRoute, Profile.Employee, Statement(" ", "", null)));
        var overlong = await ProblemAssert.IsProblem(HttpStatusCode.BadRequest,
            await api.SendAsync(HttpMethod.Post, RequestsRoute, Profile.Employee, Statement(motivation)));

        await ProblemAssert.IsProblem(HttpStatusCode.BadRequest, forAnother);
        await ProblemAssert.IsProblem(HttpStatusCode.BadRequest, formed);
        Assert.Equal(
            ["developmentGoals", "motivation", "nontechnicalExpectations"],
            blank.GetProperty("errors").EnumerateObject().Select(error => error.Name).Order().ToArray());
        Assert.Equal(["motivation"], overlong.GetProperty("errors").EnumerateObject().Select(error => error.Name).ToArray());
        Assert.DoesNotContain(motivation, overlong.GetRawText());
        Assert.Equal(0L, await api.Db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.mentorship_request"));
    }

    // DR-001: a request is attributed to an employee record, which only the caller's own employee profile creates.
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task A_caller_without_an_employee_record_creates_their_employee_profile_before_submitting(IdentityMode mode)
    {
        await using var api = await WorkflowApi.StartAsync(server, mode);

        var refused = await ProblemAssert.IsProblem(HttpStatusCode.Conflict, await api.SendAsync(HttpMethod.Post, RequestsRoute, Profile.Newcomer, Statement()));

        Assert.Equal(EmployeeRecordRequired.Title, refused.Text("title"));
        Assert.Equal(0L, await api.Db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.mentorship_request"));
        Assert.Equal(0L, await api.Db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.employee WHERE entra_object_id = $1", api.Callers.ObjectId(Profile.Newcomer)));
        Assert.Empty((await api.JsonAsync(HttpMethod.Get, RequestsRoute, Profile.Newcomer)).EnumerateArray());

        var profile = await api.JsonAsync(HttpMethod.Get, EmployeeProfileEndpoints.OwnRoute, Profile.Newcomer);
        var submitted = await api.JsonAsync(HttpMethod.Post, RequestsRoute, Profile.Newcomer, Statement(), HttpStatusCode.Created);

        Assert.Equal(profile.Id(), submitted.GetProperty("protegeEmployeeId").GetGuid());
    }

    // ADR-003: the protégé reads their request, a mentor only while a candidate of it that has not ended names them,
    // Talent Management any; nobody else learns what it says.
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task A_request_is_read_by_its_protege_talent_management_and_a_mentor_only_while_a_standing_candidate_names_them(IdentityMode mode)
    {
        await using var api = await WorkflowApi.StartAsync(server, mode);
        var request = await SubmitAsync(api, Profile.Employee);

        await AssertReadersAsync(api, request, readers: [Profile.Employee, Profile.Administrator]);

        var invited = await SelectAsync(api, request, Profile.Employee, api.Employee(Profile.Mentor));
        await AssertReadersAsync(api, request, readers: [Profile.Employee, Profile.Mentor, Profile.Administrator]);

        // The mentor declines: the pairing has ended and names them no longer, though their own candidate stays theirs to read.
        var declined = await DecideAsync(api, Decline(invited.Id()), Profile.Mentor, invited);
        await AssertReadersAsync(api, request, readers: [Profile.Employee, Profile.Administrator]);
        Assert.Equal(declined.GetRawText(), (await api.JsonAsync(HttpMethod.Get, Candidate(invited.Id()), Profile.Mentor)).GetRawText());

        // A new pairing with the same mentor names them again.
        await SelectAsync(api, request, Profile.Employee, api.Employee(Profile.Mentor));
        await AssertReadersAsync(api, request, readers: [Profile.Employee, Profile.Mentor, Profile.Administrator]);

        Assert.Equal(HttpStatusCode.NotFound, await api.StatusAsync(HttpMethod.Get, Request(Guid.NewGuid()), Profile.Administrator));
        await ProblemAssert.IsProblem(HttpStatusCode.Unauthorized, await api.SendAsync(HttpMethod.Get, Request(request), profile: null));
    }

    // AC-FR-017-01, AC-DR-003-01: the request keeps its identity and its statements after a relationship is formed from
    // it, as a record apart from the candidate, the agreement and the relationship.
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task A_request_remains_identifiable_and_unchanged_after_a_relationship_is_formed_from_it(IdentityMode mode)
    {
        await using var api = await WorkflowApi.StartAsync(server, mode);
        var mentorship = await CallerMentorship.FormedAsync(api.Db, api.Callers);

        foreach (var viewer in new[] { Profile.Employee, Profile.Mentor, Profile.Administrator })
        {
            var request = await api.JsonAsync(HttpMethod.Get, Request(mentorship.Request), viewer);

            Assert.Equal(mentorship.Request, request.Id());
            Assert.Equal(mentorship.Protege, request.GetProperty("protegeEmployeeId").GetGuid());
            Assert.Equal(("To grow as an engineer", "Lead a design review", "Confidence"),
                (request.Text("motivation"), request.Text("developmentGoals"), request.Text("nontechnicalExpectations")));
        }

        var formed = Assert.Single((await api.JsonAsync(HttpMethod.Get, RequestCandidates(mentorship.Request), Profile.Employee)).EnumerateArray());
        Assert.Equal((mentorship.Candidate, "Formed", mentorship.Request), (formed.Id(), formed.Text("status"), RequestOf(formed)));
        Assert.Equal(mentorship.Request, await api.Db.ScalarAsync<Guid>(Login.Owner, """
            SELECT candidate.request_id
            FROM mentor.mentorship_relationship AS relationship
            JOIN mentor.mentor_selection AS candidate ON candidate.id = relationship.candidate_id
            WHERE relationship.id = $1
            """, mentorship.Relationship));
        Assert.Equal(HttpStatusCode.Forbidden, await api.StatusAsync(HttpMethod.Get, Request(mentorship.Request), Profile.Applicant));
    }

    // FR-017, ADR-003: the matrix has no action that edits a request, and none that changes a candidate's participants;
    // under these prefixes the API answers exactly these routes: the module's own, a candidate's meetings
    // (COMP-MEETINGS-NOTIFICATIONS), its agreement draft (COMP-AGREEMENT-DRAFTING), whose revisions never name parties,
    // and its formation (COMP-FORMATION), decided on a revision and read as an outcome.
    [Fact]
    public async Task No_route_edits_a_request_or_changes_a_candidates_participants()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var request = await SubmitAsync(api, Profile.Employee);
        var candidate = await SelectAsync(api, request, Profile.Employee, api.Employee(Profile.Mentor));

        Assert.Equal(HttpStatusCode.Forbidden, await api.StatusAsync(HttpMethod.Put, Request(request), Profile.Employee, Statement("Rewritten")));
        Assert.Equal(HttpStatusCode.Forbidden, await api.StatusAsync(HttpMethod.Put, Request(request), Profile.Administrator, Statement("Rewritten")));
        Assert.Equal(HttpStatusCode.Forbidden,
            await api.StatusAsync(HttpMethod.Put, Candidate(candidate.Id()), Profile.Employee, new { mentorEmployeeId = api.Employee(Profile.Applicant) }, Tag(candidate)));
        Assert.Equal(HttpStatusCode.Forbidden,
            await api.StatusAsync(HttpMethod.Patch, Candidate(candidate.Id()), Profile.Administrator, new { mentorEmployeeId = api.Employee(Profile.Applicant) }, Tag(candidate)));
        Assert.Equal("To grow into a technical lead", await api.Db.ScalarAsync<string>(Login.Owner, "SELECT motivation FROM mentor.mentorship_request WHERE id = $1", request));
        Assert.Equal(api.Employee(Profile.Mentor), await api.Db.ScalarAsync<Guid>(Login.Owner, "SELECT mentor_employee_id FROM mentor.mentor_selection WHERE id = $1", candidate.Id()));

        var routes = api.Api.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText!.StartsWith("/api/mentorship-requests", StringComparison.Ordinal)
                || endpoint.RoutePattern.RawText!.StartsWith("/api/candidates", StringComparison.Ordinal))
            .Select(endpoint => $"{string.Join(',', endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods)} {endpoint.RoutePattern.RawText}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
        [
            "GET /api/candidates",
            "GET /api/candidates/{candidateId:guid}",
            "GET /api/candidates/{candidateId:guid}/draft",
            "GET /api/candidates/{candidateId:guid}/draft/revisions/{revisionId:guid}",
            "GET /api/candidates/{candidateId:guid}/formation",
            "GET /api/candidates/{candidateId:guid}/meetings",
            "GET /api/mentorship-requests",
            "GET /api/mentorship-requests/{requestId:guid}",
            "GET /api/mentorship-requests/{requestId:guid}/candidates",
            "POST /api/candidates/{candidateId:guid}/accept",
            "POST /api/candidates/{candidateId:guid}/decline",
            "POST /api/candidates/{candidateId:guid}/draft/annex",
            "POST /api/candidates/{candidateId:guid}/draft/revisions",
            "POST /api/candidates/{candidateId:guid}/draft/revisions/{revisionId:guid}/acceptances",
            "POST /api/candidates/{candidateId:guid}/draft/revisions/{revisionId:guid}/formation-decisions",
            "POST /api/candidates/{candidateId:guid}/meetings",
            "POST /api/candidates/{candidateId:guid}/willingness",
            "POST /api/candidates/{candidateId:guid}/withdraw",
            "POST /api/mentorship-requests",
            "POST /api/mentorship-requests/{requestId:guid}/candidates",
        ], routes);
    }

    private static async Task AssertReadersAsync(WorkflowApi api, Guid request, Profile[] readers)
    {
        foreach (var caller in Enum.GetValues<Profile>())
        {
            var response = await api.SendAsync(HttpMethod.Get, Request(request), caller);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            if (readers.Contains(caller))
            {
                Assert.True(response.StatusCode == HttpStatusCode.OK, $"{caller}: {response.StatusCode}");
                Assert.Equal(request, JsonSerializer.Deserialize<JsonElement>(body).Id());
            }
            else
            {
                Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{caller}: {response.StatusCode}");
                Assert.DoesNotContain("technical lead", body);
            }
        }
    }
}

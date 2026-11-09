using System.Net;
using System.Text.Json;
using Mentor.Api.Employees;
using Mentor.Api.Hosting;
using Mentor.Api.Identity;
using Mentor.Api.MentorRegistry;
using Mentor.Core.Employees;
using Mentor.Core.MentorRegistry;
using Mentor.Infrastructure.Persistence;
using Mentor.Tests.Integration.Identity;
using Mentor.Tests.Integration.Persistence;
using Mentor.Tests.Integration.Support;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static Mentor.Tests.Integration.MentorRegistry.MentorRegistryApi;

namespace Mentor.Tests.Integration.MentorRegistry;

// Any authenticated employee applies once to mentor (FR-007, BR-001) and maintains their biography and attributes
// (FR-010, FR-011); once Talent Management approves, they are discoverable with every other approved mentor, and
// nothing but approval decides it (FR-008, FR-009, FR-012, FR-013, BR-002, DR-002). COMP-MENTOR-REGISTRY.
public sealed class MentorRegistryTests(PostgresServer server)
{
    private static readonly string[] ProfileFields =
    [
        "approvalDecidedAt", "approvalDecidedByEmployeeId", "approvalStatus", "biography", "createdAt", "displayName", "employeeId",
        "id", "industryExperience", "levelTitle", "mentoringExperience", "roleTitle", "updatedAt",
    ];

    // AC-FR-007-01, AC-BR-001-01: the registration action is available to an employee who has not applied, and once.
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task An_employee_who_has_not_applied_applies_once(IdentityMode mode)
    {
        await using var api = await WorkflowApi.StartAsync(server, mode);
        var employee = api.Employee(Profile.Employee);

        var response = await api.SendAsync(HttpMethod.Post, ApplicationsRoute, Profile.Employee, Details());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(ProfileRoute(created.Id()), response.Headers.Location!.PathAndQuery);
        Assert.Equal(ProfileFields, created.PropertyNames());
        Assert.Equal(employee, created.GetProperty("employeeId").GetGuid());
        Assert.Equal("Employee", created.Text("displayName"));
        Assert.Equal("Pending", created.Text("approvalStatus"));
        Assert.Null(created.OptionalGuid("approvalDecidedByEmployeeId"));
        Assert.Null(created.OptionalTime("approvalDecidedAt"));
        Assert.Equal(
            ("Ten years of platform delivery", "Principal Engineer", "Banking and telecoms", "Two graduates to senior"),
            (created.Text("biography"), created.Text("roleTitle"), created.Text("industryExperience"), created.Text("mentoringExperience")));
        Assert.Equal(created.GetRawText(), (await api.JsonAsync(HttpMethod.Get, OwnRoute, Profile.Employee)).GetRawText());
        Assert.Equal(created.GetRawText(), (await api.JsonAsync(HttpMethod.Get, ProfileRoute(created.Id()), Profile.Employee)).GetRawText());
        Assert.Equal(["Employee", "Applicant"], (await api.JsonAsync(HttpMethod.Get, SessionEndpoint.Route, Profile.Employee)).Strings("roles"));

        // Once: a second application, the applicant's or an approved mentor's, is refused and changes nothing.
        var again = await ProblemAssert.IsProblem(HttpStatusCode.Conflict, await api.SendAsync(HttpMethod.Post, ApplicationsRoute, Profile.Employee, Details("Another biography")));
        Assert.Equal(MentorRegistryEndpoints.AlreadyApplied, again.Text("title"));
        Assert.Equal(HttpStatusCode.Conflict, await api.StatusAsync(HttpMethod.Post, ApplicationsRoute, Profile.Applicant, Details()));
        Assert.Equal(HttpStatusCode.Conflict, await api.StatusAsync(HttpMethod.Post, ApplicationsRoute, Profile.Mentor, Details()));
        Assert.Equal(1L, await api.Db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.mentor_profile WHERE employee_id = $1", employee));
        Assert.Equal("Ten years of platform delivery", await api.Db.ScalarAsync<string>(Login.Owner, "SELECT biography FROM mentor.mentor_profile WHERE employee_id = $1", employee));
        // An administrator is an employee too (AC-FR-003-02); nobody applies anonymously.
        Assert.Equal(HttpStatusCode.Created, await api.StatusAsync(HttpMethod.Post, ApplicationsRoute, Profile.Administrator, Details()));
        await ProblemAssert.IsProblem(HttpStatusCode.Unauthorized, await api.SendAsync(HttpMethod.Post, ApplicationsRoute, profile: null, Details()));
    }

    // DR-001: an application is attributed to an employee record, which only the caller's own employee profile creates.
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task A_caller_without_an_employee_record_creates_their_employee_profile_before_applying(IdentityMode mode)
    {
        await using var api = await WorkflowApi.StartAsync(server, mode);
        var before = await api.Db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.mentor_profile");

        var refused = await ProblemAssert.IsProblem(HttpStatusCode.Conflict, await api.SendAsync(HttpMethod.Post, ApplicationsRoute, Profile.Newcomer, Details()));

        Assert.Equal(MentorRegistryEndpoints.NoEmployeeProfileYet, refused.Text("title"));
        Assert.Equal(before, await api.Db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.mentor_profile"));
        Assert.Equal(0L, await api.Db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.employee WHERE entra_object_id = $1", api.Callers.ObjectId(Profile.Newcomer)));
        var none = await ProblemAssert.IsProblem(HttpStatusCode.NotFound, await api.SendAsync(HttpMethod.Get, OwnRoute, Profile.Newcomer));
        Assert.Equal(MentorRegistryEndpoints.NoMentorApplication, none.Text("title"));

        var profile = await api.JsonAsync(HttpMethod.Get, EmployeeProfileEndpoints.OwnRoute, Profile.Newcomer);
        var applied = await api.JsonAsync(HttpMethod.Post, ApplicationsRoute, Profile.Newcomer, Details(), HttpStatusCode.Created);

        Assert.Equal(profile.Id(), applied.GetProperty("employeeId").GetGuid());
        Assert.Equal(profile.Text("displayName"), applied.Text("displayName"));
    }

    // NFR-002 and FR-008: invalid details are refused without being repeated; a body naming the approval or the
    // employee is malformed; nothing is created either way.
    [Fact]
    public async Task An_application_with_invalid_details_or_anything_but_the_profile_fields_creates_nothing()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var employee = api.Employee(Profile.Employee);
        var biography = new string('b', MentorProfileDetails.BiographyMaxLength + 1);
        var overlongBody = new
        {
            biography,
            roleTitle = new string('r', MentorProfileDetails.RoleTitleMaxLength + 1),
            industryExperience = new string('i', MentorProfileDetails.IndustryExperienceMaxLength + 1),
            mentoringExperience = new string('m', MentorProfileDetails.MentoringExperienceMaxLength + 1),
        };

        var blank = await ProblemAssert.IsProblem(HttpStatusCode.BadRequest, await api.SendAsync(HttpMethod.Post, ApplicationsRoute, Profile.Employee, Details(biography: "   ")));
        var overlong = await ProblemAssert.IsProblem(HttpStatusCode.BadRequest, await api.SendAsync(HttpMethod.Post, ApplicationsRoute, Profile.Employee, overlongBody));
        var selfApproved = await api.SendAsync(HttpMethod.Post, ApplicationsRoute, Profile.Employee, new { biography = "Ten years", approvalStatus = "Approved" });
        var forAnother = await api.SendAsync(HttpMethod.Post, ApplicationsRoute, Profile.Employee, new { biography = "Ten years", employeeId = api.Employee(Profile.Mentor) });

        Assert.Equal(["biography"], blank.GetProperty("errors").EnumerateObject().Select(error => error.Name).ToArray());
        Assert.Equal(
            ["biography", "industryExperience", "mentoringExperience", "roleTitle"],
            overlong.GetProperty("errors").EnumerateObject().Select(error => error.Name).Order().ToArray());
        Assert.DoesNotContain(biography, overlong.GetRawText());
        await ProblemAssert.IsProblem(HttpStatusCode.BadRequest, selfApproved);
        await ProblemAssert.IsProblem(HttpStatusCode.BadRequest, forAnother);
        Assert.Equal(0L, await api.Db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.mentor_profile WHERE employee_id = $1", employee));
        Assert.Equal(1L, await api.Db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.mentor_profile WHERE employee_id = $1", api.Employee(Profile.Mentor)));
    }

    // AC-FR-008-01, AC-FR-008-02 (the discovery share), AC-FR-009-01, AC-FR-009-02, AC-FR-009-03, AC-BR-001-01,
    // AC-BR-002-01, AC-DR-002-01: every approved mentor and only approved mentors, whatever their relationships.
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task Discovery_lists_every_approved_mentor_and_only_approved_mentors_whatever_their_relationships(IdentityMode mode)
    {
        await using var api = await WorkflowApi.StartAsync(server, mode);
        var administrator = api.Employee(Profile.Administrator);
        var mentor = api.Employee(Profile.Mentor);
        // The Mentor caller mentors three people already: two digitally formed relationships and a historical one.
        await CallerMentorship.FormedAsync(api.Db, api.Callers);
        await CallerMentorship.FormedAsync(api.Db, api.Callers);
        await CallerMentorship.LegacyAsync(api.Db, api.Callers);
        var (_, abebe) = await MentorAsync(api.Db, "Abebe Bikila", administrator);
        var (_, declined) = await MentorAsync(api.Db, "Zanele Dlamini", administrator, "DECLINED");
        var mentorProfile = await ProfileIdAsync(api.Db, mentor);
        var pending = await ProfileIdAsync(api.Db, api.Employee(Profile.Applicant));

        foreach (var viewer in Enum.GetValues<Profile>())
        {
            var mentors = await api.JsonAsync(HttpMethod.Get, MentorsRoute, viewer);
            Assert.Equal([abebe, mentorProfile], mentors.EnumerateArray().Select(JsonElementExtensions.Id));
        }

        var listed = (await api.JsonAsync(HttpMethod.Get, MentorsRoute, Profile.Employee)).EnumerateArray().ToList();
        Assert.Equal(["Abebe Bikila", "Mentor"], listed.Select(shown => shown.Text("displayName")));
        Assert.All(listed, shown =>
        {
            Assert.Equal(ProfileFields, shown.PropertyNames());
            Assert.Equal("Approved", shown.Text("approvalStatus"));
            Assert.Equal(administrator, shown.OptionalGuid("approvalDecidedByEmployeeId"));
        });
        Assert.DoesNotContain(listed, shown => shown.Id() == pending || shown.Id() == declined);
        Assert.Equal(3L, await api.Db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.mentorship_relationship WHERE mentor_employee_id = $1", mentor));
        await ProblemAssert.IsProblem(HttpStatusCode.Unauthorized, await api.SendAsync(HttpMethod.Get, MentorsRoute, profile: null));

        // Approval alone admits a profile to discovery; the decision itself is MentorApplicationDecisionTests.
        await api.Db.ExecuteAsync(Login.Owner,
            "UPDATE mentor.mentor_profile SET approval_status = 'APPROVED', approval_decided_by_employee_id = $1, approval_decided_at = now() WHERE id = $2",
            administrator, pending);
        Assert.Equal([abebe, pending, mentorProfile], (await api.JsonAsync(HttpMethod.Get, MentorsRoute, Profile.Employee)).EnumerateArray().Select(JsonElementExtensions.Id));
    }

    // AC-FR-010-02, AC-FR-012-01, ADR-003: an approved profile opens for every employee; a pending one for its owner
    // and Talent Management alone; the own route answers the caller's own profile or says there is none.
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task A_profile_is_read_by_its_owner_and_talent_management_and_once_approved_by_every_employee(IdentityMode mode)
    {
        await using var api = await WorkflowApi.StartAsync(server, mode);
        var approved = await ProfileIdAsync(api.Db, api.Employee(Profile.Mentor));
        var pending = await ProfileIdAsync(api.Db, api.Employee(Profile.Applicant));

        foreach (var viewer in Enum.GetValues<Profile>())
        {
            Assert.Equal(approved, (await api.JsonAsync(HttpMethod.Get, ProfileRoute(approved), viewer)).Id());
        }

        Assert.Equal(pending, (await api.JsonAsync(HttpMethod.Get, ProfileRoute(pending), Profile.Applicant)).Id());
        Assert.Equal(pending, (await api.JsonAsync(HttpMethod.Get, ProfileRoute(pending), Profile.Administrator)).Id());
        foreach (var other in new[] { Profile.Employee, Profile.Mentor, Profile.Newcomer })
        {
            var refused = await api.SendAsync(HttpMethod.Get, ProfileRoute(pending), other);
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.DoesNotContain("iography", await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        Assert.Equal(HttpStatusCode.NotFound, await api.StatusAsync(HttpMethod.Get, ProfileRoute(Guid.NewGuid()), Profile.Administrator));
        await ProblemAssert.IsProblem(HttpStatusCode.Unauthorized, await api.SendAsync(HttpMethod.Get, ProfileRoute(approved), profile: null));

        Assert.Equal(approved, (await api.JsonAsync(HttpMethod.Get, OwnRoute, Profile.Mentor)).Id());
        Assert.Equal(pending, (await api.JsonAsync(HttpMethod.Get, OwnRoute, Profile.Applicant)).Id());
        foreach (var without in new[] { Profile.Employee, Profile.Administrator, Profile.Newcomer })
        {
            var none = await ProblemAssert.IsProblem(HttpStatusCode.NotFound, await api.SendAsync(HttpMethod.Get, OwnRoute, without));
            Assert.Equal(MentorRegistryEndpoints.NoMentorApplication, none.Text("title"));
        }
    }

    // AC-FR-010-01, AC-FR-010-02, AC-FR-011-01: an applicant or mentor edits their own biography and attributes, kept
    // apart from each other; protégés then read them on a discoverable profile; the approval is not theirs to touch.
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task An_applicant_or_mentor_maintains_their_own_biography_and_attributes(IdentityMode mode)
    {
        await using var api = await WorkflowApi.StartAsync(server, mode);
        var mentorProfile = await ProfileIdAsync(api.Db, api.Employee(Profile.Mentor));
        var applicantProfile = await ProfileIdAsync(api.Db, api.Employee(Profile.Applicant));

        var edited = await api.JsonAsync(HttpMethod.Put, OwnRoute, Profile.Mentor,
            Details("Fifteen years in payments", "Head of Engineering", "Payments and retail banking", "Mentored four engineers to lead"));
        var applicant = await api.JsonAsync(HttpMethod.Put, OwnRoute, Profile.Applicant,
            Details("Platform engineer", roleTitle: null, industryExperience: " Telecoms ", mentoringExperience: "  "));
        var seenByProtege = await api.JsonAsync(HttpMethod.Get, ProfileRoute(mentorProfile), Profile.Employee);
        var discovered = (await api.JsonAsync(HttpMethod.Get, MentorsRoute, Profile.Employee)).EnumerateArray().Single(shown => shown.Id() == mentorProfile);

        Assert.Equal(mentorProfile, edited.Id());
        Assert.Equal(
            ("Fifteen years in payments", "Head of Engineering", "Payments and retail banking", "Mentored four engineers to lead"),
            (edited.Text("biography"), edited.Text("roleTitle"), edited.Text("industryExperience"), edited.Text("mentoringExperience")));
        Assert.Equal("Approved", edited.Text("approvalStatus"));
        Assert.Equal(api.Employee(Profile.Administrator), edited.OptionalGuid("approvalDecidedByEmployeeId"));
        Assert.Equal("Mentor", edited.Text("displayName"));
        Assert.True(edited.OptionalTime("updatedAt") > edited.OptionalTime("createdAt"));
        Assert.Equal(edited.GetRawText(), seenByProtege.GetRawText());
        Assert.Equal(edited.GetRawText(), discovered.GetRawText());
        Assert.Equal(applicantProfile, applicant.Id());
        Assert.Equal("Platform engineer", applicant.Text("biography"));
        Assert.Equal(JsonValueKind.Null, applicant.GetProperty("roleTitle").ValueKind);
        Assert.Equal("Telecoms", applicant.Text("industryExperience"));
        Assert.Equal(JsonValueKind.Null, applicant.GetProperty("mentoringExperience").ValueKind);
        Assert.Equal("Pending", applicant.Text("approvalStatus"));

        // The approval is not the owner's to change: a body naming it is malformed, and the row keeps its decision.
        await ProblemAssert.IsProblem(HttpStatusCode.BadRequest, await api.SendAsync(HttpMethod.Put, OwnRoute, Profile.Applicant, new { biography = "Platform engineer", approvalStatus = "Approved" }));
        Assert.Equal("PENDING", await ApprovalStatusAsync(api.Db, applicantProfile));
        // Nobody edits another's profile: the own route is the applicant's or mentor's alone, and there is no route by id.
        foreach (var other in new[] { Profile.Employee, Profile.Administrator, Profile.Newcomer })
        {
            Assert.Equal(HttpStatusCode.Forbidden, await api.StatusAsync(HttpMethod.Put, OwnRoute, other, Details()));
        }

        Assert.Equal(HttpStatusCode.Forbidden, await api.StatusAsync(HttpMethod.Put, ProfileRoute(mentorProfile), Profile.Administrator, Details()));
        Assert.Equal(HttpStatusCode.Forbidden, await api.StatusAsync(HttpMethod.Put, ProfileRoute(mentorProfile), Profile.Mentor, Details()));
        Assert.Equal("Fifteen years in payments", await api.Db.ScalarAsync<string>(Login.Owner, "SELECT biography FROM mentor.mentor_profile WHERE id = $1", mentorProfile));
    }

    // AC-FR-013-01, ADR-014: discovery is a filtered query in display-name order; the backend references no inference,
    // ranking or AI library and carries no configuration for one. The registry answers exactly these routes.
    [Fact]
    public async Task Discovery_is_a_plain_query_with_no_inference_or_ranking_service()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var administrator = api.Employee(Profile.Administrator);
        foreach (var name in new[] { "Zinhle Ndlovu", "Bongani Zulu", "Aisha Patel" })
        {
            await MentorAsync(api.Db, name, administrator);
        }

        var names = (await api.JsonAsync(HttpMethod.Get, MentorsRoute, Profile.Employee)).EnumerateArray().Select(shown => shown.Text("displayName")).ToArray();

        Assert.Equal(["Aisha Patel", "Bongani Zulu", "Mentor", "Zinhle Ndlovu"], names);
        string[] sections = ["AI", "OpenAI", "AzureOpenAI", "Inference", "Ranking", "Matching", "Recommendations"];
        string[] libraries = ["OpenAI", "Azure.AI", "Microsoft.ML", "SemanticKernel", "Anthropic", "Inference", "Microsoft.Extensions.AI"];
        var configured = api.Api.Services.GetRequiredService<IConfiguration>().GetChildren().Select(section => section.Key);
        Assert.DoesNotContain(configured, section => sections.Contains(section, StringComparer.OrdinalIgnoreCase));
        var referenced = new[] { typeof(Program).Assembly, typeof(Employee).Assembly, typeof(MentorDbContext).Assembly }
            .SelectMany(assembly => assembly.GetReferencedAssemblies())
            .Select(assembly => assembly.Name!);
        Assert.DoesNotContain(referenced, name => libraries.Any(library => name.Contains(library, StringComparison.OrdinalIgnoreCase)));
        var routes = api.Api.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText!.StartsWith("/api/mentor-", StringComparison.Ordinal) || endpoint.RoutePattern.RawText == MentorsRoute)
            .Select(endpoint => $"{string.Join(',', endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods)} {endpoint.RoutePattern.RawText}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
        [
            "GET /api/mentor-applications/pending",
            "GET /api/mentor-profiles/me",
            "GET /api/mentor-profiles/{id:guid}",
            "GET /api/mentors",
            "POST /api/mentor-applications",
            "POST /api/mentor-applications/{id:guid}/approve",
            "POST /api/mentor-applications/{id:guid}/decline",
            "PUT /api/mentor-profiles/me",
        ], routes);
    }
}

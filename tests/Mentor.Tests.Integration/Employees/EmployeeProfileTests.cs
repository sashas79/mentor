using System.Net;
using Mentor.Api.Employees;
using Mentor.Api.Hosting;
using Mentor.Api.Identity;
using Mentor.Core.Employees;
using Mentor.Tests.Integration.Identity;
using Mentor.Tests.Integration.Persistence;
using Mentor.Tests.Integration.Support;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static Mentor.Tests.Integration.Employees.EmployeeProfileApi;
using static Mentor.Tests.Integration.Identity.IdentityApi;

namespace Mentor.Tests.Integration.Employees;

// Employees create and maintain their own application profile (FR-005, BR-015) on a record keyed by their Entra object
// identifier (FR-002, DR-001); edits change display attributes only and never the identity (FR-006). The record is
// created on first authenticated access and only for the authenticated caller (DR-016). COMP-EMPLOYEE-PROFILE.
public sealed class EmployeeProfileTests(PostgresServer server)
{
    private const string Me = EmployeeProfileEndpoints.OwnRoute;

    // No Entra setting at all: a prototype run must not depend on any (NFR-010).
    private static readonly Dictionary<string, string> WithoutEntra = new()
    {
        ["AzureAd:Instance"] = "",
        ["AzureAd:TenantId"] = "",
        ["AzureAd:ClientId"] = "",
        ["AzureAd:Scopes"] = "",
    };

    // AC-DR-001-01, ADR-002: the first authenticated access creates the record, keyed by the object identifier and
    // seeded with the identity's display name; the session then names it, and no later access creates another.
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task The_first_authenticated_access_creates_the_callers_record_from_their_identity(IdentityMode mode)
    {
        await using var db = await server.CreateDatabaseAsync();
        var callers = await TestCallers.RecordAsync(db, mode);
        await using var api = Start(db, mode.ToString());
        var newcomer = As(callers, Profile.Newcomer);
        Assert.Null((await SessionAsync(api, newcomer)).EmployeeId());

        var created = await OwnProfileAsync(api, newcomer);
        var again = await OwnProfileAsync(api, newcomer);
        var session = await SessionAsync(api, newcomer);

        Assert.Equal(callers.ObjectId(Profile.Newcomer), created.EntraObjectId());
        Assert.Equal(session.GetProperty("displayName").GetString(), created.DisplayName());
        Assert.Null(created.LevelTitle());
        Assert.Equal(created.GetRawText(), again.GetRawText());
        Assert.Equal(created.Id(), session.EmployeeId());
        Assert.Equal(1L, await db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.employee WHERE entra_object_id = $1", callers.ObjectId(Profile.Newcomer)));
    }

    // AC-DR-001-01: a record carries a display name, so a first access seeds one only from a name a save would accept.
    // An identity whose token carries none (issued without the profile scope, or by a client other than the SPA) or
    // one beyond the limit creates no record and has nothing invented for it; the employee's own save creates the
    // record, with the details validated as every save is.
    [Theory]
    [InlineData("without a name claim")]
    [InlineData("with a name beyond the display-name limit")]
    public async Task A_first_access_by_an_identity_without_a_usable_display_name_creates_no_record_until_the_employee_saves_one(string token)
    {
        await using var db = await server.CreateDatabaseAsync();
        await using var api = Start(db);
        var objectId = Guid.NewGuid();
        var overlong = new string('n', ProfileDetails.DisplayNameMaxLength + 1);
        var me = Bearer(token switch
        {
            "without a name claim" => TestEntra.Token(objectId, name: null),
            "with a name beyond the display-name limit" => TestEntra.Token(objectId, overlong),
            _ => throw new ArgumentOutOfRangeException(nameof(token)),
        });

        var refused = await ProblemAssert.IsProblem(HttpStatusCode.NotFound, await CallAsync(api, HttpMethod.Get, Me, me));
        var again = await CallAsync(api, HttpMethod.Get, Me, me);
        var session = await SessionAsync(api, me);
        var none = await db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.employee WHERE entra_object_id = $1", objectId);
        var blank = await CallAsync(api, HttpMethod.Put, Me, me, new { displayName = " ", levelTitle = "Senior" });
        var saved = await SaveOwnProfileAsync(api, me, "Naledi Zulu", "Senior");
        var read = await OwnProfileAsync(api, me);

        Assert.Equal(EmployeeProfileEndpoints.NoProfileYet, refused.GetProperty("title").GetString());
        Assert.DoesNotContain(overlong, refused.GetRawText());
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Null(session.EmployeeId());
        Assert.Equal(0L, none);
        await ProblemAssert.IsProblem(HttpStatusCode.BadRequest, blank);
        Assert.Equal((objectId, "Naledi Zulu", "Senior"), (saved.EntraObjectId(), saved.DisplayName(), saved.LevelTitle()));
        Assert.Equal(saved.GetRawText(), read.GetRawText());
        Assert.Equal(saved.Id(), (await SessionAsync(api, me)).EmployeeId());
        Assert.Equal(1L, await db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.employee WHERE entra_object_id = $1", objectId));
        Assert.Equal(0L, await db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.employee WHERE display_name = '' OR length(display_name) > $1", ProfileDetails.DisplayNameMaxLength));
    }

    // BR-015: once created, the profile is maintained in the application; a later token names the same record and
    // overwrites nothing.
    [Fact]
    public async Task After_the_first_access_the_application_record_is_authoritative_for_the_display_attributes()
    {
        await using var db = await server.CreateDatabaseAsync();
        await using var api = Start(db);
        var objectId = Guid.NewGuid();

        var created = await OwnProfileAsync(api, Bearer(TestEntra.Token(objectId, "Naledi Zulu")));
        await SaveOwnProfileAsync(api, Bearer(TestEntra.Token(objectId, "Naledi Zulu")), "Naledi Zulu-Mahlangu", "Senior");
        var renamedInEntra = await OwnProfileAsync(api, Bearer(TestEntra.Token(objectId, "N. Mahlangu")));

        Assert.Equal("Naledi Zulu", created.DisplayName());
        Assert.Equal(created.Id(), renamedInEntra.Id());
        Assert.Equal(("Naledi Zulu-Mahlangu", "Senior"), (renamedInEntra.DisplayName(), renamedInEntra.LevelTitle()));
    }

    // AC-FR-005-01
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task An_authenticated_employee_saves_their_own_profile_details(IdentityMode mode)
    {
        await using var db = await server.CreateDatabaseAsync();
        var callers = await TestCallers.RecordAsync(db, mode);
        await using var api = Start(db, mode.ToString());
        var me = As(callers, Profile.Employee);

        var saved = await SaveOwnProfileAsync(api, me, "Thandiwe Mokoena", "Senior Developer");
        var read = await OwnProfileAsync(api, me);
        var cleared = await SaveOwnProfileAsync(api, me, "Thandiwe Mokoena", null);

        Assert.Equal(callers.EmployeeId(Profile.Employee), saved.Id());
        Assert.Equal(("Thandiwe Mokoena", "Senior Developer"), (saved.DisplayName(), saved.LevelTitle()));
        Assert.Equal(saved.GetRawText(), read.GetRawText());
        Assert.Null(cleared.LevelTitle());
        Assert.True(cleared.GetProperty("updatedAt").GetDateTimeOffset() >= saved.GetProperty("updatedAt").GetDateTimeOffset());
        Assert.Equal("Thandiwe Mokoena|", await db.ScalarAsync<string>(Login.Owner,
            "SELECT display_name || '|' || coalesce(level_title, '') FROM mentor.employee WHERE id = $1", saved.Id()));
    }

    // FR-005: saving is itself a first access when there is no record yet.
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task A_newcomer_saves_their_profile_as_their_first_access(IdentityMode mode)
    {
        await using var db = await server.CreateDatabaseAsync();
        var callers = await TestCallers.RecordAsync(db, mode);
        await using var api = Start(db, mode.ToString());
        var newcomer = As(callers, Profile.Newcomer);

        var saved = await SaveOwnProfileAsync(api, newcomer, "Anele Jacobs", "Graduate");

        Assert.Equal(callers.ObjectId(Profile.Newcomer), saved.EntraObjectId());
        Assert.Equal(("Anele Jacobs", "Graduate"), (saved.DisplayName(), saved.LevelTitle()));
        Assert.Equal(saved.Id(), (await SessionAsync(api, newcomer)).EmployeeId());
        Assert.Equal(1L, await db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.employee WHERE entra_object_id = $1", callers.ObjectId(Profile.Newcomer)));
    }

    // AC-FR-002-01
    [Fact]
    public async Task Two_employees_with_the_same_name_have_distinct_profiles()
    {
        await using var db = await server.CreateDatabaseAsync();
        await using var api = Start(db);
        var firstSam = Bearer(TestEntra.Token(Guid.NewGuid(), "Sam Pillay"));
        var secondSam = Bearer(TestEntra.Token(Guid.NewGuid(), "Sam Pillay"));

        var first = await OwnProfileAsync(api, firstSam);
        var second = await OwnProfileAsync(api, secondSam);
        await SaveOwnProfileAsync(api, firstSam, "Sam Pillay", "Principal Engineer");

        Assert.NotEqual(first.Id(), second.Id());
        Assert.Equal("Sam Pillay", first.DisplayName());
        Assert.Equal("Sam Pillay", second.DisplayName());
        Assert.Equal("Principal Engineer", (await OwnProfileAsync(api, firstSam)).LevelTitle());
        Assert.Null((await OwnProfileAsync(api, secondSam)).LevelTitle());
        Assert.Equal(2L, await db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.employee WHERE display_name = 'Sam Pillay'"));
    }

    // AC-FR-002-02: the mentor profile and request that reference the employee follow the record, not the name.
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task Changing_the_display_name_leaves_every_record_attached_to_the_same_employee(IdentityMode mode)
    {
        await using var db = await server.CreateDatabaseAsync();
        var callers = await TestCallers.RecordAsync(db, mode);
        var mentor = callers.EmployeeId(Profile.Mentor)!.Value;
        var request = await new Seed(db).RequestAsync(mentor);
        await using var api = Start(db, mode.ToString());
        var me = As(callers, Profile.Mentor);
        var before = await SessionAsync(api, me);

        var renamed = await SaveOwnProfileAsync(api, me, "Johan van der Merwe-Botha", "Principal");
        var after = await SessionAsync(api, me);
        var seenByTalentManagement = await ProfileOfAsync(await ProfileAsync(api, mentor, As(callers, Profile.Administrator)));

        Assert.Equal(mentor, renamed.Id());
        Assert.Equal(mentor, after.EmployeeId());
        Assert.Equal(before.Strings("roles"), after.Strings("roles"));
        Assert.Equal("Johan van der Merwe-Botha", seenByTalentManagement.DisplayName());
        Assert.Equal(1L, await db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.mentor_profile WHERE employee_id = $1 AND approval_status = 'APPROVED'", mentor));
        Assert.Equal(mentor, await db.ScalarAsync<Guid>(Login.Owner, "SELECT protege_employee_id FROM mentor.mentorship_request WHERE id = $1", request));
    }

    // AC-FR-006-01: an edit changes display attributes alone; a body that names the identity key or the record id is
    // refused outright, and the application's account could not change the key anyway (CallerResolutionTests).
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task Editing_profile_details_never_changes_the_linked_identity(IdentityMode mode)
    {
        await using var db = await server.CreateDatabaseAsync();
        var callers = await TestCallers.RecordAsync(db, mode);
        await using var api = Start(db, mode.ToString());
        var me = As(callers, Profile.Employee);
        var (id, objectId) = (callers.EmployeeId(Profile.Employee)!.Value, callers.ObjectId(Profile.Employee));

        var edited = await SaveOwnProfileAsync(api, me, "Thandiwe M.", "Lead");
        var smuggledKey = await CallAsync(api, HttpMethod.Put, Me, me, new { displayName = "Thandiwe M.", levelTitle = "Lead", entraObjectId = Guid.NewGuid() });
        var smuggledId = await CallAsync(api, HttpMethod.Put, Me, me, new { displayName = "Someone Else", id = Guid.NewGuid() });

        Assert.Equal((id, objectId), (edited.Id(), edited.EntraObjectId()));
        await ProblemAssert.IsProblem(HttpStatusCode.BadRequest, smuggledKey);
        await ProblemAssert.IsProblem(HttpStatusCode.BadRequest, smuggledId);
        Assert.Equal($"{objectId}|Thandiwe M.|Lead", await db.ScalarAsync<string>(Login.Owner,
            "SELECT entra_object_id::text || '|' || display_name || '|' || level_title FROM mentor.employee WHERE id = $1", id));
        Assert.Equal(id, (await SessionAsync(api, me)).EmployeeId());
    }

    // ADR-003: employee-profile.read is the owner's and Talent Management's; everyone else is refused on the record.
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task A_profile_is_read_by_id_by_its_owner_and_talent_management_alone(IdentityMode mode)
    {
        await using var db = await server.CreateDatabaseAsync();
        var callers = await TestCallers.RecordAsync(db, mode);
        await using var api = Start(db, mode.ToString());
        var id = callers.EmployeeId(Profile.Employee)!.Value;

        var own = await ProfileOfAsync(await ProfileAsync(api, id, As(callers, Profile.Employee)));
        var administrator = await ProfileOfAsync(await ProfileAsync(api, id, As(callers, Profile.Administrator)));

        Assert.Equal(id, own.Id());
        Assert.Equal(own.GetRawText(), administrator.GetRawText());
        foreach (var other in new[] { Profile.Newcomer, Profile.Applicant, Profile.Mentor })
        {
            await ProblemAssert.IsProblem(HttpStatusCode.Forbidden, await ProfileAsync(api, id, As(callers, other)));
        }

        await ProblemAssert.IsProblem(HttpStatusCode.Unauthorized, await ProfileAsync(api, id, signIn: null));
        // The record is checked before it is read, so only Talent Management can tell that an id names nobody.
        await ProblemAssert.IsProblem(HttpStatusCode.NotFound, await ProfileAsync(api, Guid.NewGuid(), As(callers, Profile.Administrator)));
        await ProblemAssert.IsProblem(HttpStatusCode.Forbidden, await ProfileAsync(api, Guid.NewGuid(), As(callers, Profile.Mentor)));
    }

    // AC-DR-001-01, AC-DR-016-02: the only way a record comes to exist is the authenticated caller's own first access;
    // no operation creates one from a name or an identifier, or edits another employee's, an administrator's included.
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task No_operation_creates_or_edits_a_record_for_anyone_but_the_caller(IdentityMode mode)
    {
        await using var db = await server.CreateDatabaseAsync();
        var callers = await TestCallers.RecordAsync(db, mode);
        await using var api = Start(db, mode.ToString());
        var administrator = As(callers, Profile.Administrator);
        var employee = callers.EmployeeId(Profile.Employee)!.Value;
        var before = await db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.employee");

        var editAnother = await CallAsync(api, HttpMethod.Put, Route(employee), administrator, new { displayName = "Renamed by Talent Management" });
        var createByName = await CallAsync(api, HttpMethod.Post, "/api/employees", administrator, new { displayName = "J. Unknown", entraObjectId = Guid.NewGuid() });
        var createForId = await CallAsync(api, HttpMethod.Put, Route(Guid.NewGuid()), administrator, new { displayName = "J. Unknown" });

        // Default deny (ADR-003) refuses a method the route does not answer before routing could say 405.
        Assert.Equal(HttpStatusCode.Forbidden, editAnother.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, createByName.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, createForId.StatusCode);
        Assert.Equal(before, await db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.employee"));
        Assert.Equal("Employee", await db.ScalarAsync<string>(Login.Owner, "SELECT display_name FROM mentor.employee WHERE id = $1", employee));
        var routes = api.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText!.StartsWith("/api/employees", StringComparison.Ordinal))
            .Select(endpoint => $"{string.Join(',', endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods)} {endpoint.RoutePattern.RawText}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["GET /api/employees/me", "GET /api/employees/{id:guid}", "PUT /api/employees/me"], routes);
    }

    [Fact]
    public async Task Invalid_details_are_refused_with_a_validation_problem_and_change_nothing()
    {
        await using var db = await server.CreateDatabaseAsync();
        var callers = await TestCallers.RecordAsync(db, IdentityMode.Entra);
        await using var api = Start(db);
        var me = As(callers, Profile.Employee);
        var (name, level) = (new string('n', ProfileDetails.DisplayNameMaxLength + 1), new string('l', ProfileDetails.LevelTitleMaxLength + 1));

        var blank = await ProblemAssert.IsProblem(HttpStatusCode.BadRequest, await CallAsync(api, HttpMethod.Put, Me, me, new { displayName = "  ", levelTitle = "Senior" }));
        var overlong = await ProblemAssert.IsProblem(HttpStatusCode.BadRequest, await CallAsync(api, HttpMethod.Put, Me, me, new { displayName = name, levelTitle = level }));

        Assert.Equal(["displayName"], blank.GetProperty("errors").EnumerateObject().Select(error => error.Name).ToArray());
        Assert.Equal(["displayName", "levelTitle"], overlong.GetProperty("errors").EnumerateObject().Select(error => error.Name).Order().ToArray());
        Assert.DoesNotContain(name, overlong.GetRawText());
        Assert.Equal("Employee", await db.ScalarAsync<string>(Login.Owner, "SELECT display_name FROM mentor.employee WHERE id = $1", callers.EmployeeId(Profile.Employee)));
    }

    // AC-FR-005-02, AC-BR-015-01, AC-NFR-010-01: a database and a labelled simulated identity are all a profile needs;
    // the host carries no configuration, package or client for Profile Creator, a Data Lake or any other profile source
    // (ADR-014).
    [Fact]
    public async Task The_profile_is_created_and_maintained_with_no_employee_profile_integration_configured()
    {
        await using var db = await server.CreateDatabaseAsync();
        await using var api = Start(db, "Simulated", WithoutEntra);
        var person = SimulatedRoster.People[0];
        var me = Simulated(person.ObjectId.ToString());

        var created = await OwnProfileAsync(api, me);
        var saved = await SaveOwnProfileAsync(api, me, person.DisplayName, "Developer");

        Assert.Equal(person.DisplayName, created.DisplayName());
        Assert.Equal(("Developer", "Simulated"), (saved.LevelTitle(), (await SessionAsync(api, me)).GetProperty("identityScheme").GetString()));
        string[] sections = ["DataLake", "ProfileCreator", "Graph", "EmployeeProfileSource"];
        string[] libraries = ["Microsoft.Graph", "Azure.Storage", "DataLake", "ProfileCreator"];
        var configured = api.Services.GetRequiredService<IConfiguration>().GetChildren().Select(section => section.Key);
        Assert.DoesNotContain(configured, section => sections.Contains(section, StringComparer.OrdinalIgnoreCase));
        var referenced = new[] { typeof(Program).Assembly, typeof(Employee).Assembly, typeof(Mentor.Infrastructure.Persistence.MentorDbContext).Assembly }
            .SelectMany(assembly => assembly.GetReferencedAssemblies())
            .Select(assembly => assembly.Name!);
        Assert.DoesNotContain(referenced, name => libraries.Any(library => name.Contains(library, StringComparison.OrdinalIgnoreCase)));
    }
}

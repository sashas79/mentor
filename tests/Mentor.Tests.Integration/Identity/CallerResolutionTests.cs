using Mentor.Api.Hosting;
using Mentor.Tests.Integration.Persistence;
using Mentor.Tests.Integration.Support;
using Npgsql;
using static Mentor.Tests.Integration.Identity.IdentityApi;

namespace Mentor.Tests.Integration.Identity;

// The caller is the employee their Entra object identifier keys, never one found by name (FR-002, FR-006, DR-001), and
// their functional roles follow from the app role and their own records (FR-003).
public sealed class CallerResolutionTests(PostgresServer server)
{
    // AC-FR-003-01
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task Roles_and_available_actions_follow_from_the_app_role_and_the_callers_own_records(IdentityMode mode)
    {
        await using var db = await server.CreateDatabaseAsync();
        var callers = await TestCallers.RecordAsync(db, mode);
        await using var api = Start(db, mode.ToString());

        var expectedRoles = new Dictionary<Profile, string[]>
        {
            [Profile.Newcomer] = ["Employee"],
            [Profile.Employee] = ["Employee"],
            [Profile.Applicant] = ["Employee", "Applicant"],
            [Profile.Mentor] = ["Employee", "Mentor"],
            [Profile.Administrator] = ["Employee", "TalentManagement"],
        };
        var permissions = new Dictionary<Profile, string[]>();
        foreach (var (profile, roles) in expectedRoles)
        {
            var session = await SessionAsync(api, As(callers, profile));

            Assert.Equal(roles, session.Strings("roles"));
            Assert.Equal(callers.EmployeeId(profile), session.EmployeeId());
            Assert.Equal(mode.ToString(), session.GetProperty("identityScheme").GetString());
            permissions[profile] = session.Strings("permissions");
        }

        Assert.Contains("mentor-profile.update", permissions[Profile.Applicant]);
        Assert.DoesNotContain("mentor-profile.update", permissions[Profile.Employee]);
        Assert.Contains("candidate.respond", permissions[Profile.Mentor]);
        Assert.DoesNotContain("candidate.respond", permissions[Profile.Applicant]);
        Assert.Contains("mentor-application.decide", permissions[Profile.Administrator]);
        Assert.All(new[] { Profile.Newcomer, Profile.Employee, Profile.Applicant, Profile.Mentor }, profile =>
            Assert.DoesNotContain("mentor-application.decide", permissions[profile]));
    }

    // AC-FR-003-02: being a mentor takes nothing away; a mentor can still request mentorship as a protégé.
    [Fact]
    public async Task An_approved_mentor_can_also_act_as_a_protege()
    {
        await using var db = await server.CreateDatabaseAsync();
        var callers = await TestCallers.RecordAsync(db, IdentityMode.Entra);
        await using var api = Start(db);

        var employee = await SessionAsync(api, As(callers, Profile.Employee));
        var mentor = await SessionAsync(api, As(callers, Profile.Mentor));

        Assert.Subset(mentor.Strings("permissions").ToHashSet(), employee.Strings("permissions").ToHashSet());
        Assert.Contains("mentorship-request.create", mentor.Strings("permissions"));
        Assert.Contains("goals.submit-progress", mentor.Strings("permissions"));
    }

    // AC-FR-002-01
    [Fact]
    public async Task Two_employees_with_the_same_name_are_distinct_callers()
    {
        await using var db = await server.CreateDatabaseAsync();
        var (first, second) = (Guid.NewGuid(), Guid.NewGuid());
        var firstEmployee = await EmployeeAsync(db, first, "Sam Pillay");
        var secondEmployee = await EmployeeAsync(db, second, "Sam Pillay");
        await using var api = Start(db);

        Assert.Equal(firstEmployee, (await SessionAsync(api, Bearer(TestEntra.Token(first, "Sam Pillay")))).EmployeeId());
        Assert.Equal(secondEmployee, (await SessionAsync(api, Bearer(TestEntra.Token(second, "Sam Pillay")))).EmployeeId());
    }

    // AC-FR-002-02, AC-FR-006-01, AC-DR-001-01: the display name is an attribute; the object identifier is the key, and
    // the application's database account cannot change it.
    [Fact]
    public async Task A_changed_display_name_leaves_the_caller_attached_to_the_same_employee()
    {
        await using var db = await server.CreateDatabaseAsync();
        var objectId = Guid.NewGuid();
        var employee = await EmployeeAsync(db, objectId, "Naledi Zulu");
        await using var api = Start(db);

        await db.ExecuteAsync(Login.App, "UPDATE mentor.employee SET display_name = 'Naledi Zulu-Mahlangu', level_title = 'Senior' WHERE id = $1", employee);
        var renamedInProfile = await SessionAsync(api, Bearer(TestEntra.Token(objectId, "Naledi Zulu")));
        var renamedInEntra = await SessionAsync(api, Bearer(TestEntra.Token(objectId, "N. Mahlangu")));

        Assert.Equal(employee, renamedInProfile.EmployeeId());
        Assert.Equal(employee, renamedInEntra.EmployeeId());
        var refusal = await Assert.ThrowsAsync<PostgresException>(() =>
            db.ExecuteAsync(Login.App, "UPDATE mentor.employee SET entra_object_id = gen_random_uuid() WHERE id = $1", employee));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, refusal.SqlState);
    }

    // DR-001, DR-016: only an authenticated identity is a caller; a name alone resolves to nobody.
    [Fact]
    public async Task A_caller_is_found_by_object_identifier_and_never_by_name()
    {
        await using var db = await server.CreateDatabaseAsync();
        await EmployeeAsync(db, Guid.NewGuid(), "Naledi Zulu");
        await using var api = Start(db);

        var namesake = await SessionAsync(api, Bearer(TestEntra.Token(Guid.NewGuid(), "Naledi Zulu")));

        Assert.Null(namesake.EmployeeId());
        Assert.Equal(1L, await db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.employee"));
    }
}

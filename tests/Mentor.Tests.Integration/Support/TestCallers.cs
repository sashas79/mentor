using System.Net.Http.Headers;
using Mentor.Api.Hosting;
using Mentor.Api.Identity;
using Mentor.Tests.Integration.Persistence;

namespace Mentor.Tests.Integration.Support;

/// <summary>The kinds of caller the authorisation matrix tells apart.</summary>
public enum Profile
{
    /// <summary>Authenticated, with no employee record yet.</summary>
    Newcomer,

    /// <summary>An employee with no mentor application and no app role; an IT operator signing in is one of these.</summary>
    Employee,

    /// <summary>An employee whose mentor application is pending.</summary>
    Applicant,

    /// <summary>An approved mentor.</summary>
    Mentor,

    /// <summary>A Talent Management administrator by app-role assignment.</summary>
    Administrator,
}

/// <summary>
/// One caller of each <see cref="Profile"/>, recorded in a test database exactly as the profile requires, with the
/// credential that signs them in under an identity mode: a <see cref="TestEntra"/> token, or a roster header.
/// </summary>
internal sealed class TestCallers
{
    private readonly IdentityMode mode;
    private readonly Dictionary<Profile, Guid> objectIds;
    private readonly Dictionary<Profile, Guid> employeeIds = [];

    private TestCallers(IdentityMode mode)
    {
        this.mode = mode;
        objectIds = mode == IdentityMode.Simulated
            ? new Dictionary<Profile, Guid>
            {
                [Profile.Newcomer] = Roster("Anele Jacobs"),
                [Profile.Employee] = Roster("Thandiwe Mokoena"),
                [Profile.Applicant] = Roster("Priya Naidoo"),
                [Profile.Mentor] = Roster("Johan van der Merwe"),
                [Profile.Administrator] = Roster("Lerato Khumalo"),
            }
            : Enum.GetValues<Profile>().ToDictionary(profile => profile, _ => Guid.NewGuid());
    }

    public static async Task<TestCallers> RecordAsync(TestDatabase db, IdentityMode mode)
    {
        var callers = new TestCallers(mode);
        foreach (var profile in Enum.GetValues<Profile>().Where(profile => profile != Profile.Newcomer))
        {
            callers.employeeIds[profile] = await db.ScalarAsync<Guid>(Login.Owner,
                "INSERT INTO mentor.employee (id, entra_object_id, display_name) VALUES (gen_random_uuid(), $1, $2) RETURNING id",
                callers.objectIds[profile], profile.ToString());
        }

        await db.ExecuteAsync(Login.Owner,
            "INSERT INTO mentor.mentor_profile (id, employee_id, biography, approval_status) VALUES (gen_random_uuid(), $1, 'Biography', 'PENDING')",
            callers.employeeIds[Profile.Applicant]);
        await db.ExecuteAsync(Login.Owner, """
            INSERT INTO mentor.mentor_profile (id, employee_id, biography, approval_status, approval_decided_by_employee_id, approval_decided_at)
            VALUES (gen_random_uuid(), $1, 'Biography', 'APPROVED', $2, now())
            """, callers.employeeIds[Profile.Mentor], callers.employeeIds[Profile.Administrator]);
        return callers;
    }

    public Guid ObjectId(Profile profile) => objectIds[profile];

    /// <summary>The employee record of the caller; a newcomer has none.</summary>
    public Guid? EmployeeId(Profile profile) => employeeIds.TryGetValue(profile, out var id) ? id : null;

    public HttpRequestMessage Request(HttpMethod method, string route, Profile? profile)
    {
        var request = new HttpRequestMessage(method, route);
        if (profile is not { } signedIn)
        {
            return request;
        }

        if (mode == IdentityMode.Simulated)
        {
            request.Headers.Add(SimulatedIdentityHandler.HeaderName, objectIds[signedIn].ToString());
        }
        else
        {
            var token = signedIn == Profile.Administrator
                ? TestEntra.AdministratorToken(objectIds[signedIn])
                : TestEntra.Token(objectIds[signedIn]);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    private static Guid Roster(string displayName) =>
        SimulatedRoster.People.Single(person => person.DisplayName == displayName).ObjectId;
}

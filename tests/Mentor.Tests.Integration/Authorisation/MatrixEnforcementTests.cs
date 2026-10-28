using System.Net;
using System.Security.Claims;
using Mentor.Api.Authorisation;
using Mentor.Api.Hosting;
using Mentor.Core.Requests;
using Mentor.Tests.Integration.Persistence;
using Mentor.Tests.Integration.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Mentor.Tests.Integration.Authorisation;

// The server enforces every row of the matrix, identically under Entra and the simulated identity (AC-FR-004-01,
// AC-FR-004-02, AC-NFR-001-01), with roles resolved from the authoritative store. Every denied row is exercised.
public sealed class MatrixEnforcementTests(PostgresServer server)
{
    private const string RecordProbe = "/probe/record/{permission}";

    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task Every_endpoint_admits_the_roles_the_matrix_grants_and_refuses_every_other_caller(IdentityMode mode)
    {
        await using var db = await server.CreateDatabaseAsync();
        var callers = await TestCallers.RecordAsync(db, mode);
        await using var host = await StartAsync(db, mode);

        var mismatches = new List<string>();
        foreach (var permission in ExpectedMatrix.Permissions)
        {
            var anonymous = await host.SendAsync(callers.Request(HttpMethod.Get, EndpointProbe(permission), profile: null));
            if (anonymous.StatusCode != HttpStatusCode.Unauthorized)
            {
                mismatches.Add($"anonymous {permission}: {anonymous.StatusCode}");
            }

            foreach (var profile in Enum.GetValues<Profile>())
            {
                var expected = ExpectedMatrix.ReachesEndpoint(permission, profile) ? HttpStatusCode.OK : HttpStatusCode.Forbidden;
                var response = await host.SendAsync(callers.Request(HttpMethod.Get, EndpointProbe(permission), profile));
                if (response.StatusCode != expected)
                {
                    mismatches.Add($"{profile} {permission}: {response.StatusCode}, expected {expected}");
                }
            }
        }

        Assert.Empty(mismatches);
    }

    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task Every_action_on_a_record_is_admitted_only_to_the_caller_standing_where_the_matrix_requires(IdentityMode mode)
    {
        await using var db = await server.CreateDatabaseAsync();
        var callers = await TestCallers.RecordAsync(db, mode);
        await using var host = await StartAsync(db, mode);

        var mismatches = new List<string>();
        foreach (var permission in ExpectedMatrix.Permissions)
        {
            foreach (var profile in Enum.GetValues<Profile>())
            {
                foreach (var standing in ExpectedMatrix.StandingsOf(profile))
                {
                    var expected = ExpectedMatrix.Admits(permission, profile, standing) ? HttpStatusCode.NoContent : HttpStatusCode.Forbidden;
                    var route = $"/probe/record/{permission}?{RecordQuery(standing, callers.EmployeeId(profile))}";
                    var response = await host.SendAsync(callers.Request(HttpMethod.Get, route, profile));
                    if (response.StatusCode != expected)
                    {
                        mismatches.Add($"{profile} as {standing} {permission}: {response.StatusCode}, expected {expected}");
                    }
                }
            }
        }

        Assert.Empty(mismatches);
    }

    // AC-FR-004-01: invoking the API directly, nobody but an administrator approves a mentor or edits a relationship,
    // the relationship's own parties included.
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task Only_an_administrator_can_approve_a_mentor_or_edit_a_relationship(IdentityMode mode)
    {
        await using var db = await server.CreateDatabaseAsync();
        var callers = await TestCallers.RecordAsync(db, mode);
        await using var host = await StartAsync(db, mode);

        foreach (var permission in new[] { AuthorisationMatrix.MentorApplication.Decide, AuthorisationMatrix.Relationship.UpdateAttributes })
        {
            foreach (var profile in Enum.GetValues<Profile>())
            {
                var expected = profile == Profile.Administrator ? HttpStatusCode.OK : HttpStatusCode.Forbidden;
                Assert.Equal(expected, (await host.SendAsync(callers.Request(HttpMethod.Get, EndpointProbe(permission.Name), profile))).StatusCode);
            }
        }

        foreach (var party in new[] { Standing.Protege, Standing.NamedMentor })
        {
            var route = $"/probe/record/{AuthorisationMatrix.Relationship.UpdateAttributes.Name}?{RecordQuery(party, callers.EmployeeId(Profile.Mentor))}";
            Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(callers.Request(HttpMethod.Get, route, Profile.Mentor))).StatusCode);
        }
    }

    // A module that passes the entity itself instead of its ProtectedRecord gets a refusal, never an endpoint-only check.
    [Fact]
    public async Task A_record_not_given_as_a_protected_record_is_refused()
    {
        await using var db = await server.CreateDatabaseAsync();
        var callers = await TestCallers.RecordAsync(db, IdentityMode.Entra);
        await using var host = await StartAsync(db, IdentityMode.Entra);

        var response = await host.SendAsync(callers.Request(HttpMethod.Get, "/probe/raw-record", Profile.Employee));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static string EndpointProbe(string permission) => $"/probe/matrix/{permission}";

    private static string RecordQuery(Standing standing, Guid? self)
    {
        var other = Guid.NewGuid();
        var me = self ?? other;
        return string.Join('&',
            $"owner={(standing == Standing.Owner ? me : other)}",
            $"protege={(standing == Standing.Protege ? me : other)}",
            $"mentor={(standing == Standing.NamedMentor ? me : other)}",
            $"discoverable={standing == Standing.Discoverable}");
    }

    private static Task<ProbeHost> StartAsync(TestDatabase db, IdentityMode mode) =>
        ProbeHost.StartAsync(
            mode,
            new Dictionary<string, string?> { ["ConnectionStrings:Mentor"] = db.ConnectionString(Login.App) },
            testIdentity: false,
            mapProbes: MapMatrixProbes);

    private static void MapMatrixProbes(WebApplication app)
    {
        foreach (var permission in AuthorisationMatrix.All)
        {
            app.MapGet(EndpointProbe(permission.Name), () => "reached").RequirePermission(permission);
        }

        // Any signed-in caller reaches it; the record check alone decides.
        app.MapGet(RecordProbe, async (string permission, Guid owner, Guid protege, Guid mentor, bool discoverable, ClaimsPrincipal user, IAuthorizationService authorization) =>
            {
                var record = new ProtectedRecord { Owner = owner, Protege = protege, NamedMentors = new HashSet<Guid> { mentor }, Discoverable = discoverable };
                return await authorization.IsPermittedAsync(user, AuthorisationMatrix.Find(permission)!, record) ? Results.NoContent() : Results.Forbid();
            })
            .RequirePermission(AuthorisationMatrix.Session.Read);

        app.MapGet("/probe/raw-record", async (ClaimsPrincipal user, IAuthorizationService authorization, Mentor.Api.Identity.CallerResolver callers, HttpContext context) =>
            {
                var caller = await callers.ResolveAsync(user, context.RequestAborted);
                var candidate = new MentorSelection { ProtegeEmployeeId = caller!.EmployeeId!.Value, MentorEmployeeId = Guid.NewGuid() };
                var result = await authorization.AuthorizeAsync(user, candidate, AuthorisationMatrix.Candidate.Read.Name);
                return result.Succeeded ? Results.NoContent() : Results.Forbid();
            })
            .RequirePermission(AuthorisationMatrix.Session.Read);
    }
}

using System.Net;
using System.Net.Http.Headers;
using Mentor.Api.Hosting;
using Mentor.Api.Identity;
using Mentor.Tests.Integration.Persistence;
using Mentor.Tests.Integration.Support;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using static Mentor.Tests.Integration.Identity.IdentityApi;

namespace Mentor.Tests.Integration.Identity;

// Production authentication is Microsoft Entra ID (FR-001, NFR-001, ADR-002): the API accepts only access tokens the
// company tenant issued to a signed-in employee for this API, and keys the caller by the token's object identifier.
public sealed class EntraBearerTests(PostgresServer server)
{
    // AC-FR-001-01: the Entra token is the only credential; the application has no password of its own.
    [Fact]
    public async Task An_employee_with_an_entra_token_is_signed_in_without_an_application_password()
    {
        await using var db = await server.CreateDatabaseAsync();
        var objectId = Guid.NewGuid();
        var employee = await EmployeeAsync(db, objectId, "Naledi Zulu");
        await using var api = Start(db);

        var session = await SessionAsync(api, Bearer(TestEntra.Token(objectId, "Naledi Zulu")));

        Assert.Equal(objectId, session.GetProperty("objectId").GetGuid());
        Assert.Equal("Naledi Zulu", session.GetProperty("displayName").GetString());
        Assert.Equal("Entra", session.GetProperty("identityScheme").GetString());
        Assert.Equal(employee, session.EmployeeId());
        Assert.Equal(["Employee"], session.Strings("roles"));
        await ProblemAssert.IsProblem(HttpStatusCode.Unauthorized, await SendAsync(api, SessionEndpoint.Route,
            request => request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String("naledi:password"u8))));
    }

    // AC-FR-001-02
    [Fact]
    public async Task An_unauthenticated_request_to_a_protected_resource_is_refused()
    {
        await using var db = await server.CreateDatabaseAsync();
        await using var api = Start(db);

        await ProblemAssert.IsProblem(HttpStatusCode.Unauthorized, await SendAsync(api, SessionEndpoint.Route));
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("issued for another API")]
    [InlineData("issued by another tenant")]
    [InlineData("issued by another authority")]
    [InlineData("signed with a key the tenant does not publish")]
    [InlineData("without a delegated scope")]
    [InlineData("with another API's scope")]
    [InlineData("app-only, even with the administrator app role")]
    [InlineData("without an object identifier")]
    public async Task A_token_not_issued_by_the_tenant_to_a_signed_in_employee_for_this_api_is_refused(string defect)
    {
        await using var db = await server.CreateDatabaseAsync();
        var objectId = Guid.NewGuid();
        await EmployeeAsync(db, objectId, "Naledi Zulu");
        await using var api = Start(db);
        var token = defect switch
        {
            "expired" => TestEntra.Token(objectId, expires: DateTime.UtcNow.AddHours(-1)),
            "issued for another API" => TestEntra.Token(objectId, audience: Guid.NewGuid().ToString()),
            "issued by another tenant" => TestEntra.Token(objectId, tenant: Guid.NewGuid()),
            "issued by another authority" => TestEntra.Token(objectId, issuer: "https://login.example.com/v2.0"),
            "signed with a key the tenant does not publish" => TestEntra.Token(objectId, key: TestEntra.ForeignKey),
            "without a delegated scope" => TestEntra.Token(objectId, scope: null),
            "with another API's scope" => TestEntra.Token(objectId, scope: "User.Read"),
            "app-only, even with the administrator app role" => TestEntra.Token(objectId, scope: null, roles: [MentorPrincipal.TalentManagementAppRole]),
            "without an object identifier" => TestEntra.Token(objectId, includeObjectId: false),
            _ => throw new ArgumentOutOfRangeException(nameof(defect)),
        };

        await ProblemAssert.IsProblem(HttpStatusCode.Unauthorized, await SendAsync(api, SessionEndpoint.Route, Bearer(token)));
    }

    // FR-003, ADR-002: the administrator role is the Entra app-role assignment; no other role a token names counts.
    [Fact]
    public async Task Only_the_talent_management_app_role_makes_a_caller_an_administrator()
    {
        await using var db = await server.CreateDatabaseAsync();
        var objectId = Guid.NewGuid();
        await EmployeeAsync(db, objectId, "Naledi Zulu");
        await using var api = Start(db);

        var administrator = await SessionAsync(api, Bearer(TestEntra.AdministratorToken(objectId)));
        var claimant = await SessionAsync(api, Bearer(TestEntra.Token(objectId, roles: ["TalentManagement", "Mentor", "Applicant", "Administrator"])));

        Assert.Equal(["Employee", "TalentManagement"], administrator.Strings("roles"));
        Assert.Equal(["Employee"], claimant.Strings("roles"));
    }

    // ADR-002: the schemes never coexist, so the production path reads no simulated identity.
    [Fact]
    public async Task Under_entra_a_simulated_identity_is_not_accepted_and_the_roster_is_not_published()
    {
        await using var db = await server.CreateDatabaseAsync();
        var administrator = SimulatedRoster.People.First(person => person.DeclaredRole == DeclaredRole.TalentManagementAdministrator);
        await using var api = Start(db);

        await ProblemAssert.IsProblem(HttpStatusCode.Unauthorized, await SendAsync(api, SessionEndpoint.Route, Simulated(administrator.ObjectId.ToString())));
        await ProblemAssert.IsProblem(HttpStatusCode.Unauthorized, await SendAsync(api, SimulatedRoster.Route));
    }

    [Theory]
    [InlineData("AzureAd:TenantId", "")]
    [InlineData("AzureAd:TenantId", "common")]
    [InlineData("AzureAd:ClientId", "")]
    [InlineData("AzureAd:Scopes", "")]
    [InlineData("AzureAd:Instance", "http://login.microsoftonline.com/")]
    public async Task Entra_mode_refuses_to_start_without_the_configuration_token_validation_needs(string key, string value)
    {
        await using var api = new ApiFactory(Environments.Production, new Dictionary<string, string> { [key] = value });

        var failure = Record.Exception(() => api.CreateClient());

        Assert.Contains(key, RefusalIn(failure).Message);
    }

    // NFR-002, NFR-009: a token names the employee; neither it nor its claims reach the logs, accepted or refused.
    [Fact]
    public async Task No_token_or_claim_reaches_the_logs()
    {
        await using var probe = await ProbeHost.StartAsync(
            settings: new Dictionary<string, string?> { ["Logging:LogLevel:Default"] = "Trace", ["Logging:LogLevel:Microsoft"] = "Trace" },
            testIdentity: false);
        var objectId = Guid.NewGuid();
        var accepted = TestEntra.Token(objectId, ProbeHost.Note);
        var refused = TestEntra.Token(objectId, ProbeHost.Note, key: TestEntra.ForeignKey);

        foreach (var token in new[] { accepted, refused })
        {
            var request = ProbeHost.Request(HttpMethod.Get, "/probe/permitted");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            await probe.SendAsync(request);
        }

        Assert.NotEmpty(probe.Logs.Entries);
        Assert.DoesNotContain(probe.Logs.Entries, entry => entry.Mentions(ProbeHost.Note) || entry.Mentions(objectId.ToString()));
        Assert.DoesNotContain(probe.Logs.Entries, entry => entry.Mentions(accepted.Split('.')[1]) || entry.Mentions(refused.Split('.')[2]));
    }

    // The host may wrap the validation failure.
    private static OptionsValidationException RefusalIn(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is OptionsValidationException found)
            {
                return found;
            }
        }

        throw new Xunit.Sdk.XunitException($"Expected the host to refuse to start, but got: {exception}");
    }
}

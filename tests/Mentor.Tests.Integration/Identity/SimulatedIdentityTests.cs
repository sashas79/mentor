using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mentor.Api.Hosting;
using Mentor.Api.Identity;
using Mentor.Tests.Integration.Persistence;
using Mentor.Tests.Integration.Support;
using static Mentor.Tests.Integration.Identity.IdentityApi;

namespace Mentor.Tests.Integration.Identity;

// A prototype runs with expressly identified simulated identities where Entra configuration is incomplete (NFR-010),
// and a simulated identity is labelled as such everywhere, so it is never taken for production authentication
// (AC-FR-001-03, AC-NFR-001-01).
public sealed class SimulatedIdentityTests(PostgresServer server)
{
    // No Entra setting at all: a simulated run must not depend on any.
    private static readonly Dictionary<string, string> WithoutEntra = new()
    {
        ["AzureAd:Instance"] = "",
        ["AzureAd:TenantId"] = "",
        ["AzureAd:ClientId"] = "",
        ["AzureAd:Scopes"] = "",
    };

    // AC-NFR-010-01
    [Fact]
    public async Task A_prototype_run_needs_no_entra_configuration_and_labels_every_simulated_caller()
    {
        await using var db = await server.CreateDatabaseAsync();
        await using var api = Start(db, "Simulated", WithoutEntra);
        var person = SimulatedRoster.People[0];

        var capabilities = await (await SendAsync(api, CapabilitiesEndpoint.Route)).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var roster = await (await SendAsync(api, SimulatedRoster.Route)).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var session = await SessionAsync(api, Simulated(person.ObjectId.ToString()));

        Assert.Equal("Simulated", capabilities.GetProperty("identityMode").GetString());
        Assert.Equal(SimulatedRoster.People.Count, roster.GetArrayLength());
        Assert.Equal(person.ObjectId, session.GetProperty("objectId").GetGuid());
        Assert.Equal(person.DisplayName, session.GetProperty("displayName").GetString());
        Assert.Equal("Simulated", session.GetProperty("identityScheme").GetString());
    }

    [Theory]
    [InlineData("8f14e45f-ceea-467a-9575-7b9b1a7e0c11")]
    [InlineData("Lerato Khumalo")]
    [InlineData("")]
    public async Task Nobody_outside_the_roster_can_be_simulated(string header)
    {
        await using var db = await server.CreateDatabaseAsync();
        await using var api = Start(db, "Simulated");

        await ProblemAssert.IsProblem(HttpStatusCode.Unauthorized, await SendAsync(api, SessionEndpoint.Route, Simulated(header)));
    }

    // The header names a person; the roster, not the caller, says what role they hold.
    [Fact]
    public async Task The_roster_alone_declares_who_is_an_administrator()
    {
        await using var db = await server.CreateDatabaseAsync();
        await using var api = Start(db, "Simulated");

        foreach (var person in SimulatedRoster.People)
        {
            var session = await SessionAsync(api, Simulated(person.ObjectId.ToString()));

            Assert.Equal(
                person.DeclaredRole == DeclaredRole.TalentManagementAdministrator,
                session.Strings("roles").Contains("TalentManagement"));
        }
    }

    [Fact]
    public async Task Under_simulation_an_entra_token_is_not_accepted()
    {
        await using var db = await server.CreateDatabaseAsync();
        var objectId = Guid.NewGuid();
        await EmployeeAsync(db, objectId, "Naledi Zulu");
        await using var api = Start(db, "Simulated");

        await ProblemAssert.IsProblem(HttpStatusCode.Unauthorized, await SendAsync(api, SessionEndpoint.Route, Bearer(TestEntra.AdministratorToken(objectId))));
    }
}

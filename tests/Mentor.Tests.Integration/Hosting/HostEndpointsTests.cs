using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mentor.Api.Authorisation;
using Mentor.Api.Hosting;
using Mentor.Api.Identity;
using Mentor.Tests.Integration.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Mentor.Tests.Integration.Hosting;

public sealed class HostEndpointsTests
{
    // The endpoints ADR-003 lets anyone call: health, capabilities, the API contract while it is served, and sign-in
    // support (the simulated roster, which exists only while identity is simulated).
    private static readonly HashSet<string> PublicRoutes =
        [HealthEndpoints.Live, HealthEndpoints.Ready, CapabilitiesEndpoint.Route, "/openapi/{documentName}.json", SimulatedRoster.Route];

    // Readiness depends on the database; DatabaseConnectionTests covers it.
    [Fact]
    public async Task Liveness_answers_without_credentials_or_a_database()
    {
        await using var api = new ApiFactory(Environments.Production);

        var response = await api.GetAsync(HealthEndpoints.Live);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_openapi_3_1_contract_is_served_when_enabled()
    {
        await using var api = new ApiFactory(Environments.Development);

        var response = await api.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.StartsWith("3.1", document.GetProperty("openapi").GetString());
        Assert.True(document.GetProperty("paths").TryGetProperty(CapabilitiesEndpoint.Route, out _));
    }

    [Fact]
    public async Task The_openapi_contract_is_not_served_unless_enabled()
    {
        await using var api = new ApiFactory(Environments.Production);

        await ProblemAssert.IsProblem(HttpStatusCode.Unauthorized, await api.GetAsync("/openapi/v1.json"));
    }

    [Theory]
    [InlineData("Entra")]
    [InlineData("Simulated")]
    public async Task A_route_outside_the_public_endpoints_is_refused_without_credentials(string identityMode)
    {
        await using var api = new ApiFactory(Environments.Development, new Dictionary<string, string> { ["Identity:Mode"] = identityMode });

        await ProblemAssert.IsProblem(HttpStatusCode.Unauthorized, await api.GetAsync("/api/relationships"));
    }

    // Guards every module added later: an endpoint must require permissions of the authorisation matrix and nothing else
    // (no bare roles, no ad hoc policy), or be a listed public one.
    [Theory]
    [InlineData("Entra")]
    [InlineData("Simulated")]
    public async Task Every_endpoint_requires_a_matrix_permission_or_is_a_listed_public_endpoint(string identityMode)
    {
        await using var api = new ApiFactory(Environments.Development, new Dictionary<string, string> { ["Identity:Mode"] = identityMode });

        var endpoints = RouteEndpoints(api);

        Assert.NotEmpty(endpoints);
        var violations = endpoints
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null
                ? !PublicRoutes.Contains(endpoint.RoutePattern.RawText!)
                : !RequiresMatrixPermission(endpoint))
            .Select(endpoint => endpoint.RoutePattern.RawText);
        Assert.Empty(violations);
    }

    // AC-BR-022-02: historical relationships are entered through the database role of ADR-008, never through the API.
    [Theory]
    [InlineData("Entra")]
    [InlineData("Simulated")]
    public async Task No_endpoint_enters_a_historical_relationship(string identityMode)
    {
        await using var api = new ApiFactory(Environments.Development, new Dictionary<string, string> { ["Identity:Mode"] = identityMode });

        Assert.DoesNotContain(RouteEndpoints(api), endpoint => endpoint.RoutePattern.RawText!.Contains("legacy", StringComparison.OrdinalIgnoreCase));
    }

    private static List<RouteEndpoint> RouteEndpoints(ApiFactory api) =>
        [.. api.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()];

    private static bool RequiresMatrixPermission(RouteEndpoint endpoint)
    {
        var authorize = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
        return authorize.Count > 0
            && authorize.All(data => AuthorisationMatrix.Find(data.Policy) is not null && string.IsNullOrEmpty(data.Roles))
            && endpoint.Metadata.GetMetadata<AuthorizationPolicy>() is null
            && endpoint.Metadata.GetMetadata<IAuthorizationRequirementData>() is null;
    }
}

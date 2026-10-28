using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mentor.Api.Hosting;
using Mentor.Tests.Integration.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Mentor.Tests.Integration.Hosting;

public sealed class HostEndpointsTests
{
    // The endpoints ADR-003 lets anyone call: health, capabilities, and the API contract while it is served.
    private static readonly HashSet<string> PublicRoutes =
        [HealthEndpoints.Live, HealthEndpoints.Ready, CapabilitiesEndpoint.Route, "/openapi/{documentName}.json"];

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

    // Guards every module added later: an endpoint must name the permission it enforces, or be a listed public one.
    [Fact]
    public async Task Every_endpoint_names_its_permission_or_is_a_listed_public_endpoint()
    {
        await using var api = new ApiFactory(Environments.Development);

        var endpoints = api.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();

        Assert.NotEmpty(endpoints);
        var violations = endpoints
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null
                ? !PublicRoutes.Contains(endpoint.RoutePattern.RawText!)
                : !NamesPermission(endpoint))
            .Select(endpoint => endpoint.RoutePattern.RawText);
        Assert.Empty(violations);
    }

    private static bool NamesPermission(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(data => !string.IsNullOrWhiteSpace(data.Policy) || !string.IsNullOrWhiteSpace(data.Roles))
        || endpoint.Metadata.GetMetadata<AuthorizationPolicy>() is not null
        || endpoint.Metadata.GetMetadata<IAuthorizationRequirementData>() is not null;
}

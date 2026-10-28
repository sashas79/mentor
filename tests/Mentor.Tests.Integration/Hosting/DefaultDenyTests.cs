using System.Net;
using Mentor.Api.Hosting;
using Mentor.Tests.Integration.Support;

namespace Mentor.Tests.Integration.Hosting;

// Authorisation is enforced by the server identically under either identity mode (AC-FR-004-01, AC-FR-004-02).
public sealed class DefaultDenyTests
{
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task An_endpoint_that_names_no_permission_is_refused_to_every_caller(IdentityMode identityMode)
    {
        await using var probe = await ProbeHost.StartAsync(identityMode);

        await ProblemAssert.IsProblem(HttpStatusCode.Unauthorized, await probe.SendAsync(ProbeHost.Request(HttpMethod.Get, "/probe/undeclared")));
        await ProblemAssert.IsProblem(HttpStatusCode.Forbidden, await probe.SendAsync(ProbeHost.Request(HttpMethod.Get, "/probe/undeclared", ProbeHost.PermittedRole)));
    }

    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task A_bare_authorize_is_no_permission_and_is_refused(IdentityMode identityMode)
    {
        await using var probe = await ProbeHost.StartAsync(identityMode);

        await ProblemAssert.IsProblem(HttpStatusCode.Forbidden, await probe.SendAsync(ProbeHost.Request(HttpMethod.Get, "/probe/bare", ProbeHost.PermittedRole)));
    }

    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task A_named_permission_admits_only_the_callers_that_hold_it(IdentityMode identityMode)
    {
        await using var probe = await ProbeHost.StartAsync(identityMode);

        var permitted = await probe.SendAsync(ProbeHost.Request(HttpMethod.Get, "/probe/permitted", ProbeHost.PermittedRole));
        Assert.Equal(HttpStatusCode.OK, permitted.StatusCode);
        await ProblemAssert.IsProblem(HttpStatusCode.Forbidden, await probe.SendAsync(ProbeHost.Request(HttpMethod.Get, "/probe/permitted", ProbeHost.OtherRole)));
        await ProblemAssert.IsProblem(HttpStatusCode.Unauthorized, await probe.SendAsync(ProbeHost.Request(HttpMethod.Get, "/probe/permitted")));
    }

    [Fact]
    public async Task An_explicitly_public_endpoint_needs_no_credentials()
    {
        await using var probe = await ProbeHost.StartAsync();

        var response = await probe.SendAsync(ProbeHost.Request(HttpMethod.Get, "/probe/public"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_route_is_refused_to_an_anonymous_caller_and_not_found_for_an_authenticated_one()
    {
        await using var probe = await ProbeHost.StartAsync();

        await ProblemAssert.IsProblem(HttpStatusCode.Unauthorized, await probe.SendAsync(ProbeHost.Request(HttpMethod.Get, "/probe/missing")));
        await ProblemAssert.IsProblem(HttpStatusCode.NotFound, await probe.SendAsync(ProbeHost.Request(HttpMethod.Get, "/probe/missing", ProbeHost.OtherRole)));
    }
}

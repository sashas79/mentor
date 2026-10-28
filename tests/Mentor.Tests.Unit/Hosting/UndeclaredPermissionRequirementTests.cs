using System.Security.Claims;
using Mentor.Api.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Mentor.Tests.Unit.Hosting;

public sealed class UndeclaredPermissionRequirementTests
{
    [Fact]
    public async Task A_matched_endpoint_never_meets_the_requirement()
    {
        var request = new DefaultHttpContext();
        request.SetEndpoint(new Endpoint(_ => Task.CompletedTask, EndpointMetadataCollection.Empty, "probe"));

        var context = await Evaluate(request);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task A_request_that_matched_no_endpoint_meets_it_so_routing_answers_404()
    {
        var context = await Evaluate(new DefaultHttpContext());

        Assert.True(context.HasSucceeded);
    }

    private static async Task<AuthorizationHandlerContext> Evaluate(HttpContext request)
    {
        var requirement = new UndeclaredPermissionRequirement();
        var context = new AuthorizationHandlerContext([requirement], new ClaimsPrincipal(new ClaimsIdentity("test")), request);
        await requirement.HandleAsync(context);
        return context;
    }
}

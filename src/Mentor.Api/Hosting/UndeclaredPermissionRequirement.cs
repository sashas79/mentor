using Microsoft.AspNetCore.Authorization;

namespace Mentor.Api.Hosting;

/// <summary>
/// The requirement of the default-deny policy (<see cref="DefaultDeny"/>). It is never met for a matched endpoint.
/// A request that matched no endpoint executes nothing, so it is let through and routing answers 404.
/// </summary>
internal sealed class UndeclaredPermissionRequirement
    : AuthorizationHandler<UndeclaredPermissionRequirement>, IAuthorizationRequirement
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, UndeclaredPermissionRequirement requirement)
    {
        if (context.Resource is HttpContext httpContext && httpContext.GetEndpoint() is null)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }

    public override string ToString() => "The endpoint names no permission, so it is denied by default (ADR-003).";
}

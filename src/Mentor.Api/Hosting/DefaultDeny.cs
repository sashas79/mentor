using Microsoft.AspNetCore.Authorization;

namespace Mentor.Api.Hosting;

/// <summary>
/// Server-side default deny (ADR-003, CONTRACT-AUTHORISATION-MATRIX). An endpoint is reachable only through a
/// permission it names (a policy, a policy object or roles) or an explicit AllowAnonymous. An endpoint that names
/// nothing, or carries a bare [Authorize], is refused to every caller: 401 without credentials, 403 with them. The
/// permissions endpoints name are those of the authorisation matrix (Mentor.Api.Authorisation).
/// </summary>
internal static class DefaultDeny
{
    public static IServiceCollection AddDefaultDenyAuthorization(this IServiceCollection services)
    {
        var deny = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new UndeclaredPermissionRequirement())
            .Build();
        services.AddAuthorizationBuilder()
            .SetDefaultPolicy(deny)
            .SetFallbackPolicy(deny);
        return services;
    }
}

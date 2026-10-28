using System.Security.Claims;
using Mentor.Api.Identity;
using Microsoft.AspNetCore.Authorization;

namespace Mentor.Api.Authorisation;

/// <summary>
/// Enforces the <see cref="AuthorisationMatrix"/> (ADR-003) through ASP.NET Core authorisation, which every module must
/// use: an endpoint requires a permission with <see cref="RequirePermission{TBuilder}"/>, and a command or query checks
/// the record it acts on with <see cref="IsPermittedAsync"/> before it reads or changes anything.
/// </summary>
public static class MatrixAuthorisation
{
    public static IServiceCollection AddAuthorisationMatrix(this IServiceCollection services)
    {
        var authorization = services.AddAuthorizationBuilder();
        foreach (var permission in AuthorisationMatrix.All)
        {
            authorization.AddPolicy(permission.Name, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(permission)));
        }

        services.AddHttpContextAccessor();
        services.AddScoped<IAuthorizationHandler, PermissionHandler>();
        return services;
    }

    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, Permission permission)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(permission.Name);

    /// <summary>Whether the caller may take the action on this record: their role and their relation to it.</summary>
    public static async Task<bool> IsPermittedAsync(this IAuthorizationService authorization, ClaimsPrincipal user, Permission permission, ProtectedRecord record) =>
        (await authorization.AuthorizeAsync(user, record, new PermissionRequirement(permission))).Succeeded;
}

internal sealed class PermissionRequirement(Permission permission) : IAuthorizationRequirement
{
    public Permission Permission { get; } = permission;

    public override string ToString() => $"Permission {Permission.Name} of the authorisation matrix (ADR-003).";
}

/// <summary>
/// Admits a caller the permission grants. At an endpoint the resource is the request and only the actor is checked; for
/// a <see cref="ProtectedRecord"/> the relation is checked too. Any other resource is refused, so that a record passed in
/// some other form can never be mistaken for an endpoint check.
/// </summary>
internal sealed class PermissionHandler(CallerResolver callers, IHttpContextAccessor httpContextAccessor)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        ProtectedRecord? record;
        switch (context.Resource)
        {
            case null or HttpContext:
                record = null;
                break;
            case ProtectedRecord protectedRecord:
                record = protectedRecord;
                break;
            default:
                return;
        }

        var cancellation = httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
        if (await callers.ResolveAsync(context.User, cancellation) is { } caller && requirement.Permission.Admits(caller, record))
        {
            context.Succeed(requirement);
        }
    }
}

using System.Security.Claims;
using Mentor.Api.Authorisation;
using Mentor.Core.Audit;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Mentor.Api.Identity;

/// <summary>
/// The signed-in caller as the server sees them. <see cref="Permissions"/> are the actions their roles reach, so the
/// frontend can offer what the server will allow (AC-FR-003-01); an action on a record is further limited to records the
/// caller stands in the required relation to. <see cref="IdentityScheme"/> Simulated marks a prototype identity.
/// </summary>
public sealed record Session(
    Guid ObjectId,
    string DisplayName,
    IdentityScheme IdentityScheme,
    Guid? EmployeeId,
    IReadOnlyList<FunctionalRole> Roles,
    int MatrixVersion,
    IReadOnlyList<string> Permissions);

internal static class SessionEndpoint
{
    public const string Route = "/api/session";

    public static IEndpointConventionBuilder MapSession(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet(Route, async Task<Results<Ok<Session>, ForbidHttpResult>> (ClaimsPrincipal user, CallerResolver callers, CancellationToken cancellationToken) =>
            {
                if (await callers.ResolveAsync(user, cancellationToken) is not { } caller)
                {
                    return TypedResults.Forbid();
                }

                return TypedResults.Ok(new Session(
                    caller.ObjectId,
                    caller.DisplayName,
                    caller.Scheme,
                    caller.EmployeeId,
                    [.. caller.Roles.Order()],
                    AuthorisationMatrix.Version,
                    [.. AuthorisationMatrix.All.Where(permission => permission.Admits(caller, null)).Select(permission => permission.Name)]));
            })
            .WithName("GetSession")
            .RequirePermission(AuthorisationMatrix.Session.Read);
}

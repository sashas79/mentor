using System.Security.Claims;
using System.Text.Json;
using Mentor.Api.Authorisation;
using Mentor.Core.Employees;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Mentor.Api.Employees;

/// <summary>
/// The endpoints of the employee profile module (COMP-EMPLOYEE-PROFILE, CONTRACT-MENTORSHIP-API). Each requires a
/// permission of the authorisation matrix (ADR-003): an employee reads and updates their own display attributes, and
/// Talent Management reads any profile. There is no operation that creates or edits a record for anyone but the
/// caller, and none that takes an identity key.
/// </summary>
public static class EmployeeProfileEndpoints
{
    /// <summary>The caller's own profile.</summary>
    public const string OwnRoute = "/api/employees/me";

    /// <summary>A profile by record id.</summary>
    public const string Route = "/api/employees/{id:guid}";

    /// <summary>
    /// The title of the problem GET <see cref="OwnRoute"/> answers when there is no record yet and the identity carries
    /// no display name that could seed one; PUT <see cref="OwnRoute"/> with the caller's details creates it.
    /// </summary>
    public const string NoProfileYet = "No profile yet";

    public static IServiceCollection AddEmployeeProfiles(this IServiceCollection services)
    {
        services.AddScoped<EmployeeProfiles>();
        return services;
    }

    public static IEndpointRouteBuilder MapEmployeeProfiles(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(OwnRoute, async Task<Results<Ok<EmployeeProfile>, ProblemHttpResult, ForbidHttpResult>> (ClaimsPrincipal user, EmployeeProfiles profiles, CancellationToken cancellationToken) =>
            {
                var lookup = await profiles.ReadOwnAsync(user, cancellationToken);
                if (lookup.AwaitsDetails)
                {
                    // The identity's name is not repeated: it is what a save would refuse (NFR-002).
                    return TypedResults.Problem(
                        statusCode: StatusCodes.Status404NotFound,
                        title: NoProfileYet,
                        detail: "The signed-in identity carries no display name a profile may hold, so this access did not create the record; save the profile with a display name to create it.");
                }

                if (lookup.Profile is not { } profile)
                {
                    return TypedResults.Forbid();
                }

                return TypedResults.Ok(profile);
            })
            .WithName("GetOwnEmployeeProfile")
            .RequirePermission(AuthorisationMatrix.EmployeeProfile.Read);

        endpoints.MapPut(OwnRoute, async Task<Results<Ok<EmployeeProfile>, ValidationProblem, ForbidHttpResult>> (EmployeeProfileEdit edit, ClaimsPrincipal user, EmployeeProfiles profiles, CancellationToken cancellationToken) =>
            {
                if (!ProfileDetails.TryCreate(edit.DisplayName, edit.LevelTitle, out var details, out var errors))
                {
                    return TypedResults.ValidationProblem(errors.ToDictionary(error => JsonNamingPolicy.CamelCase.ConvertName(error.Key), error => error.Value));
                }

                if (await profiles.SaveOwnAsync(user, details, cancellationToken) is not { } profile)
                {
                    return TypedResults.Forbid();
                }

                return TypedResults.Ok(profile);
            })
            .WithName("SaveOwnEmployeeProfile")
            .RequirePermission(AuthorisationMatrix.EmployeeProfile.Update);

        endpoints.MapGet(Route, async Task<Results<Ok<EmployeeProfile>, NotFound, ForbidHttpResult>> (Guid id, ClaimsPrincipal user, EmployeeProfiles profiles, CancellationToken cancellationToken) =>
            {
                var lookup = await profiles.ReadAsync(user, id, cancellationToken);
                if (!lookup.Exists)
                {
                    return TypedResults.NotFound();
                }

                if (lookup.Profile is not { } profile)
                {
                    return TypedResults.Forbid();
                }

                return TypedResults.Ok(profile);
            })
            .WithName("GetEmployeeProfile")
            .RequirePermission(AuthorisationMatrix.EmployeeProfile.Read);

        return endpoints;
    }
}

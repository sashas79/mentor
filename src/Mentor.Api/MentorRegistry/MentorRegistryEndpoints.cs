using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Text.Json;
using Mentor.Api.Authorisation;
using Mentor.Core.MentorRegistry;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Mentor.Api.MentorRegistry;

/// <summary>
/// The endpoints of the mentor registry (COMP-MENTOR-REGISTRY, CONTRACT-MENTORSHIP-API). Each requires a permission of
/// the authorisation matrix (ADR-003): any employee applies once and lists the approved mentors; an applicant or
/// mentor reads and maintains their own profile; any employee reads an approved profile; Talent Management reads any
/// profile, lists the pending applications and approves or declines them. No operation edits another employee's
/// profile, and none takes an approval from anyone but Talent Management.
/// </summary>
public static class MentorRegistryEndpoints
{
    public const string ApplicationsRoute = "/api/mentor-applications";
    public const string PendingApplicationsRoute = "/api/mentor-applications/pending";
    public const string ApproveRoute = "/api/mentor-applications/{id:guid}/approve";
    public const string DeclineRoute = "/api/mentor-applications/{id:guid}/decline";
    public const string OwnProfileRoute = "/api/mentor-profiles/me";
    public const string ProfileRoute = "/api/mentor-profiles/{id:guid}";
    public const string DiscoveryRoute = "/api/mentors";

    /// <summary>The problem a caller without an employee record gets: their own employee profile creates the record first.</summary>
    public const string NoEmployeeProfileYet = "No employee profile yet";

    /// <summary>The problem a second application gets: an employee applies once.</summary>
    public const string AlreadyApplied = "Already applied";

    /// <summary>The problem GET and PUT <see cref="OwnProfileRoute"/> answer for a caller who has not applied.</summary>
    public const string NoMentorApplication = "No mentor application";

    /// <summary>The problem a decision on a decided application gets.</summary>
    public const string AlreadyDecided = "Application already decided";

    private const string GetProfileName = "GetMentorProfile";
    private const string Tag = "MentorRegistry";

    public static IServiceCollection AddMentorRegistry(this IServiceCollection services)
    {
        services.AddScoped<MentorApplications>();
        return services;
    }

    public static IEndpointRouteBuilder MapMentorRegistry(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(ApplicationsRoute, ApplyAsync)
            .WithName("ApplyToMentor")
            .WithTags(Tag)
            .RequirePermission(AuthorisationMatrix.MentorApplication.Create);
        endpoints.MapGet(PendingApplicationsRoute, ListPendingAsync)
            .WithName("ListPendingMentorApplications")
            .WithTags(Tag)
            .RequirePermission(AuthorisationMatrix.MentorApplication.ListPending);
        endpoints.MapPost(ApproveRoute, ApproveAsync)
            .WithName("ApproveMentorApplication")
            .WithTags(Tag)
            .RequirePermission(AuthorisationMatrix.MentorApplication.Decide);
        endpoints.MapPost(DeclineRoute, DeclineAsync)
            .WithName("DeclineMentorApplication")
            .WithTags(Tag)
            .RequirePermission(AuthorisationMatrix.MentorApplication.Decide);
        endpoints.MapGet(OwnProfileRoute, GetOwnAsync)
            .WithName("GetOwnMentorProfile")
            .WithTags(Tag)
            .RequirePermission(AuthorisationMatrix.MentorProfile.Read);
        endpoints.MapPut(OwnProfileRoute, SaveOwnAsync)
            .WithName("SaveOwnMentorProfile")
            .WithTags(Tag)
            .RequirePermission(AuthorisationMatrix.MentorProfile.Update);
        endpoints.MapGet(ProfileRoute, GetAsync)
            .WithName(GetProfileName)
            .WithTags(Tag)
            .RequirePermission(AuthorisationMatrix.MentorProfile.Read);
        endpoints.MapGet(DiscoveryRoute, DiscoverAsync)
            .WithName("DiscoverMentors")
            .WithTags(Tag)
            .RequirePermission(AuthorisationMatrix.MentorDiscovery.List);
        return endpoints;
    }

    private static async Task<Results<CreatedAtRoute<MentorProfileView>, ValidationProblem, ForbidHttpResult, ProblemHttpResult>> ApplyAsync(
        MentorProfileEdit application, ClaimsPrincipal user, MentorApplications applications, CancellationToken cancellationToken)
    {
        if (!TryDetails(application, out var details, out var invalid))
        {
            return invalid;
        }

        var outcome = await applications.ApplyAsync(user, details, cancellationToken);
        return outcome.Refusal switch
        {
            MentorRegistryRefusal.None => TypedResults.CreatedAtRoute(outcome.Profile!, GetProfileName, new { id = outcome.Profile!.Id }),
            MentorRegistryRefusal.NoEmployeeRecord => NoEmployeeRecordProblem(),
            MentorRegistryRefusal.AlreadyApplied => Conflict(AlreadyApplied, "The employee has applied already; an employee applies once and maintains that one profile."),
            _ => TypedResults.Forbid(),
        };
    }

    private static async Task<Ok<IReadOnlyList<MentorProfileView>>> ListPendingAsync(MentorApplications applications, CancellationToken cancellationToken) =>
        TypedResults.Ok(await applications.PendingAsync(cancellationToken));

    private static Task<Results<Ok<MentorProfileView>, NotFound, ForbidHttpResult, ProblemHttpResult>> ApproveAsync(
        Guid id, ClaimsPrincipal user, MentorApplications applications, CancellationToken cancellationToken) =>
        DecideAsync(id, MentorApprovalStatus.Approved, user, applications, cancellationToken);

    private static Task<Results<Ok<MentorProfileView>, NotFound, ForbidHttpResult, ProblemHttpResult>> DeclineAsync(
        Guid id, ClaimsPrincipal user, MentorApplications applications, CancellationToken cancellationToken) =>
        DecideAsync(id, MentorApprovalStatus.Declined, user, applications, cancellationToken);

    private static async Task<Results<Ok<MentorProfileView>, ProblemHttpResult, ForbidHttpResult>> GetOwnAsync(
        ClaimsPrincipal user, MentorApplications applications, CancellationToken cancellationToken)
    {
        var outcome = await applications.ReadOwnAsync(user, cancellationToken);
        return outcome.Refusal switch
        {
            MentorRegistryRefusal.None => TypedResults.Ok(outcome.Profile!),
            MentorRegistryRefusal.NotFound => NoApplicationProblem(),
            _ => TypedResults.Forbid(),
        };
    }

    private static async Task<Results<Ok<MentorProfileView>, ValidationProblem, ProblemHttpResult, ForbidHttpResult>> SaveOwnAsync(
        MentorProfileEdit edit, ClaimsPrincipal user, MentorApplications applications, CancellationToken cancellationToken)
    {
        if (!TryDetails(edit, out var details, out var invalid))
        {
            return invalid;
        }

        var outcome = await applications.SaveOwnAsync(user, details, cancellationToken);
        return outcome.Refusal switch
        {
            MentorRegistryRefusal.None => TypedResults.Ok(outcome.Profile!),
            MentorRegistryRefusal.NotFound => NoApplicationProblem(),
            _ => TypedResults.Forbid(),
        };
    }

    private static async Task<Results<Ok<MentorProfileView>, NotFound, ForbidHttpResult>> GetAsync(
        Guid id, ClaimsPrincipal user, MentorApplications applications, CancellationToken cancellationToken)
    {
        var outcome = await applications.ReadAsync(user, id, cancellationToken);
        return outcome.Refusal switch
        {
            MentorRegistryRefusal.None => TypedResults.Ok(outcome.Profile!),
            MentorRegistryRefusal.NotFound => TypedResults.NotFound(),
            _ => TypedResults.Forbid(),
        };
    }

    private static async Task<Ok<IReadOnlyList<MentorProfileView>>> DiscoverAsync(MentorApplications applications, CancellationToken cancellationToken) =>
        TypedResults.Ok(await applications.DiscoverAsync(cancellationToken));

    private static async Task<Results<Ok<MentorProfileView>, NotFound, ForbidHttpResult, ProblemHttpResult>> DecideAsync(
        Guid id, MentorApprovalStatus decision, ClaimsPrincipal user, MentorApplications applications, CancellationToken cancellationToken)
    {
        var outcome = await applications.DecideAsync(user, id, decision, cancellationToken);
        return outcome.Refusal switch
        {
            MentorRegistryRefusal.None => TypedResults.Ok(outcome.Profile!),
            MentorRegistryRefusal.NotFound => TypedResults.NotFound(),
            MentorRegistryRefusal.AlreadyDecided => Conflict(AlreadyDecided, "The application has been approved or declined already; a decision is taken once."),
            MentorRegistryRefusal.NoEmployeeRecord => NoEmployeeRecordProblem(),
            _ => TypedResults.Forbid(),
        };
    }

    private static bool TryDetails(MentorProfileEdit edit, [NotNullWhen(true)] out MentorProfileDetails? details, [NotNullWhen(false)] out ValidationProblem? invalid)
    {
        if (MentorProfileDetails.TryCreate(edit.Biography, edit.RoleTitle, edit.IndustryExperience, edit.MentoringExperience, out details, out var errors))
        {
            invalid = null;
            return true;
        }

        invalid = TypedResults.ValidationProblem(errors.ToDictionary(error => JsonNamingPolicy.CamelCase.ConvertName(error.Key), error => error.Value));
        return false;
    }

    private static ProblemHttpResult NoEmployeeRecordProblem() => Conflict(
        NoEmployeeProfileYet,
        "The action is attributed to the caller's employee record, which their own employee profile creates on first access; open or save that profile first.");

    private static ProblemHttpResult NoApplicationProblem() => TypedResults.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: NoMentorApplication,
        detail: "The caller has not applied to mentor; the application creates the profile.");

    private static ProblemHttpResult Conflict(string title, string detail) =>
        TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: title, detail: detail);
}

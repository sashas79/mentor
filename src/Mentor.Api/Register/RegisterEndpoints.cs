using System.Security.Claims;
using Mentor.Api.Authorisation;
using Mentor.Api.Employees;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;

namespace Mentor.Api.Register;

/// <summary>
/// The endpoints of the relationship register and reports (COMP-REGISTER-REPORTING, CONTRACT-MENTORSHIP-API,
/// CONTRACT-REGISTER-EXPORT). These are open to Talent Management only, through the API as through the user interface
/// built on it (ADR-003, AC-FR-060-02):
/// <list type="bullet">
/// <item>the register as JSON (register.read);</item>
/// <item>its spreadsheet-compatible CSV export, audited (register.export);</item>
/// <item>the structured goals and progress of every relationship (register.read).</item>
/// </list>
/// Each answer is computed from the authoritative store when asked. Answers hold personal data, so no cache keeps one.
/// No route writes a relationship, a goal or a legacy row, and none takes a body.
/// </summary>
public static class RegisterEndpoints
{
    public const string RegisterRoute = "/api/register";
    public const string ExportRoute = "/api/register/export";
    public const string GoalsRoute = "/api/register/goals";

    private const string Tag = "Register";

    public static IServiceCollection AddRelationshipRegister(this IServiceCollection services)
    {
        services.AddScoped<RelationshipRegister>();
        return services;
    }

    public static IEndpointRouteBuilder MapRelationshipRegister(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(RegisterRoute, ReadAsync)
            .WithName("GetRelationshipRegister")
            .WithTags(Tag)
            .RequirePermission(AuthorisationMatrix.Register.Read);
        endpoints.MapGet(ExportRoute, ExportAsync)
            .WithName("ExportRelationshipRegister")
            .WithTags(Tag)
            .Produces(StatusCodes.Status200OK, typeof(byte[]), RegisterCsv.ContentType)
            .RequirePermission(AuthorisationMatrix.Register.Export);
        endpoints.MapGet(GoalsRoute, GoalsAsync)
            .WithName("GetRegisterGoalsAndProgress")
            .WithTags(Tag)
            .RequirePermission(AuthorisationMatrix.Register.Read);
        return endpoints;
    }

    private static async Task<Ok<IReadOnlyList<RegisterEntryView>>> ReadAsync(
        RelationshipRegister register, HttpResponse response, CancellationToken cancellationToken)
    {
        NoStore(response);
        return TypedResults.Ok(await register.ReadAsync(cancellationToken));
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> ExportAsync(
        ClaimsPrincipal user, RelationshipRegister register, HttpResponse response, CancellationToken cancellationToken)
    {
        NoStore(response);
        var (export, refusal) = await register.ExportAsync(user, cancellationToken);
        return export is not null
            ? TypedResults.File(export.Content, RegisterCsv.ContentType, export.FileName)
            : refusal switch
            {
                RegisterRefusal.NoEmployeeRecord => EmployeeRecordRequired.Problem(),
                _ => throw new InvalidOperationException($"Refusal {refusal} is not answered."),
            };
    }

    private static async Task<Ok<IReadOnlyList<RelationshipGoalsView>>> GoalsAsync(
        RelationshipRegister register, HttpResponse response, CancellationToken cancellationToken)
    {
        NoStore(response);
        return TypedResults.Ok(await register.GoalsAsync(cancellationToken));
    }

    private static void NoStore(HttpResponse response) => response.Headers[HeaderNames.CacheControl] = "no-store";
}

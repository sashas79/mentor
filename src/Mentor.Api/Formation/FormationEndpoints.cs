using System.Security.Claims;
using Mentor.Api.Agreements;
using Mentor.Api.Authorisation;
using Mentor.Api.Employees;
using Mentor.Api.Hosting;
using Mentor.Api.Requests;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Mentor.Api.Formation;

/// <summary>
/// The endpoints of formation (COMP-FORMATION, CONTRACT-MENTORSHIP-API). Only a Talent Management administrator decides
/// formation, on the current revision of the draft, naming the content hash reviewed; the participants and Talent
/// Management read the outcome (ADR-003). Formation is part of the candidate: the outcome carries the candidate's version
/// as its ETag, and a decision names it in If-Match (ADR-005). No route edits or removes a decision, a relationship's
/// formation, a formed agreement or a goal.
/// </summary>
public static class FormationEndpoints
{
    public const string FormationRoute = "/api/candidates/{candidateId:guid}/formation";
    public const string DecisionsRoute = "/api/candidates/{candidateId:guid}/draft/revisions/{revisionId:guid}/formation-decisions";

    /// <summary>The problem (409) of a formation decision before both participants have accepted the current revision.</summary>
    public const string NotAccepted = "Agreement not accepted";

    private const string Tag = "Formation";

    public static IServiceCollection AddFormation(this IServiceCollection services)
    {
        services.AddScoped<Formations>();
        return services;
    }

    public static IEndpointRouteBuilder MapFormation(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(FormationRoute, GetFormationAsync)
            .WithName("GetFormation")
            .WithTags(Tag)
            .RequirePermission(AuthorisationMatrix.Formation.Read);
        endpoints.MapPost(DecisionsRoute, DecideAsync)
            .WithName("DecideFormation")
            .WithTags(Tag)
            .RequirePermission(AuthorisationMatrix.Formation.Decide);
        return endpoints;
    }

    private static async Task<Results<Ok<FormationView>, NotFound, ForbidHttpResult>> GetFormationAsync(
        Guid candidateId, ClaimsPrincipal user, Formations formations, HttpResponse response, CancellationToken cancellationToken)
    {
        var outcome = await formations.ReadAsync(user, candidateId, cancellationToken);
        if (outcome.Value is { } formation)
        {
            VersionTag.Write(response, formation.Version);
            return TypedResults.Ok(formation);
        }

        return outcome.Refusal == FormationRefusal.NotFound ? TypedResults.NotFound() : TypedResults.Forbid();
    }

    private static async Task<Results<Ok<FormationView>, ValidationProblem, NotFound, ForbidHttpResult, ProblemHttpResult>> DecideAsync(
        Guid candidateId, Guid revisionId, FormationDecisionRequest decision, ClaimsPrincipal user, Formations formations, HttpRequest request, HttpResponse response,
        CancellationToken cancellationToken)
    {
        if (decision.Outcome is not { } outcome || !Enum.IsDefined(outcome) || string.IsNullOrWhiteSpace(decision.ContentHash))
        {
            var errors = new Dictionary<string, string[]>();
            if (decision.Outcome is not { } named || !Enum.IsDefined(named))
            {
                errors["outcome"] = ["The decision, Approved or Rejected, is required."];
            }

            if (string.IsNullOrWhiteSpace(decision.ContentHash))
            {
                errors["contentHash"] = ["The content hash of the revision reviewed is required."];
            }

            return TypedResults.ValidationProblem(errors);
        }

        var result = await formations.DecideAsync(user, candidateId, revisionId, DecidedOn(request), outcome, decision.ContentHash, cancellationToken);
        if (result.Value is { } formation)
        {
            VersionTag.Write(response, formation.Version);
            return TypedResults.Ok(formation);
        }

        return result.Refusal switch
        {
            FormationRefusal.NotFound => TypedResults.NotFound(),
            FormationRefusal.Forbidden => TypedResults.Forbid(),
            _ => Problem(result.Refusal),
        };
    }

    private static long? DecidedOn(HttpRequest request) => VersionTag.TryRead(request.Headers.IfMatch, out var version) ? version : null;

    private static ProblemHttpResult Problem(FormationRefusal refusal) => refusal switch
    {
        FormationRefusal.NoEmployeeRecord => EmployeeRecordRequired.Problem(),
        FormationRefusal.VersionRequired => Problem(StatusCodes.Status428PreconditionRequired, MentorshipRequestEndpoints.VersionRequired,
            "A formation decision names the candidate version it was taken on: send the ETag of the draft, as last read, in If-Match."),
        FormationRefusal.Stale => Problem(StatusCodes.Status409Conflict, MentorshipRequestEndpoints.CandidateChanged,
            "The candidate has changed since that version was read; read the draft again and decide on its current state."),
        FormationRefusal.NotInState => Problem(StatusCodes.Status409Conflict, MentorshipRequestEndpoints.NotPossibleNow,
            "Formation is decided while the candidate is drafting with both participants' willingness recorded, before it is formed or withdrawn."),
        FormationRefusal.RevisionSuperseded => Problem(StatusCodes.Status409Conflict, AgreementDraftEndpoints.RevisionSuperseded,
            "The revision named has been replaced; formation is decided on the current revision only. Read the draft again and review its current revision."),
        FormationRefusal.ContentMismatch => Problem(StatusCodes.Status422UnprocessableEntity, AgreementDraftEndpoints.ContentMismatch,
            "The content hash named is not the hash of the revision named; a formation decision names exactly the content reviewed."),
        FormationRefusal.NotAccepted => Problem(StatusCodes.Status409Conflict, NotAccepted,
            "Both participants accept the current revision before Talent Management decides its formation."),
        _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, "Not a refusal answered with a problem."),
    };

    private static ProblemHttpResult Problem(int status, string title, string detail) =>
        TypedResults.Problem(statusCode: status, title: title, detail: detail);
}

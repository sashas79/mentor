using System.Security.Claims;
using Mentor.Api.Authorisation;
using Mentor.Api.Employees;
using Mentor.Api.Hosting;
using Mentor.Api.Requests;
using Mentor.Core.Agreements;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Mentor.Api.Agreements;

/// <summary>
/// The endpoints of the agreement draft (COMP-AGREEMENT-DRAFTING, CONTRACT-MENTORSHIP-API). Each requires a permission of
/// the authorisation matrix (ADR-003), checked against the candidate the draft belongs to. The draft is part of the
/// candidate: every response carries the candidate's version as the ETag and every command names it in If-Match
/// (ADR-005). No route edits or deletes a revision, an acceptance or a permitted change; a revision is replaced by
/// another, and the annex is changed only by the route that records the change.
/// </summary>
public static class AgreementDraftEndpoints
{
    public const string DraftRoute = "/api/candidates/{candidateId:guid}/draft";
    public const string RevisionsRoute = "/api/candidates/{candidateId:guid}/draft/revisions";
    public const string RevisionRoute = "/api/candidates/{candidateId:guid}/draft/revisions/{revisionId:guid}";
    public const string AcceptancesRoute = "/api/candidates/{candidateId:guid}/draft/revisions/{revisionId:guid}/acceptances";
    public const string AnnexRoute = "/api/candidates/{candidateId:guid}/draft/annex";

    /// <summary>The problem (409) of an acceptance of a revision a replacement has superseded.</summary>
    public const string RevisionSuperseded = "Revision superseded";

    /// <summary>The problem (422) of an acceptance naming a content hash that is not the revision's.</summary>
    public const string ContentMismatch = "Content mismatch";

    /// <summary>The problem (409) of a replacement whose content is exactly the current revision's.</summary>
    public const string ContentUnchanged = "Content unchanged";

    /// <summary>The problem (409) of an annex change that changes nothing.</summary>
    public const string AnnexUnchanged = "Annex unchanged";

    private const string GetRevisionName = "GetAgreementRevision";
    private const string Tag = "AgreementDrafts";

    public static IServiceCollection AddAgreementDrafting(this IServiceCollection services)
    {
        services.AddScoped<AgreementDrafts>();
        return services;
    }

    public static IEndpointRouteBuilder MapAgreementDrafting(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(DraftRoute, GetDraftAsync)
            .WithName("GetAgreementDraft")
            .WithTags(Tag)
            .RequirePermission(AuthorisationMatrix.AgreementDraft.Read);
        endpoints.MapGet(RevisionRoute, GetRevisionAsync)
            .WithName(GetRevisionName)
            .WithTags(Tag)
            .RequirePermission(AuthorisationMatrix.AgreementDraft.Read);
        endpoints.MapPost(RevisionsRoute, ReviseAsync)
            .WithName("CreateAgreementRevision")
            .WithTags(Tag)
            .RequirePermission(AuthorisationMatrix.AgreementDraft.CreateRevision);
        endpoints.MapPost(AcceptancesRoute, AcceptAsync)
            .WithName("AcceptAgreementRevision")
            .WithTags(Tag)
            .RequirePermission(AuthorisationMatrix.AgreementDraft.Accept);
        endpoints.MapPost(AnnexRoute, ChangeAnnexAsync)
            .WithName("ChangeAdministrativeAnnex")
            .WithTags(Tag)
            .RequirePermission(AuthorisationMatrix.AgreementDraft.ChangeAnnex);
        return endpoints;
    }

    private static async Task<Results<Ok<AgreementDraftView>, NotFound, ForbidHttpResult>> GetDraftAsync(
        Guid candidateId, ClaimsPrincipal user, AgreementDrafts drafts, HttpResponse response, CancellationToken cancellationToken)
    {
        var outcome = await drafts.ReadAsync(user, candidateId, cancellationToken);
        if (outcome.Value is { } draft)
        {
            VersionTag.Write(response, draft.Version);
            return TypedResults.Ok(draft);
        }

        return outcome.Refusal == DraftRefusal.NotFound ? TypedResults.NotFound() : TypedResults.Forbid();
    }

    private static async Task<Results<Ok<AgreementRevisionView>, NotFound, ForbidHttpResult>> GetRevisionAsync(
        Guid candidateId, Guid revisionId, ClaimsPrincipal user, AgreementDrafts drafts, CancellationToken cancellationToken)
    {
        var outcome = await drafts.ReadRevisionAsync(user, candidateId, revisionId, cancellationToken);
        if (outcome.Value is { } revision)
        {
            return TypedResults.Ok(revision);
        }

        return outcome.Refusal == DraftRefusal.NotFound ? TypedResults.NotFound() : TypedResults.Forbid();
    }

    private static async Task<Results<CreatedAtRoute<AgreementDraftView>, ValidationProblem, NotFound, ForbidHttpResult, ProblemHttpResult>> ReviseAsync(
        Guid candidateId, AgreementDraftForm form, ClaimsPrincipal user, AgreementDrafts drafts, HttpRequest request, HttpResponse response, CancellationToken cancellationToken)
    {
        if (!AgreementDraftDetails.TryCreate(form, out var details, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        var outcome = await drafts.ReviseAsync(user, candidateId, DecidedOn(request), details, cancellationToken);
        if (outcome.Value is { CurrentRevision: { } revision } draft)
        {
            VersionTag.Write(response, draft.Version);
            return TypedResults.CreatedAtRoute(draft, GetRevisionName, new { candidateId, revisionId = revision.Id });
        }

        return outcome.Refusal switch
        {
            DraftRefusal.NotFound => TypedResults.NotFound(),
            DraftRefusal.Forbidden => TypedResults.Forbid(),
            _ => Problem(outcome.Refusal,
                "A revision is made while the candidate is drafting: after both participants' willingness and before formation or withdrawal."),
        };
    }

    private static async Task<Results<Ok<AgreementDraftView>, ValidationProblem, NotFound, ForbidHttpResult, ProblemHttpResult>> AcceptAsync(
        Guid candidateId, Guid revisionId, AcceptanceRequest acceptance, ClaimsPrincipal user, AgreementDrafts drafts, HttpRequest request, HttpResponse response,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(acceptance.ContentHash))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["contentHash"] = ["The content hash of the revision displayed is required."],
            });
        }

        var outcome = await drafts.AcceptAsync(user, candidateId, revisionId, DecidedOn(request), acceptance.ContentHash, cancellationToken);
        return Answer(outcome, response,
            "Each participant accepts the current revision once, for themselves, while the candidate is drafting.");
    }

    private static async Task<Results<Ok<AgreementDraftView>, ValidationProblem, NotFound, ForbidHttpResult, ProblemHttpResult>> ChangeAnnexAsync(
        Guid candidateId, AnnexChangeRequest change, ClaimsPrincipal user, AgreementDrafts drafts, HttpRequest request, HttpResponse response,
        CancellationToken cancellationToken)
    {
        if (!AnnexChange.TryCreate(change.Basis, change.Value, out var annexChange, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        var outcome = await drafts.ChangeAnnexAsync(user, candidateId, DecidedOn(request), annexChange, cancellationToken);
        return Answer(outcome, response,
            "The administrative annex of a draft is changed while the candidate is drafting, before formation or withdrawal.");
    }

    private static long? DecidedOn(HttpRequest request) => VersionTag.TryRead(request.Headers.IfMatch, out var version) ? version : null;

    private static Results<Ok<AgreementDraftView>, ValidationProblem, NotFound, ForbidHttpResult, ProblemHttpResult> Answer(
        DraftOutcome<AgreementDraftView> outcome, HttpResponse response, string notPossible)
    {
        if (outcome.Value is { } draft)
        {
            VersionTag.Write(response, draft.Version);
            return TypedResults.Ok(draft);
        }

        return outcome.Refusal switch
        {
            DraftRefusal.NotFound => TypedResults.NotFound(),
            DraftRefusal.Forbidden => TypedResults.Forbid(),
            _ => Problem(outcome.Refusal, notPossible),
        };
    }

    private static ProblemHttpResult Problem(DraftRefusal refusal, string notPossible) => refusal switch
    {
        DraftRefusal.NoEmployeeRecord => EmployeeRecordRequired.Problem(),
        DraftRefusal.VersionRequired => Problem(StatusCodes.Status428PreconditionRequired, MentorshipRequestEndpoints.VersionRequired,
            "A draft command names the candidate version it was taken on: send the ETag of the draft, as last read, in If-Match."),
        DraftRefusal.Stale => Problem(StatusCodes.Status409Conflict, MentorshipRequestEndpoints.CandidateChanged,
            "The candidate has changed since that version was read; read the draft again and act on its current state."),
        DraftRefusal.NotInState => Problem(StatusCodes.Status409Conflict, MentorshipRequestEndpoints.NotPossibleNow, notPossible),
        DraftRefusal.RevisionSuperseded => Problem(StatusCodes.Status409Conflict, RevisionSuperseded,
            "The revision named has been replaced; only the current revision can be accepted. Read the draft again and review its current revision."),
        DraftRefusal.ContentMismatch => Problem(StatusCodes.Status422UnprocessableEntity, ContentMismatch,
            "The content hash named is not the hash of the revision named; an acceptance names exactly the content displayed."),
        DraftRefusal.ContentUnchanged => Problem(StatusCodes.Status409Conflict, ContentUnchanged,
            "The content submitted is exactly the current revision's; a replacement revision changes the content."),
        DraftRefusal.AnnexUnchanged => Problem(StatusCodes.Status409Conflict, AnnexUnchanged,
            "The annex field already holds that value."),
        _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, "Not a refusal answered with a problem."),
    };

    private static ProblemHttpResult Problem(int status, string title, string detail) =>
        TypedResults.Problem(statusCode: status, title: title, detail: detail);
}

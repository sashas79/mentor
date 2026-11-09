using System.Security.Claims;
using System.Text.Json;
using Mentor.Api.Authorisation;
using Mentor.Api.Employees;
using Mentor.Api.Hosting;
using Mentor.Core.Requests;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Mentor.Api.Requests;

/// <summary>
/// The endpoints of mentorship requests and candidate pairing (COMP-MENTORSHIP-REQUEST, CONTRACT-MENTORSHIP-API). Each
/// requires a permission of the authorisation matrix (ADR-003). Every response for a candidate carries its version as
/// the ETag, and every pairing decision names that version in If-Match (ADR-005). No route edits a request or changes a
/// candidate's participants.
/// </summary>
public static class MentorshipRequestEndpoints
{
    public const string RequestsRoute = "/api/mentorship-requests";
    public const string RequestRoute = "/api/mentorship-requests/{requestId:guid}";
    public const string RequestCandidatesRoute = "/api/mentorship-requests/{requestId:guid}/candidates";
    public const string CandidatesRoute = "/api/candidates";
    public const string CandidateRoute = "/api/candidates/{candidateId:guid}";
    public const string AcceptRoute = "/api/candidates/{candidateId:guid}/accept";
    public const string DeclineRoute = "/api/candidates/{candidateId:guid}/decline";
    public const string WillingnessRoute = "/api/candidates/{candidateId:guid}/willingness";
    public const string WithdrawRoute = "/api/candidates/{candidateId:guid}/withdraw";

    /// <summary>The problem (428) of a decision that names no version in If-Match.</summary>
    public const string VersionRequired = "Version required";

    /// <summary>The problem (409) of a decision taken on a version that is no longer current.</summary>
    public const string CandidateChanged = "Candidate changed";

    /// <summary>The problem (409) of a decision the candidate's status does not admit, or one the caller has taken already.</summary>
    public const string NotPossibleNow = "Not possible in the candidate's current state";

    /// <summary>The problem (409) of a selection on a request a relationship has been formed from.</summary>
    public const string RequestClosed = "Request closed";

    /// <summary>The problem (409) of a selection while the request has a candidate that has not ended.</summary>
    public const string CandidateInProgress = "Candidate in progress";

    /// <summary>The problem (422) of a selection naming an employee who is not an approved mentor.</summary>
    public const string NotDiscoverable = "Not a discoverable mentor";

    /// <summary>The problem (422) of a protégé selecting themselves.</summary>
    public const string OwnRequest = "Own request";

    private const string GetRequestName = "GetMentorshipRequest";
    private const string GetCandidateName = "GetCandidate";
    private const string RequestsTag = "MentorshipRequests";
    private const string CandidatesTag = "Candidates";

    public static IServiceCollection AddMentorshipRequests(this IServiceCollection services)
    {
        services.AddScoped<MentorshipRequests>();
        services.AddScoped<Candidates>();
        return services;
    }

    public static IEndpointRouteBuilder MapMentorshipRequests(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(RequestsRoute, SubmitAsync)
            .WithName("SubmitMentorshipRequest")
            .WithTags(RequestsTag)
            .RequirePermission(AuthorisationMatrix.MentorshipRequest.Create);
        endpoints.MapGet(RequestsRoute, ListOwnRequestsAsync)
            .WithName("ListOwnMentorshipRequests")
            .WithTags(RequestsTag)
            .RequirePermission(AuthorisationMatrix.MentorshipRequest.Read);
        endpoints.MapGet(RequestRoute, GetRequestAsync)
            .WithName(GetRequestName)
            .WithTags(RequestsTag)
            .RequirePermission(AuthorisationMatrix.MentorshipRequest.Read);
        endpoints.MapGet(RequestCandidatesRoute, ListRequestCandidatesAsync)
            .WithName("ListRequestCandidates")
            .WithTags(CandidatesTag)
            .RequirePermission(AuthorisationMatrix.Candidate.Read);
        endpoints.MapPost(RequestCandidatesRoute, SelectAsync)
            .WithName("SelectMentor")
            .WithTags(CandidatesTag)
            .RequirePermission(AuthorisationMatrix.Candidate.Create);
        endpoints.MapGet(CandidatesRoute, ListOwnCandidatesAsync)
            .WithName("ListOwnCandidates")
            .WithTags(CandidatesTag)
            .RequirePermission(AuthorisationMatrix.Candidate.Read);
        endpoints.MapGet(CandidateRoute, GetCandidateAsync)
            .WithName(GetCandidateName)
            .WithTags(CandidatesTag)
            .RequirePermission(AuthorisationMatrix.Candidate.Read);
        endpoints.MapPost(AcceptRoute, AcceptAsync)
            .WithName("AcceptInvitation")
            .WithTags(CandidatesTag)
            .RequirePermission(AuthorisationMatrix.Candidate.Respond);
        endpoints.MapPost(DeclineRoute, DeclineAsync)
            .WithName("DeclineInvitation")
            .WithTags(CandidatesTag)
            .RequirePermission(AuthorisationMatrix.Candidate.Respond);
        endpoints.MapPost(WillingnessRoute, RecordWillingnessAsync)
            .WithName("RecordWillingness")
            .WithTags(CandidatesTag)
            .RequirePermission(AuthorisationMatrix.Candidate.RecordWillingness);
        endpoints.MapPost(WithdrawRoute, WithdrawAsync)
            .WithName("WithdrawCandidate")
            .WithTags(CandidatesTag)
            .RequirePermission(AuthorisationMatrix.Candidate.Withdraw);
        return endpoints;
    }

    private static async Task<Results<CreatedAtRoute<MentorshipRequestView>, ValidationProblem, ForbidHttpResult, ProblemHttpResult>> SubmitAsync(
        MentorshipRequestSubmission submission, ClaimsPrincipal user, MentorshipRequests requests, CancellationToken cancellationToken)
    {
        if (!RequestDetails.TryCreate(submission.Motivation, submission.DevelopmentGoals, submission.NontechnicalExpectations, out var details, out var errors))
        {
            return TypedResults.ValidationProblem(errors.ToDictionary(error => JsonNamingPolicy.CamelCase.ConvertName(error.Key), error => error.Value));
        }

        var outcome = await requests.SubmitAsync(user, details, cancellationToken);
        return outcome.Refusal switch
        {
            PairingRefusal.None => TypedResults.CreatedAtRoute(outcome.Value!, GetRequestName, new { requestId = outcome.Value!.Id }),
            PairingRefusal.NoEmployeeRecord => EmployeeRecordRequired.Problem(),
            _ => TypedResults.Forbid(),
        };
    }

    private static async Task<Ok<IReadOnlyList<MentorshipRequestView>>> ListOwnRequestsAsync(ClaimsPrincipal user, MentorshipRequests requests, CancellationToken cancellationToken) =>
        TypedResults.Ok(await requests.ListOwnAsync(user, cancellationToken));

    private static async Task<Results<Ok<MentorshipRequestView>, NotFound, ForbidHttpResult>> GetRequestAsync(
        Guid requestId, ClaimsPrincipal user, MentorshipRequests requests, CancellationToken cancellationToken)
    {
        var outcome = await requests.ReadAsync(user, requestId, cancellationToken);
        return outcome.Refusal switch
        {
            PairingRefusal.None => TypedResults.Ok(outcome.Value!),
            PairingRefusal.NotFound => TypedResults.NotFound(),
            _ => TypedResults.Forbid(),
        };
    }

    private static async Task<Results<Ok<IReadOnlyList<CandidateView>>, NotFound, ForbidHttpResult>> ListRequestCandidatesAsync(
        Guid requestId, ClaimsPrincipal user, MentorshipRequests requests, CancellationToken cancellationToken)
    {
        var outcome = await requests.CandidatesAsync(user, requestId, cancellationToken);
        return outcome.Refusal switch
        {
            PairingRefusal.None => TypedResults.Ok(outcome.Value!),
            PairingRefusal.NotFound => TypedResults.NotFound(),
            _ => TypedResults.Forbid(),
        };
    }

    private static async Task<Results<CreatedAtRoute<CandidateView>, ValidationProblem, NotFound, ForbidHttpResult, ProblemHttpResult>> SelectAsync(
        Guid requestId, CandidateSelection selection, ClaimsPrincipal user, Candidates candidates, HttpResponse response, CancellationToken cancellationToken)
    {
        if (selection.MentorEmployeeId is not { } mentor)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["mentorEmployeeId"] = ["The mentor to invite is required."] });
        }

        var outcome = await candidates.SelectAsync(user, requestId, mentor, cancellationToken);
        if (outcome.Value is { } candidate)
        {
            VersionTag.Write(response, candidate.Version);
            return TypedResults.CreatedAtRoute(candidate, GetCandidateName, new { candidateId = candidate.Id });
        }

        return outcome.Refusal switch
        {
            PairingRefusal.NotFound => TypedResults.NotFound(),
            PairingRefusal.Forbidden => TypedResults.Forbid(),
            _ => Problem(outcome.Refusal),
        };
    }

    private static async Task<Ok<IReadOnlyList<CandidateView>>> ListOwnCandidatesAsync(ClaimsPrincipal user, Candidates candidates, CancellationToken cancellationToken) =>
        TypedResults.Ok(await candidates.ListOwnAsync(user, cancellationToken));

    private static async Task<Results<Ok<CandidateView>, NotFound, ForbidHttpResult, ProblemHttpResult>> GetCandidateAsync(
        Guid candidateId, ClaimsPrincipal user, Candidates candidates, HttpResponse response, CancellationToken cancellationToken) =>
        Answer(await candidates.ReadAsync(user, candidateId, cancellationToken), response);

    private static async Task<Results<Ok<CandidateView>, NotFound, ForbidHttpResult, ProblemHttpResult>> AcceptAsync(
        Guid candidateId, ClaimsPrincipal user, Candidates candidates, HttpRequest request, HttpResponse response, CancellationToken cancellationToken) =>
        Answer(await candidates.RespondAsync(user, candidateId, DecidedOn(request), accept: true, cancellationToken), response,
            "The mentor answers an invitation once, while it is pending.");

    private static async Task<Results<Ok<CandidateView>, NotFound, ForbidHttpResult, ProblemHttpResult>> DeclineAsync(
        Guid candidateId, ClaimsPrincipal user, Candidates candidates, HttpRequest request, HttpResponse response, CancellationToken cancellationToken) =>
        Answer(await candidates.RespondAsync(user, candidateId, DecidedOn(request), accept: false, cancellationToken), response,
            "The mentor answers an invitation once, while it is pending.");

    private static async Task<Results<Ok<CandidateView>, NotFound, ForbidHttpResult, ProblemHttpResult>> RecordWillingnessAsync(
        Guid candidateId, ClaimsPrincipal user, Candidates candidates, HttpRequest request, HttpResponse response, CancellationToken cancellationToken) =>
        Answer(await candidates.RecordWillingnessAsync(user, candidateId, DecidedOn(request), cancellationToken), response,
            "Each participant records their own willingness once, after the mentor has accepted and before drafting begins.");

    private static async Task<Results<Ok<CandidateView>, NotFound, ForbidHttpResult, ProblemHttpResult>> WithdrawAsync(
        Guid candidateId, ClaimsPrincipal user, Candidates candidates, HttpRequest request, HttpResponse response, CancellationToken cancellationToken) =>
        Answer(await candidates.WithdrawAsync(user, candidateId, DecidedOn(request), cancellationToken), response,
            "A candidate is withdrawn before formation, and once; a declined, withdrawn or formed candidate has ended.");

    private static long? DecidedOn(HttpRequest request) => VersionTag.TryRead(request.Headers.IfMatch, out var version) ? version : null;

    private static Results<Ok<CandidateView>, NotFound, ForbidHttpResult, ProblemHttpResult> Answer(
        PairingOutcome<CandidateView> outcome, HttpResponse response, string? notPossible = null)
    {
        if (outcome.Value is { } candidate)
        {
            VersionTag.Write(response, candidate.Version);
            return TypedResults.Ok(candidate);
        }

        return outcome.Refusal switch
        {
            PairingRefusal.NotFound => TypedResults.NotFound(),
            PairingRefusal.Forbidden => TypedResults.Forbid(),
            _ => Problem(outcome.Refusal, notPossible),
        };
    }

    private static ProblemHttpResult Problem(PairingRefusal refusal, string? notPossible = null) => refusal switch
    {
        PairingRefusal.VersionRequired => Problem(StatusCodes.Status428PreconditionRequired, VersionRequired,
            "A pairing decision names the version it was taken on: send the candidate's ETag, as last read, in If-Match."),
        PairingRefusal.Stale => Problem(StatusCodes.Status409Conflict, CandidateChanged,
            "The candidate has changed since that version was read; read it again and decide on its current state."),
        PairingRefusal.NotInState => Problem(StatusCodes.Status409Conflict, NotPossibleNow, notPossible),
        PairingRefusal.RequestClosed => Problem(StatusCodes.Status409Conflict, RequestClosed,
            "A relationship has been formed from this request; a different mentor or different terms need a new request."),
        PairingRefusal.CandidateInProgress => Problem(StatusCodes.Status409Conflict, CandidateInProgress,
            "The request has a candidate that has not ended; it is withdrawn before another mentor is selected."),
        PairingRefusal.NotDiscoverable => Problem(StatusCodes.Status422UnprocessableEntity, NotDiscoverable,
            "The employee named is not an approved mentor."),
        PairingRefusal.OwnRequest => Problem(StatusCodes.Status422UnprocessableEntity, OwnRequest,
            "A protégé cannot select themselves as the mentor of their own request."),
        _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, "Not a refusal answered with a problem."),
    };

    private static ProblemHttpResult Problem(int status, string title, string? detail) =>
        TypedResults.Problem(statusCode: status, title: title, detail: detail);
}

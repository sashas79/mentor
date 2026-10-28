using System.Security.Claims;
using System.Text.Json.Serialization;
using Mentor.Api.Authorisation;
using Mentor.Api.Identity;
using Mentor.Core.Agreements;
using Mentor.Core.Meetings;
using Mentor.Core.Requests;
using Mentor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Mentor.Api.Meetings;

/// <summary>
/// What a participant records about a meeting (ADR-010): when it is proposed or scheduled, if known, and whether it was
/// arranged outside the application. Nothing else is accepted: a body carrying any other member, notes for one, is
/// refused, so no discussion content can reach the application through this contract (FR-059, NFR-002).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MeetingRequest(DateTimeOffset? ScheduledAt, bool ArrangedOutsideApplication);

/// <summary>A proposed introduction of a candidate or a planned session of a relationship (DR-013): its schedule and nothing about its content.</summary>
public sealed record MeetingView(
    Guid Id,
    MeetingKind Kind,
    Guid? CandidateId,
    Guid? RelationshipId,
    DateTimeOffset? ScheduledAt,
    bool ArrangedOutsideApplication,
    int? FrequencyWeeks,
    int? DurationMinutes,
    Guid CreatedByEmployeeId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static MeetingView Of(Meeting meeting) => new(
        meeting.Id,
        meeting.Kind,
        meeting.CandidateId,
        meeting.RelationshipId,
        meeting.ScheduledAt,
        meeting.ArrangedOutsideApplication,
        meeting.FrequencyWeeks,
        meeting.DurationMinutes,
        meeting.CreatedByEmployeeId,
        meeting.CreatedAt,
        meeting.UpdatedAt);
}

/// <summary>
/// The meeting schedule of a relationship (DR-013, FR-055): the meeting frequency and approximate duration both
/// participants accepted in the agreement, and the planned sessions. A relationship without a formed agreement, a
/// historical one, has no agreed terms.
/// </summary>
public sealed record MeetingSchedule(Guid RelationshipId, int? AgreedFrequencyWeeks, int? AgreedDurationMinutes, IReadOnlyList<MeetingView> Sessions);

/// <summary>
/// The meeting schedule representation (COMP-MEETINGS-NOTIFICATIONS, ADR-010): proposed introductions of a candidate
/// and planned recurring sessions of a relationship. The parties read and record meetings of their own candidate or
/// relationship and Talent Management reads them (ADR-003). No calendar, availability search or rescheduling workflow
/// is integrated, and no endpoint accepts or returns anything said in a meeting.
/// </summary>
internal static class MeetingEndpoints
{
    public const string CandidateMeetingsRoute = "/api/candidates/{candidateId:guid}/meetings";
    public const string RelationshipMeetingsRoute = "/api/relationships/{relationshipId:guid}/meetings";
    public const string MeetingRoute = "/api/meetings/{meetingId:guid}";

    private const string GetMeetingName = "GetMeeting";

    public static IEndpointRouteBuilder MapMeetings(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(CandidateMeetingsRoute, ListCandidateMeetingsAsync)
            .WithName("ListCandidateMeetings")
            .WithTags("Meetings")
            .RequirePermission(AuthorisationMatrix.Meeting.Read);
        endpoints.MapPost(CandidateMeetingsRoute, ProposeIntroductionAsync)
            .WithName("ProposeIntroductoryMeeting")
            .WithTags("Meetings")
            .RequirePermission(AuthorisationMatrix.Meeting.Record);
        endpoints.MapGet(RelationshipMeetingsRoute, GetScheduleAsync)
            .WithName("GetMeetingSchedule")
            .WithTags("Meetings")
            .RequirePermission(AuthorisationMatrix.Meeting.Read);
        endpoints.MapPost(RelationshipMeetingsRoute, PlanSessionAsync)
            .WithName("PlanRecurringSession")
            .WithTags("Meetings")
            .RequirePermission(AuthorisationMatrix.Meeting.Record);
        endpoints.MapGet(MeetingRoute, GetMeetingAsync)
            .WithName(GetMeetingName)
            .WithTags("Meetings")
            .RequirePermission(AuthorisationMatrix.Meeting.Read);
        endpoints.MapPut(MeetingRoute, UpdateMeetingAsync)
            .WithName("UpdateMeeting")
            .WithTags("Meetings")
            .RequirePermission(AuthorisationMatrix.Meeting.Record);
        return endpoints;
    }

    private static async Task<Results<Ok<IReadOnlyList<MeetingView>>, NotFound, ForbidHttpResult>> ListCandidateMeetingsAsync(
        Guid candidateId, ClaimsPrincipal user, IAuthorizationService authorization, MentorDbContext db, CancellationToken cancellationToken)
    {
        var candidate = await db.MentorSelections.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == candidateId, cancellationToken);
        if (candidate is null)
        {
            return TypedResults.NotFound();
        }

        if (!await authorization.IsPermittedAsync(user, AuthorisationMatrix.Meeting.Read, ProtectedRecord.Of(candidate)))
        {
            return TypedResults.Forbid();
        }

        return TypedResults.Ok(await MeetingsAsync(db.Meetings.Where(meeting => meeting.CandidateId == candidateId), cancellationToken));
    }

    // An introduction is arranged once the mentor has accepted and before formation (ADR-015); a meeting row is optional.
    private static async Task<Results<CreatedAtRoute<MeetingView>, NotFound, ForbidHttpResult, ProblemHttpResult>> ProposeIntroductionAsync(
        Guid candidateId, MeetingRequest request, ClaimsPrincipal user, CallerResolver callers, IAuthorizationService authorization, MentorDbContext db, CancellationToken cancellationToken)
    {
        var candidate = await db.MentorSelections.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == candidateId, cancellationToken);
        if (candidate is null)
        {
            return TypedResults.NotFound();
        }

        if (!await authorization.IsPermittedAsync(user, AuthorisationMatrix.Meeting.Record, ProtectedRecord.Of(candidate))
            || await callers.ResolveAsync(user, cancellationToken) is not { EmployeeId: { } recordedBy })
        {
            return TypedResults.Forbid();
        }

        if (candidate.Status is not (CandidateStatus.Accepted or CandidateStatus.Drafting))
        {
            return TypedResults.Problem(
                title: "An introduction is proposed once the mentor has accepted the invitation and before the relationship is formed.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var meeting = new Meeting
        {
            Kind = MeetingKind.Introductory,
            CandidateId = candidateId,
            ScheduledAt = request.ScheduledAt,
            ArrangedOutsideApplication = request.ArrangedOutsideApplication,
            CreatedByEmployeeId = recordedBy,
        };
        db.Meetings.Add(meeting);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.CreatedAtRoute(MeetingView.Of(meeting), GetMeetingName, new { meetingId = meeting.Id });
    }

    private static async Task<Results<Ok<MeetingSchedule>, NotFound, ForbidHttpResult>> GetScheduleAsync(
        Guid relationshipId, ClaimsPrincipal user, IAuthorizationService authorization, MentorDbContext db, CancellationToken cancellationToken)
    {
        var relationship = await db.MentorshipRelationships.AsNoTracking().SingleOrDefaultAsync(relationship => relationship.Id == relationshipId, cancellationToken);
        if (relationship is null)
        {
            return TypedResults.NotFound();
        }

        if (!await authorization.IsPermittedAsync(user, AuthorisationMatrix.Meeting.Read, ProtectedRecord.Of(relationship)))
        {
            return TypedResults.Forbid();
        }

        var terms = await AgreedTermsAsync(db, relationshipId, cancellationToken);
        var sessions = await MeetingsAsync(db.Meetings.Where(meeting => meeting.RelationshipId == relationshipId), cancellationToken);
        return TypedResults.Ok(new MeetingSchedule(relationshipId, terms.FrequencyWeeks, terms.DurationMinutes, sessions));
    }

    // The agreed frequency and duration are copied from the formed agreement onto the session (ADR-010).
    private static async Task<Results<CreatedAtRoute<MeetingView>, NotFound, ForbidHttpResult>> PlanSessionAsync(
        Guid relationshipId, MeetingRequest request, ClaimsPrincipal user, CallerResolver callers, IAuthorizationService authorization, MentorDbContext db, CancellationToken cancellationToken)
    {
        var relationship = await db.MentorshipRelationships.AsNoTracking().SingleOrDefaultAsync(relationship => relationship.Id == relationshipId, cancellationToken);
        if (relationship is null)
        {
            return TypedResults.NotFound();
        }

        if (!await authorization.IsPermittedAsync(user, AuthorisationMatrix.Meeting.Record, ProtectedRecord.Of(relationship))
            || await callers.ResolveAsync(user, cancellationToken) is not { EmployeeId: { } recordedBy })
        {
            return TypedResults.Forbid();
        }

        var terms = await AgreedTermsAsync(db, relationshipId, cancellationToken);
        var meeting = new Meeting
        {
            Kind = MeetingKind.RecurringSession,
            RelationshipId = relationshipId,
            ScheduledAt = request.ScheduledAt,
            ArrangedOutsideApplication = request.ArrangedOutsideApplication,
            FrequencyWeeks = terms.FrequencyWeeks,
            DurationMinutes = terms.DurationMinutes,
            CreatedByEmployeeId = recordedBy,
        };
        db.Meetings.Add(meeting);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.CreatedAtRoute(MeetingView.Of(meeting), GetMeetingName, new { meetingId = meeting.Id });
    }

    private static async Task<Results<Ok<MeetingView>, NotFound, ForbidHttpResult>> GetMeetingAsync(
        Guid meetingId, ClaimsPrincipal user, IAuthorizationService authorization, MentorDbContext db, CancellationToken cancellationToken)
    {
        var meeting = await db.Meetings.AsNoTracking().SingleOrDefaultAsync(meeting => meeting.Id == meetingId, cancellationToken);
        if (meeting is null)
        {
            return TypedResults.NotFound();
        }

        if (await OwnerOfAsync(db, meeting, cancellationToken) is not { } owner
            || !await authorization.IsPermittedAsync(user, AuthorisationMatrix.Meeting.Read, owner))
        {
            return TypedResults.Forbid();
        }

        return TypedResults.Ok(MeetingView.Of(meeting));
    }

    // The time and the arranged-outside flag are the only columns of a meeting that change (ADR-013).
    private static async Task<Results<Ok<MeetingView>, NotFound, ForbidHttpResult>> UpdateMeetingAsync(
        Guid meetingId, MeetingRequest request, ClaimsPrincipal user, IAuthorizationService authorization, MentorDbContext db, CancellationToken cancellationToken)
    {
        var meeting = await db.Meetings.SingleOrDefaultAsync(meeting => meeting.Id == meetingId, cancellationToken);
        if (meeting is null)
        {
            return TypedResults.NotFound();
        }

        if (await OwnerOfAsync(db, meeting, cancellationToken) is not { } owner
            || !await authorization.IsPermittedAsync(user, AuthorisationMatrix.Meeting.Record, owner))
        {
            return TypedResults.Forbid();
        }

        meeting.ScheduledAt = request.ScheduledAt;
        meeting.ArrangedOutsideApplication = request.ArrangedOutsideApplication;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(MeetingView.Of(meeting));
    }

    private static async Task<IReadOnlyList<MeetingView>> MeetingsAsync(IQueryable<Meeting> meetings, CancellationToken cancellationToken) =>
        [.. (await meetings.AsNoTracking()
                .OrderBy(meeting => meeting.ScheduledAt)
                .ThenBy(meeting => meeting.CreatedAt)
                .ThenBy(meeting => meeting.Id)
                .ToListAsync(cancellationToken))
            .Select(MeetingView.Of)];

    private static async Task<AgreementMeetingTerms> AgreedTermsAsync(MentorDbContext db, Guid relationshipId, CancellationToken cancellationToken)
    {
        var content = await db.FormedAgreements.AsNoTracking()
            .Where(agreement => agreement.RelationshipId == relationshipId)
            .Select(agreement => agreement.Content)
            .SingleOrDefaultAsync(cancellationToken);
        return content is null ? AgreementMeetingTerms.None : AgreementMeetingTerms.Of(content);
    }

    // A meeting is checked against the candidate or relationship it belongs to (ADR-003).
    private static async Task<ProtectedRecord?> OwnerOfAsync(MentorDbContext db, Meeting meeting, CancellationToken cancellationToken)
    {
        if (meeting.CandidateId is { } candidateId)
        {
            var candidate = await db.MentorSelections.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == candidateId, cancellationToken);
            return candidate is null ? null : ProtectedRecord.Of(candidate);
        }

        if (meeting.RelationshipId is { } relationshipId)
        {
            var relationship = await db.MentorshipRelationships.AsNoTracking().SingleOrDefaultAsync(relationship => relationship.Id == relationshipId, cancellationToken);
            return relationship is null ? null : ProtectedRecord.Of(relationship);
        }

        return null;
    }
}

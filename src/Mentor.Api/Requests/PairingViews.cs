using System.Text.Json.Serialization;
using Mentor.Core.Requests;

namespace Mentor.Api.Requests;

/// <summary>
/// A mentorship request as the API shows it (DR-003): the protégé who submitted it, what they stated, its submission
/// date and its disposition. It is a record of its own, distinct from every candidate and agreement made from it, and
/// keeps its identifier before and after formation (FR-017).
/// </summary>
public sealed record MentorshipRequestView(
    Guid Id,
    Guid ProtegeEmployeeId,
    string ProtegeDisplayName,
    string Motivation,
    string DevelopmentGoals,
    string NontechnicalExpectations,
    RequestDisposition Disposition,
    DateTimeOffset SubmittedAt,
    DateTimeOffset UpdatedAt)
{
    public static MentorshipRequestView Of(MentorshipRequest request, string protegeDisplayName) => new(
        request.Id,
        request.ProtegeEmployeeId,
        protegeDisplayName,
        request.Motivation,
        request.DevelopmentGoals,
        request.NontechnicalExpectations,
        request.Disposition,
        request.SubmittedAt,
        request.UpdatedAt);
}

/// <summary>
/// What a protégé submits (FR-014 to FR-016): the three statements of <see cref="RequestDetails"/> and nothing else. A
/// body naming the protégé, the disposition or any other member is refused as malformed: the protégé is the
/// authenticated caller, whoever a body names.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MentorshipRequestSubmission(string? Motivation, string? DevelopmentGoals, string? NontechnicalExpectations);

/// <summary>
/// A candidate as the API shows it (DR-004, ADR-015): its request, its two fixed participants, its status, the mentor's
/// response, each participant's own willingness, the withdrawal, the current revision and the version every change to it
/// names in If-Match (ADR-005). It carries none of the request's statements, which a mentor whose candidate has ended
/// may no longer read.
/// </summary>
public sealed record CandidateView(
    Guid Id,
    Guid RequestId,
    Guid ProtegeEmployeeId,
    string ProtegeDisplayName,
    Guid MentorEmployeeId,
    string MentorDisplayName,
    CandidateStatus Status,
    MentorResponse MentorResponse,
    DateTimeOffset InvitedAt,
    DateTimeOffset? MentorRespondedAt,
    DateTimeOffset? ProtegeWillingAt,
    DateTimeOffset? MentorWillingAt,
    Guid? WithdrawnByEmployeeId,
    DateTimeOffset? WithdrawnAt,
    Guid? CurrentRevisionId,
    long Version,
    DateTimeOffset UpdatedAt)
{
    public static CandidateView Of(MentorSelection candidate, string protegeDisplayName, string mentorDisplayName) => new(
        candidate.Id,
        candidate.RequestId,
        candidate.ProtegeEmployeeId,
        protegeDisplayName,
        candidate.MentorEmployeeId,
        mentorDisplayName,
        candidate.Status,
        candidate.MentorResponse,
        candidate.InvitedAt,
        candidate.MentorRespondedAt,
        candidate.ProtegeWillingAt,
        candidate.MentorWillingAt,
        candidate.WithdrawnByEmployeeId,
        candidate.WithdrawnAt,
        candidate.CurrentRevisionId,
        candidate.Version,
        candidate.UpdatedAt);
}

/// <summary>
/// The protégé's selection of a discoverable mentor for their request (FR-018): the mentor, by employee id, and nothing
/// else. The protégé and the request are not the body's to name.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CandidateSelection(Guid? MentorEmployeeId);

/// <summary>Why an operation on a request or a candidate yielded nothing.</summary>
public enum PairingRefusal
{
    None,

    NotFound,

    /// <summary>The matrix refuses the caller the action, on this record or at all.</summary>
    Forbidden,

    /// <summary>The caller has no employee record to attribute the request to; their employee profile creates it.</summary>
    NoEmployeeRecord,

    /// <summary>The change names no version in If-Match (428).</summary>
    VersionRequired,

    /// <summary>The version named is no longer current: another decision was taken first (409).</summary>
    Stale,

    /// <summary>The candidate's status does not admit the decision, or the caller has taken it already (409).</summary>
    NotInState,

    /// <summary>A relationship has been formed from the request; another pairing needs a new request (409).</summary>
    RequestClosed,

    /// <summary>The request has a candidate that has not ended (409).</summary>
    CandidateInProgress,

    /// <summary>The employee named is not an approved mentor (422).</summary>
    NotDiscoverable,

    /// <summary>The protégé named themselves as the mentor (422).</summary>
    OwnRequest,
}

/// <summary>The outcome of an operation: its result, or why there is none.</summary>
public sealed record PairingOutcome<T>(T? Value, PairingRefusal Refusal)
    where T : class
{
    public static PairingOutcome<T> Of(T value) => new(value, PairingRefusal.None);

    public static PairingOutcome<T> Refused(PairingRefusal refusal) => new(null, refusal);
}

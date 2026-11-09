using System.Text.Json.Serialization;
using Mentor.Core.Goals;

namespace Mentor.Api.Goals;

/// <summary>
/// The mentor's decision on a completion request (DR-011): the mentor, approved or rejected, and the server's time of the
/// decision. A decision carries no reason (DEC-008).
/// </summary>
public sealed record GoalDecisionView(Guid Id, GoalDecision Decision, Guid MentorEmployeeId, DateTimeOffset DecidedAt);

/// <summary>
/// A progress entry of the goal (DR-010): the protégé's dated progress update and whether it asks for completion. A
/// completion request carries its standing, pending until the mentor decides, and then the decision; ordinary progress
/// needs no decision and carries neither.
/// </summary>
public sealed record GoalProgressView(
    Guid Id,
    Guid ProtegeEmployeeId,
    string ProgressUpdate,
    bool CompletionRequested,
    DateTimeOffset SubmittedAt,
    CompletionStanding? Completion,
    GoalDecisionView? Decision);

/// <summary>
/// One development goal of a formed relationship (DR-009, COMP-GOALS-PROGRESS). It shows the agreed wording exactly as
/// agreed, which never changes; its completion status, which only the mentor's approval sets to Complete; the request
/// that awaits the mentor's decision, if any; and every progress entry, oldest first. Structured goals and progress
/// only: nothing said in a meeting is stored to be shown.
/// </summary>
public sealed record GoalView(
    Guid Id,
    Guid RelationshipId,
    Guid FormedAgreementId,
    int Position,
    string AgreedDescription,
    GoalCompletionStatus CompletionStatus,
    Guid? PendingCompletionRequestId,
    IReadOnlyList<GoalProgressView> Progress,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// The body of a progress entry and of a completion request: the protégé's progress update. Nothing else is accepted. A
/// body naming any other member, such as notes on a meeting, the goal's wording, a status, a date or a person, is refused.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GoalProgressRequest(string? ProgressUpdate);

/// <summary>
/// The body of the mentor's decision on a completion request: Approved or Rejected, and nothing else. There is no reason
/// field, and the mentor and the time are not supplied (DEC-008).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GoalDecisionRequest(GoalDecision? Decision);

/// <summary>Why a goal request was refused.</summary>
public enum GoalRefusal
{
    None,
    NotFound,
    Forbidden,
    GoalComplete,
    CompletionPending,
    NotACompletionRequest,
    CompletionDecided,
}

/// <summary>A goal request's answer: its value, or why it was refused.</summary>
public sealed record GoalResult<T>(T? Value, GoalRefusal Refusal)
    where T : class
{
    public static GoalResult<T> Of(T value) => new(value, GoalRefusal.None);

    public static GoalResult<T> Refused(GoalRefusal refusal) => new(null, refusal);
}

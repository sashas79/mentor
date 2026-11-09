using System.Text.Json.Serialization;
using Mentor.Api.Agreements;
using Mentor.Core.Agreements;
using Mentor.Core.Formation;
using Mentor.Core.Goals;
using Mentor.Core.Relationships;
using Mentor.Core.Requests;

namespace Mentor.Api.Formation;

/// <summary>
/// A formation decision exactly as recorded, with the standing derived for it now (ADR-004): a decision on a revision that
/// a replacement superseded, or on a withdrawn candidate, is history and is never effective.
/// </summary>
public sealed record FormationDecisionView(
    Guid Id,
    Guid RevisionId,
    int RevisionNumber,
    string ContentHash,
    FormationOutcome Outcome,
    Guid AdministratorEmployeeId,
    DateTimeOffset DecidedAt,
    Applicability Applicability);

/// <summary>One agreed goal of the formed relationship, worded exactly as agreed (DR-009).</summary>
public sealed record FormedGoalView(Guid Id, int Position, string AgreedDescription, GoalCompletionStatus CompletionStatus);

/// <summary>The administrative annex as it stood at formation, kept beside the agreement and never part of it (ADR-004).</summary>
public sealed record RetainedAnnexView(string? AdminReference, string? AdminRemarks)
{
    public string Notice => AdministrativeAnnexView.Label;
}

/// <summary>
/// The formed agreement (ADR-004): the approved revision's content and content hash, byte-identical to what both
/// participants accepted and verifiable by recomputing the hash, with the annex retained beside it. It is never changed.
/// </summary>
public sealed record FormedAgreementView(
    Guid Id,
    Guid RevisionId,
    string ContentHash,
    DateTimeOffset FormedAt,
    AgreementContent Content,
    RetainedAnnexView Annex);

/// <summary>
/// The relationship formed from the candidate (DR-005): its participants, its attributes as they now stand (In Progress,
/// blank and N at formation, ADR-006), the duration and dates of the accepted content, its formed agreement and goals.
/// </summary>
public sealed record FormedRelationshipView(
    Guid Id,
    Provenance Provenance,
    Guid ProtegeEmployeeId,
    Guid MentorEmployeeId,
    RelationshipState? State,
    RelationshipStatus? Status,
    ActiveFlag? IsActive,
    DurationCategory? DurationCategory,
    DateOnly? StartDate,
    DateOnly? EndDate,
    DateTimeOffset FormedAt,
    FormedAgreementView Agreement,
    IReadOnlyList<FormedGoalView> Goals);

/// <summary>
/// The formation outcome of a candidate (COMP-FORMATION, ADR-003: the parties read the outcome): every formation decision
/// with its applicability, the step of the draft and, once formed, the relationship. The candidate's version is its ETag.
/// </summary>
public sealed record FormationView(
    Guid CandidateId,
    CandidateStatus CandidateStatus,
    int? Step,
    Guid? CurrentRevisionId,
    IReadOnlyList<FormationDecisionView> Decisions,
    FormedRelationshipView? Relationship,
    long Version);

/// <summary>
/// The body of a formation decision: the outcome and the content hash of the revision the administrator reviewed, which
/// the route names. Nothing else: the relationship's attributes, IsActive among them, are set by formation, not supplied.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormationDecisionRequest(FormationOutcome? Outcome, string? ContentHash);

/// <summary>Why a formation request was refused.</summary>
public enum FormationRefusal
{
    None,
    NotFound,
    Forbidden,
    NoEmployeeRecord,
    VersionRequired,
    Stale,
    NotInState,
    RevisionSuperseded,
    ContentMismatch,
    NotAccepted,
}

/// <summary>A formation request's answer: its value, or why it was refused.</summary>
public sealed record FormationResult<T>(T? Value, FormationRefusal Refusal)
    where T : class
{
    public static FormationResult<T> Of(T value) => new(value, FormationRefusal.None);

    public static FormationResult<T> Refused(FormationRefusal refusal) => new(null, refusal);
}

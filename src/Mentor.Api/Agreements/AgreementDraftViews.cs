using System.Text.Json.Serialization;
using Mentor.Core.Agreements;
using Mentor.Core.Requests;

namespace Mentor.Api.Agreements;

/// <summary>A participant of the candidate, by the stable employee identity and the name it reads now.</summary>
public sealed record DraftParticipantView(Guid EmployeeId, string DisplayName);

/// <summary>
/// An acceptance exactly as recorded (DR-007): the participant who gave it, the revision and content hash it accepted and
/// the server time, with its standing derived for it now (ADR-004). Only an <see cref="Applicability.Applicable"/>
/// acceptance counts toward formation; a superseded one is history (AC-FR-028-02).
/// </summary>
public sealed record AcceptanceView(
    Guid Id,
    Guid RevisionId,
    int RevisionNumber,
    string ContentHash,
    Guid ParticipantEmployeeId,
    Participant Participant,
    DateTimeOffset AcceptedAt,
    Applicability Applicability);

/// <summary>A revision by its identity (DR-015): its number, the revision it replaced, its content hash, its author and time.</summary>
public sealed record RevisionSummaryView(
    Guid Id,
    int RevisionNumber,
    Guid? ParentRevisionId,
    string ContentHash,
    Guid CreatedByEmployeeId,
    DateTimeOffset CreatedAt,
    bool IsCurrent);

/// <summary>
/// One revision in full (AC-FR-022-02): the complete content each participant accepts, the fixed policy text included,
/// the content hash that identifies exactly that content, and the acceptances given to it. A revision never changes; a
/// replacement is another revision.
/// </summary>
public sealed record AgreementRevisionView(
    Guid Id,
    Guid CandidateId,
    int RevisionNumber,
    Guid? ParentRevisionId,
    string ContentHash,
    Guid CreatedByEmployeeId,
    DateTimeOffset CreatedAt,
    bool IsCurrent,
    AgreementContent Content,
    IReadOnlyList<AcceptanceView> Acceptances);

/// <summary>
/// A recorded no-reset change of the annex (AC-BR-020-01): its permission basis, the field it changed, the value before
/// and after, the administrator who made it and the server time.
/// </summary>
public sealed record PermittedChangeView(
    Guid Id,
    PermittedChangeBasis Basis,
    AnnexField Field,
    string? PriorValue,
    string? NewValue,
    Guid ActorEmployeeId,
    DateTimeOffset ChangedAt);

/// <summary>
/// The administrative annex of the draft (ADR-004): held on the candidate, outside the content, never hashed or
/// accepted, and labelled as such wherever it is shown, with the record of every change made to it.
/// </summary>
public sealed record AdministrativeAnnexView(string? AdminReference, string? AdminRemarks, IReadOnlyList<PermittedChangeView> Changes)
{
    public const string Label = "Administrative, not part of the accepted agreement";

    public string Notice => Label;
}

/// <summary>
/// The agreement draft of a candidate (COMP-AGREEMENT-DRAFTING): its workflow step, derived (ADR-004); the current
/// revision in full; every revision by its identity; every acceptance with its applicability; the administrative annex;
/// and the candidate version every draft command names in If-Match (ADR-005). The step is 1 to 3 while drafting, 4 once
/// formed, and none before drafting or after withdrawal.
/// </summary>
public sealed record AgreementDraftView(
    Guid CandidateId,
    CandidateStatus CandidateStatus,
    int? Step,
    DraftParticipantView Protege,
    DraftParticipantView Mentor,
    AgreementRevisionView? CurrentRevision,
    IReadOnlyList<RevisionSummaryView> Revisions,
    IReadOnlyList<AcceptanceView> Acceptances,
    AdministrativeAnnexView Annex,
    long Version);

/// <summary>
/// A participant's acceptance of a revision (ADR-004): the revision is named by the route, and the body names the content
/// hash of the content displayed to them, nothing else.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AcceptanceRequest(string? ContentHash);

/// <summary>An administrator's annex change (ADR-004): the permission basis and the new value, which a blank clears; nothing else.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AnnexChangeRequest(PermittedChangeBasis? Basis, string? Value);

/// <summary>Why an operation on a draft yielded nothing.</summary>
public enum DraftRefusal
{
    None,

    /// <summary>No such candidate, or no such revision of it.</summary>
    NotFound,

    /// <summary>The matrix refuses the caller the action, on this candidate or at all.</summary>
    Forbidden,

    /// <summary>The administrator has no employee record to attribute the change to; their employee profile creates it.</summary>
    NoEmployeeRecord,

    /// <summary>The command names no version in If-Match (428).</summary>
    VersionRequired,

    /// <summary>The version named is no longer current: another command was taken first (409).</summary>
    Stale,

    /// <summary>The candidate is not drafting, or the caller has accepted the revision already (409).</summary>
    NotInState,

    /// <summary>The revision named is no longer the candidate's current revision (409).</summary>
    RevisionSuperseded,

    /// <summary>The content hash named is not the hash of the revision named (422).</summary>
    ContentMismatch,

    /// <summary>The submitted content is exactly the current revision's: there is nothing to replace (409).</summary>
    ContentUnchanged,

    /// <summary>The annex field already holds the value (409).</summary>
    AnnexUnchanged,
}

/// <summary>The outcome of a draft operation: its result, or why there is none.</summary>
public sealed record DraftOutcome<T>(T? Value, DraftRefusal Refusal)
    where T : class
{
    public static DraftOutcome<T> Of(T value) => new(value, DraftRefusal.None);

    public static DraftOutcome<T> Refused(DraftRefusal refusal) => new(null, refusal);
}

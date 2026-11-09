using System.Text.Json.Serialization;
using Mentor.Core.Relationships;

namespace Mentor.Api.Relationships;

/// <summary>A participant of the relationship: the stable employee identity and the name it now reads.</summary>
public sealed record RelationshipParticipantView(Guid EmployeeId, string DisplayName);

/// <summary>Where a legacy relationship was entered from: the historical tracker's file and row (ADR-008).</summary>
public sealed record LegacySourceView(string File, int Row);

/// <summary>
/// A mentorship relationship (DR-005, COMP-RELATIONSHIP-ADMIN): its provenance, its participants, the three independent
/// attributes State, Status and IsActive, the duration and dates, and its version, which is its ETag (ADR-005). A digital
/// relationship always holds all three attributes; a legacy one holds null for a value its source left unresolved, which
/// is unknown, not a value (FR-044). A digital relationship names the candidate it was formed from, a legacy one its source.
/// </summary>
public sealed record RelationshipView(
    Guid Id,
    Provenance Provenance,
    Guid? CandidateId,
    RelationshipParticipantView Protege,
    RelationshipParticipantView Mentor,
    RelationshipState? State,
    RelationshipStatus? Status,
    ActiveFlag? IsActive,
    DurationCategory? DurationCategory,
    DateOnly? StartDate,
    DateOnly? EndDate,
    LegacySourceView? Source,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Version);

/// <summary>
/// The body of an attribute edit: any of state, status and isActive, each exactly one of its business values as the API
/// names them (InProgress, OnHold, Complete; Blank, NeedsReassigning; Y, N), or absent. Nothing else: the participants,
/// the provenance, the duration and the dates are not the edit's to change, and no edit forms a relationship.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RelationshipAttributesRequest(string? State, string? Status, string? IsActive);

/// <summary>Why a relationship request was refused.</summary>
public enum RelationshipRefusal
{
    None,
    NotFound,
    Forbidden,
    NoEmployeeRecord,
    VersionRequired,
    Stale,
    Unchanged,
}

/// <summary>A relationship request's answer: its value, or why it was refused.</summary>
public sealed record RelationshipResult<T>(T? Value, RelationshipRefusal Refusal)
    where T : class
{
    public static RelationshipResult<T> Of(T value) => new(value, RelationshipRefusal.None);

    public static RelationshipResult<T> Refused(RelationshipRefusal refusal) => new(null, refusal);
}

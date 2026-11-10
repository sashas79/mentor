using Mentor.Api.Goals;
using Mentor.Core.Legacy;
using Mentor.Core.Relationships;

namespace Mentor.Api.Register;

/// <summary>What a register row stands for: an entered relationship, or a historical source row held out of the relationship table.</summary>
public enum RegisterEntryKind
{
    /// <summary>A relationship, formed in the application or entered from the tracker.</summary>
    Relationship,

    /// <summary>
    /// A tracker row held as a review exception because a person could not be resolved or the row names no mentor
    /// (ADR-008). It has no relationship, so it shows its source text only.
    /// </summary>
    HeldSourceRow,
}

/// <summary>A participant as the register shows them: the stable employee identity, the name it now reads and the optional level or title.</summary>
public sealed record RegisterPersonView(Guid EmployeeId, string DisplayName, string? Level);

/// <summary>
/// Where a legacy row was entered from (ADR-008): the tracker's file and row and its cells exactly as entered. It is
/// evidence of the source, never a resolved value: a date or a category the source text did not state unambiguously
/// is unknown, and only this text remains of it.
/// </summary>
public sealed record RegisterSourceView(
    string File,
    int Row,
    string? MentorText,
    string? ProtegeText,
    string? ActiveText,
    string? DurationText,
    string? StartText,
    string? EndText);

/// <summary>
/// Why a legacy row still needs review (ADR-011). A held source row gives its reason, an unresolved person or no
/// mentor. An entered legacy relationship gives UnresolvedValue and the attributes that are still unknown. These are
/// derived from the relationship as it now reads, so a value an administrator has since set is no longer listed.
/// </summary>
public sealed record RegisterReviewView(LegacyReviewReason Reason, IReadOnlyList<RelationshipAttribute> UnresolvedAttributes);

/// <summary>
/// One row of the relationship register (COMP-REGISTER-REPORTING, CONTRACT-REGISTER-EXPORT, ADR-011). It is
/// reconstructed on demand from the authoritative store and never stored, and each value is the stored value or
/// nothing. A null is unknown, not a value: a BLANK Status is the business value Blank, while an unresolved legacy
/// value is null. Provenance, the source and the review exception tell a historical row and its unresolved mappings
/// apart from a digitally formed one. A held source row has no relationship, participants or attributes, only its
/// source and its reason. No goal, progress, agreement content or meeting content is part of a row.
/// </summary>
public sealed record RegisterEntryView(
    RegisterEntryKind Kind,
    Guid? RelationshipId,
    Guid? ReviewExceptionId,
    Provenance Provenance,
    RegisterPersonView? Mentor,
    RegisterPersonView? Protege,
    RelationshipState? State,
    RelationshipStatus? Status,
    ActiveFlag? IsActive,
    DateOnly? StartDate,
    DateOnly? EndDate,
    DurationCategory? DurationCategory,
    RegisterSourceView? Source,
    RegisterReviewView? ReviewException);

/// <summary>Goal completion across one relationship's goals; a request awaiting the mentor's decision counts as incomplete too.</summary>
public sealed record GoalSummaryView(int Total, int Complete, int Incomplete, int AwaitingDecision);

/// <summary>
/// One relationship in the administrator's structured view of goals and progress (FR-040, FR-066, BR-013): who the
/// relationship is between, its attributes, its goals' completion summary and each goal with its progress exactly as
/// the goal routes show it. A legacy relationship has no goals. Only structured goals and progress appear here.
/// Nothing said in a meeting is stored, so there is nothing of it to show.
/// </summary>
public sealed record RelationshipGoalsView(
    Guid RelationshipId,
    Provenance Provenance,
    RegisterPersonView Mentor,
    RegisterPersonView Protege,
    RelationshipState? State,
    RelationshipStatus? Status,
    ActiveFlag? IsActive,
    GoalSummaryView Summary,
    IReadOnlyList<GoalView> Goals);

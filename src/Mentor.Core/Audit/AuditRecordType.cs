namespace Mentor.Core.Audit;

/// <summary>
/// The kinds of record an entry names as the one its action affected (ADR-007), and by which administrators query the
/// trail. Stored as text under the database's enumeration naming (CANDIDATE, AGREEMENT_ACCEPTANCE, ...), with no check
/// constraint, so a later module adds a value without a migration; a legacy entry script names one of these values.
/// </summary>
public enum AuditRecordType
{
    MentorProfile,
    MentorshipRequest,
    Candidate,
    AgreementRevision,
    AgreementAcceptance,
    PermittedChange,
    FormationDecision,
    Relationship,
    Goal,
    GoalProgress,
    GoalApproval,
    Meeting,

    /// <summary>The register as a whole: an export names no single record.</summary>
    Register,
    LegacyReviewException,
}

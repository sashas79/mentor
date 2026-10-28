namespace Mentor.Tests.Integration.Persistence;

/// <summary>The two table classes of ADR-013.</summary>
internal static class Tables
{
    public static readonly string[] Evidence =
    [
        "agreement_revision", "agreement_acceptance", "permitted_change", "formation_decision", "formed_agreement",
        "goal_progress", "goal_approval", "audit_entry", "legacy_entry_authorisation", "legacy_review_exception",
    ];

    public static readonly string[] Operational =
    [
        "employee", "mentor_profile", "mentorship_request", "mentor_selection", "mentorship_relationship",
        "development_goal", "meeting", "notification_event",
    ];

    public static TheoryData<string> EvidenceTables => new(Evidence);

    public static TheoryData<string> OperationalTables => new(Operational);
}

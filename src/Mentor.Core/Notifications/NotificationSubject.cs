namespace Mentor.Core.Notifications;

/// <summary>
/// The kinds of record a notification event is about (CONTRACT-NOTIFICATION-EVENTS), stored in subject_type beside the
/// record's identifier so that a recipient can open the record the event concerns.
/// </summary>
public static class NotificationSubject
{
    /// <summary>A mentor application and profile (DR-002).</summary>
    public const string MentorProfile = "MENTOR_PROFILE";

    /// <summary>A candidate: one request paired with one mentor (DR-004, ADR-015). Draft and formation events name it too.</summary>
    public const string Candidate = "CANDIDATE";

    /// <summary>An agreed development goal of a formed relationship (DR-009).</summary>
    public const string DevelopmentGoal = "DEVELOPMENT_GOAL";
}

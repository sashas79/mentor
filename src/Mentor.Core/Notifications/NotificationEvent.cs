namespace Mentor.Core.Notifications;

/// <summary>How notification events reach their recipient (ADR-009). Simulated is the labelled in-app inbox of the prototype.</summary>
public enum NotificationDelivery
{
    None,
    Simulated,
}

/// <summary>The material workflow actions that raise an event (ADR-009; withdrawal by ADR-015).</summary>
public enum NotificationEventType
{
    MentorApplicationDecided,
    InvitationSent,
    MentorResponded,
    WillingnessRecorded,
    CandidateWithdrawn,
    RevisionCreated,
    AcceptanceRecorded,
    FormationDecided,
    GoalCompletionRequested,
    GoalCompletionDecided,
}

public enum RecipientRole
{
    Protege,
    Mentor,
    Applicant,
    TalentManagement,
}

/// <summary>
/// A recipient's pending-action record for a material workflow action (CONTRACT-NOTIFICATION-EVENTS). It is not
/// evidence; the audit trail is. Only the read marker ever changes.
/// </summary>
public sealed class NotificationEvent
{
    public Guid Id { get; init; }

    public NotificationEventType EventType { get; init; }

    /// <summary>The kind of record the event is about; one of <see cref="NotificationSubject"/>.</summary>
    public required string SubjectType { get; init; }

    public Guid SubjectId { get; init; }

    public Guid RecipientEmployeeId { get; init; }

    public RecipientRole RecipientRole { get; init; }

    public RequiredAction RequiredAction { get; init; }

    public NotificationDelivery Delivery { get; init; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Equal to <see cref="CreatedAt"/> for a simulated delivery, otherwise none; the database sets it.</summary>
    public DateTimeOffset? DeliveredAt { get; private set; }

    public DateTimeOffset? ReadAt { get; set; }
}

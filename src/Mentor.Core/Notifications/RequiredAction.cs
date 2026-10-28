namespace Mentor.Core.Notifications;

/// <summary>
/// The next action a notification event asks of its recipient (CONTRACT-NOTIFICATION-EVENTS, FR-057): the recipient's
/// pending action, where one applies. <see cref="None"/> marks an event that only informs. The database limits the
/// column to these values.
/// </summary>
public enum RequiredAction
{
    /// <summary>Nothing is required; the event informs the recipient of an outcome.</summary>
    None,

    /// <summary>The invited mentor accepts or declines (ADR-015).</summary>
    RespondToInvitation,

    /// <summary>The protégé's pairing ended without a relationship; proceeding is a new selection (ADR-015).</summary>
    SelectAnotherMentor,

    /// <summary>The participant records their own affirmative willingness to proceed (ADR-015).</summary>
    RecordWillingness,

    /// <summary>Both participants are willing: the candidate is drafting and a first revision can be prepared (ADR-004).</summary>
    PrepareAgreementDraft,

    /// <summary>The participant reviews the current revision and records their own acceptance of it (ADR-004).</summary>
    AcceptCurrentRevision,

    /// <summary>Formation was rejected; the participants are free to revise the draft or withdraw (ADR-005).</summary>
    ReviseDraft,

    /// <summary>The relationship is formed; the parties plan their first recurring session (ADR-010).</summary>
    ArrangeFirstSession,

    /// <summary>The mentor approves or rejects the protégé's completion request (FR-037).</summary>
    DecideGoalCompletion,

    /// <summary>Completion was rejected; the goal stays incomplete and the protégé continues to submit progress (FR-037).</summary>
    SubmitProgress,
}

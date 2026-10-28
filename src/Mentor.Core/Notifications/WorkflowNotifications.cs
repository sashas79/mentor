using Mentor.Core.Formation;
using Mentor.Core.Goals;
using Mentor.Core.MentorRegistry;
using Mentor.Core.Relationships;
using Mentor.Core.Requests;

namespace Mentor.Core.Notifications;

/// <summary>
/// One notification a material workflow action raises (CONTRACT-NOTIFICATION-EVENTS): the action, the record it is
/// about, the intended recipient with the role they hold towards that record, and the next action required of them.
/// </summary>
public sealed record WorkflowNotification(
    NotificationEventType EventType,
    string SubjectType,
    Guid SubjectId,
    Guid RecipientEmployeeId,
    RecipientRole RecipientRole,
    RequiredAction RequiredAction);

/// <summary>
/// The catalogue of material workflow actions and the notifications each raises (ADR-009, FR-057): who is told, in
/// which role, and what they must do next. A module calls the method for its action with the record as the command
/// leaves it, and raises the result in the same transaction as the action. The recipient is always the other party to
/// the action: a Talent Management administrator's pending work is a query over the records, not an inbox entry,
/// because administrators are known by their app role rather than by a record the application could address.
/// </summary>
public static class WorkflowNotifications
{
    /// <summary>A Talent Management administrator approved or declined the application: the applicant is informed.</summary>
    public static WorkflowNotification MentorApplicationDecided(MentorProfile profile)
    {
        if (profile.ApprovalStatus == MentorApprovalStatus.Pending)
        {
            throw new InvalidOperationException("The application has not been decided.");
        }

        return new(NotificationEventType.MentorApplicationDecided, NotificationSubject.MentorProfile, profile.Id,
            profile.EmployeeId, RecipientRole.Applicant, RequiredAction.None);
    }

    /// <summary>The protégé selected the mentor: the mentor responds to the invitation.</summary>
    public static WorkflowNotification InvitationSent(MentorSelection candidate) =>
        new(NotificationEventType.InvitationSent, NotificationSubject.Candidate, candidate.Id,
            candidate.MentorEmployeeId, RecipientRole.Mentor, RequiredAction.RespondToInvitation);

    /// <summary>
    /// The mentor accepted or declined: the protégé records their willingness to proceed, or selects another mentor.
    /// </summary>
    public static WorkflowNotification MentorResponded(MentorSelection candidate)
    {
        var action = candidate.MentorResponse switch
        {
            MentorResponse.Accepted => RequiredAction.RecordWillingness,
            MentorResponse.Declined => RequiredAction.SelectAnotherMentor,
            _ => throw new InvalidOperationException("The mentor has not responded."),
        };
        return new(NotificationEventType.MentorResponded, NotificationSubject.Candidate, candidate.Id,
            candidate.ProtegeEmployeeId, RecipientRole.Protege, action);
    }

    /// <summary>
    /// A participant recorded their willingness: the other participant records theirs, or, when that made the candidate
    /// DRAFTING, prepares the agreement draft (a mutual-agreement action, FR-057).
    /// </summary>
    public static WorkflowNotification WillingnessRecorded(MentorSelection candidate, Guid byEmployeeId)
    {
        var (recipient, role) = OtherParticipant(candidate, byEmployeeId);
        var action = candidate.Status == CandidateStatus.Drafting ? RequiredAction.PrepareAgreementDraft : RequiredAction.RecordWillingness;
        return new(NotificationEventType.WillingnessRecorded, NotificationSubject.Candidate, candidate.Id, recipient, role, action);
    }

    /// <summary>A participant withdrew: the other participant is informed; a protégé may select another mentor.</summary>
    public static WorkflowNotification CandidateWithdrawn(MentorSelection candidate)
    {
        if (candidate.WithdrawnByEmployeeId is not { } withdrawnBy)
        {
            throw new InvalidOperationException("The candidate has not been withdrawn.");
        }

        var (recipient, role) = OtherParticipant(candidate, withdrawnBy);
        var action = role == RecipientRole.Protege ? RequiredAction.SelectAnotherMentor : RequiredAction.None;
        return new(NotificationEventType.CandidateWithdrawn, NotificationSubject.Candidate, candidate.Id, recipient, role, action);
    }

    /// <summary>
    /// A participant created a replacement revision, which returned the draft to step 1: the other participant reviews
    /// and accepts the current revision.
    /// </summary>
    public static WorkflowNotification RevisionCreated(MentorSelection candidate, Guid byEmployeeId)
    {
        var (recipient, role) = OtherParticipant(candidate, byEmployeeId);
        return new(NotificationEventType.RevisionCreated, NotificationSubject.Candidate, candidate.Id, recipient, role, RequiredAction.AcceptCurrentRevision);
    }

    /// <summary>
    /// A participant accepted the current revision: the other participant accepts it too, or, when both now have, is
    /// informed that the draft awaits the Talent Management formation decision.
    /// </summary>
    public static WorkflowNotification AcceptanceRecorded(MentorSelection candidate, Guid byEmployeeId, bool otherParticipantHasAccepted)
    {
        var (recipient, role) = OtherParticipant(candidate, byEmployeeId);
        var action = otherParticipantHasAccepted ? RequiredAction.None : RequiredAction.AcceptCurrentRevision;
        return new(NotificationEventType.AcceptanceRecorded, NotificationSubject.Candidate, candidate.Id, recipient, role, action);
    }

    /// <summary>
    /// A Talent Management administrator approved or rejected formation: both participants are told; after an approval
    /// they arrange their first session, after a rejection they may revise the draft.
    /// </summary>
    public static IReadOnlyList<WorkflowNotification> FormationDecided(MentorSelection candidate, FormationOutcome outcome)
    {
        var action = outcome == FormationOutcome.Approved ? RequiredAction.ArrangeFirstSession : RequiredAction.ReviseDraft;
        return
        [
            new(NotificationEventType.FormationDecided, NotificationSubject.Candidate, candidate.Id, candidate.ProtegeEmployeeId, RecipientRole.Protege, action),
            new(NotificationEventType.FormationDecided, NotificationSubject.Candidate, candidate.Id, candidate.MentorEmployeeId, RecipientRole.Mentor, action),
        ];
    }

    /// <summary>The protégé asked for a goal to be completed: the mentor decides.</summary>
    public static WorkflowNotification GoalCompletionRequested(DevelopmentGoal goal, MentorshipRelationship relationship)
    {
        RequireGoalOf(goal, relationship);
        return new(NotificationEventType.GoalCompletionRequested, NotificationSubject.DevelopmentGoal, goal.Id,
            relationship.MentorEmployeeId, RecipientRole.Mentor, RequiredAction.DecideGoalCompletion);
    }

    /// <summary>
    /// The mentor approved or rejected completion: the protégé is told; after a rejection the goal stays incomplete
    /// and progress continues.
    /// </summary>
    public static WorkflowNotification GoalCompletionDecided(DevelopmentGoal goal, MentorshipRelationship relationship, GoalDecision decision)
    {
        RequireGoalOf(goal, relationship);
        var action = decision == GoalDecision.Approved ? RequiredAction.None : RequiredAction.SubmitProgress;
        return new(NotificationEventType.GoalCompletionDecided, NotificationSubject.DevelopmentGoal, goal.Id,
            relationship.ProtegeEmployeeId, RecipientRole.Protege, action);
    }

    private static (Guid Recipient, RecipientRole Role) OtherParticipant(MentorSelection candidate, Guid actor)
    {
        if (actor == candidate.ProtegeEmployeeId)
        {
            return (candidate.MentorEmployeeId, RecipientRole.Mentor);
        }

        if (actor == candidate.MentorEmployeeId)
        {
            return (candidate.ProtegeEmployeeId, RecipientRole.Protege);
        }

        throw new ArgumentException("The actor is not a participant of the candidate.", nameof(actor));
    }

    private static void RequireGoalOf(DevelopmentGoal goal, MentorshipRelationship relationship)
    {
        if (goal.RelationshipId != relationship.Id)
        {
            throw new ArgumentException("The goal does not belong to the relationship.", nameof(goal));
        }
    }
}

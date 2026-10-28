using Mentor.Core.Formation;
using Mentor.Core.Goals;
using Mentor.Core.MentorRegistry;
using Mentor.Core.Notifications;
using Mentor.Core.Relationships;
using Mentor.Core.Requests;
using static Mentor.Core.Notifications.WorkflowNotifications;

namespace Mentor.Tests.Unit.Notifications;

// AC-FR-057-01: for every material action the prototype identifies the action, the intended recipient and role, and
// the required next action where one applies (ADR-009, ADR-015).
public sealed class WorkflowNotificationsTests
{
    private static readonly Guid Protege = Guid.NewGuid();
    private static readonly Guid Mentor = Guid.NewGuid();
    private static readonly Guid Applicant = Guid.NewGuid();
    private static readonly Guid Outsider = Guid.NewGuid();

    [Fact]
    public void Each_material_action_names_the_action_the_intended_recipient_and_role_and_the_next_action()
    {
        var accepted = Candidate(CandidateStatus.Accepted);
        var drafting = Candidate(CandidateStatus.Drafting);
        var declined = Candidate(CandidateStatus.Declined, MentorResponse.Declined);
        var withdrawnByMentor = Candidate(CandidateStatus.Withdrawn, withdrawnBy: Mentor);
        var withdrawnByProtege = Candidate(CandidateStatus.Withdrawn, withdrawnBy: Protege);
        var profile = new MentorProfile { Id = Guid.NewGuid(), EmployeeId = Applicant, Biography = "Ten years of delivery", ApprovalStatus = MentorApprovalStatus.Declined };
        var relationship = new MentorshipRelationship { Id = Guid.NewGuid(), ProtegeEmployeeId = Protege, MentorEmployeeId = Mentor };
        var goal = new DevelopmentGoal { Id = Guid.NewGuid(), RelationshipId = relationship.Id, AgreedDescription = "Lead a design review" };

        var cases = new (string Action, WorkflowNotification Actual, WorkflowNotification Expected)[]
        {
            ("mentor application decided", MentorApplicationDecided(profile),
                new(NotificationEventType.MentorApplicationDecided, NotificationSubject.MentorProfile, profile.Id, Applicant, RecipientRole.Applicant, RequiredAction.None)),
            ("invitation sent", InvitationSent(accepted),
                Of(NotificationEventType.InvitationSent, accepted, Mentor, RecipientRole.Mentor, RequiredAction.RespondToInvitation)),
            ("mentor accepted", MentorResponded(accepted),
                Of(NotificationEventType.MentorResponded, accepted, Protege, RecipientRole.Protege, RequiredAction.RecordWillingness)),
            ("mentor declined", MentorResponded(declined),
                Of(NotificationEventType.MentorResponded, declined, Protege, RecipientRole.Protege, RequiredAction.SelectAnotherMentor)),
            ("first willingness, by the protégé", WillingnessRecorded(accepted, Protege),
                Of(NotificationEventType.WillingnessRecorded, accepted, Mentor, RecipientRole.Mentor, RequiredAction.RecordWillingness)),
            ("first willingness, by the mentor", WillingnessRecorded(accepted, Mentor),
                Of(NotificationEventType.WillingnessRecorded, accepted, Protege, RecipientRole.Protege, RequiredAction.RecordWillingness)),
            ("second willingness, which made the candidate drafting", WillingnessRecorded(drafting, Mentor),
                Of(NotificationEventType.WillingnessRecorded, drafting, Protege, RecipientRole.Protege, RequiredAction.PrepareAgreementDraft)),
            ("withdrawn by the mentor", CandidateWithdrawn(withdrawnByMentor),
                Of(NotificationEventType.CandidateWithdrawn, withdrawnByMentor, Protege, RecipientRole.Protege, RequiredAction.SelectAnotherMentor)),
            ("withdrawn by the protégé", CandidateWithdrawn(withdrawnByProtege),
                Of(NotificationEventType.CandidateWithdrawn, withdrawnByProtege, Mentor, RecipientRole.Mentor, RequiredAction.None)),
            ("replacement revision by the protégé", RevisionCreated(drafting, Protege),
                Of(NotificationEventType.RevisionCreated, drafting, Mentor, RecipientRole.Mentor, RequiredAction.AcceptCurrentRevision)),
            ("first acceptance, by the mentor", AcceptanceRecorded(drafting, Mentor, otherParticipantHasAccepted: false),
                Of(NotificationEventType.AcceptanceRecorded, drafting, Protege, RecipientRole.Protege, RequiredAction.AcceptCurrentRevision)),
            ("second acceptance, by the protégé", AcceptanceRecorded(drafting, Protege, otherParticipantHasAccepted: true),
                Of(NotificationEventType.AcceptanceRecorded, drafting, Mentor, RecipientRole.Mentor, RequiredAction.None)),
            ("goal completion requested", GoalCompletionRequested(goal, relationship),
                new(NotificationEventType.GoalCompletionRequested, NotificationSubject.DevelopmentGoal, goal.Id, Mentor, RecipientRole.Mentor, RequiredAction.DecideGoalCompletion)),
            ("goal completion approved", GoalCompletionDecided(goal, relationship, GoalDecision.Approved),
                new(NotificationEventType.GoalCompletionDecided, NotificationSubject.DevelopmentGoal, goal.Id, Protege, RecipientRole.Protege, RequiredAction.None)),
            ("goal completion rejected", GoalCompletionDecided(goal, relationship, GoalDecision.Rejected),
                new(NotificationEventType.GoalCompletionDecided, NotificationSubject.DevelopmentGoal, goal.Id, Protege, RecipientRole.Protege, RequiredAction.SubmitProgress)),
        };

        Assert.All(cases, c => Assert.True(c.Expected == c.Actual, $"{c.Action}: {c.Actual}"));
    }

    // Administrator approval and rejection reach both participants.
    [Theory]
    [InlineData(FormationOutcome.Approved, RequiredAction.ArrangeFirstSession)]
    [InlineData(FormationOutcome.Rejected, RequiredAction.ReviseDraft)]
    public void A_formation_decision_is_told_to_both_participants(FormationOutcome outcome, RequiredAction next)
    {
        var drafting = Candidate(CandidateStatus.Drafting);

        var notifications = FormationDecided(drafting, outcome);

        Assert.Equal(
            [(Protege, RecipientRole.Protege), (Mentor, RecipientRole.Mentor)],
            notifications.Select(notification => (notification.RecipientEmployeeId, notification.RecipientRole)));
        Assert.All(notifications, notification =>
        {
            Assert.Equal(NotificationEventType.FormationDecided, notification.EventType);
            Assert.Equal(NotificationSubject.Candidate, notification.SubjectType);
            Assert.Equal(drafting.Id, notification.SubjectId);
            Assert.Equal(next, notification.RequiredAction);
        });
    }

    // ADR-009's list, with ADR-015's withdrawal: every event type has a material action that raises it.
    [Fact]
    public void Every_event_type_has_a_material_action()
    {
        var accepted = Candidate(CandidateStatus.Accepted);
        var withdrawn = Candidate(CandidateStatus.Withdrawn, withdrawnBy: Protege);
        var profile = new MentorProfile { EmployeeId = Applicant, Biography = "Biography", ApprovalStatus = MentorApprovalStatus.Approved };
        var relationship = new MentorshipRelationship { Id = Guid.NewGuid(), ProtegeEmployeeId = Protege, MentorEmployeeId = Mentor };
        var goal = new DevelopmentGoal { RelationshipId = relationship.Id, AgreedDescription = "Goal" };

        var raised = new[]
        {
            MentorApplicationDecided(profile), InvitationSent(accepted), MentorResponded(accepted), WillingnessRecorded(accepted, Protege),
            CandidateWithdrawn(withdrawn), RevisionCreated(accepted, Protege), AcceptanceRecorded(accepted, Protege, false),
            GoalCompletionRequested(goal, relationship), GoalCompletionDecided(goal, relationship, GoalDecision.Approved),
        }.Concat(FormationDecided(accepted, FormationOutcome.Approved));

        Assert.Equal(Enum.GetValues<NotificationEventType>().Order(), raised.Select(notification => notification.EventType).Distinct().Order());
    }

    // The recipient is always the other party to the action, never a Talent Management administrator.
    [Fact]
    public void No_event_is_addressed_to_talent_management()
    {
        var accepted = Candidate(CandidateStatus.Accepted);
        var relationship = new MentorshipRelationship { Id = Guid.NewGuid(), ProtegeEmployeeId = Protege, MentorEmployeeId = Mentor };
        var goal = new DevelopmentGoal { RelationshipId = relationship.Id, AgreedDescription = "Goal" };

        var raised = new[]
        {
            InvitationSent(accepted), MentorResponded(accepted), WillingnessRecorded(accepted, Mentor), RevisionCreated(accepted, Mentor),
            AcceptanceRecorded(accepted, Mentor, true), GoalCompletionRequested(goal, relationship), GoalCompletionDecided(goal, relationship, GoalDecision.Rejected),
        }.Concat(FormationDecided(accepted, FormationOutcome.Rejected));

        Assert.All(raised, notification =>
        {
            Assert.NotEqual(RecipientRole.TalentManagement, notification.RecipientRole);
            Assert.Contains(notification.RecipientEmployeeId, new[] { Protege, Mentor });
        });
    }

    [Fact]
    public void An_action_by_someone_who_is_not_a_participant_raises_nothing()
    {
        var drafting = Candidate(CandidateStatus.Drafting);

        Assert.Throws<ArgumentException>(() => WillingnessRecorded(drafting, Outsider));
        Assert.Throws<ArgumentException>(() => RevisionCreated(drafting, Outsider));
        Assert.Throws<ArgumentException>(() => AcceptanceRecorded(drafting, Outsider, false));
        Assert.Throws<ArgumentException>(() => CandidateWithdrawn(Candidate(CandidateStatus.Withdrawn, withdrawnBy: Outsider)));
    }

    [Fact]
    public void A_record_without_the_decision_raises_nothing()
    {
        var relationship = new MentorshipRelationship { Id = Guid.NewGuid(), ProtegeEmployeeId = Protege, MentorEmployeeId = Mentor };
        var foreignGoal = new DevelopmentGoal { RelationshipId = Guid.NewGuid(), AgreedDescription = "Goal" };

        Assert.Throws<InvalidOperationException>(() => MentorResponded(Candidate(CandidateStatus.Invited, MentorResponse.Pending)));
        Assert.Throws<InvalidOperationException>(() => MentorApplicationDecided(new MentorProfile { EmployeeId = Applicant, Biography = "B", ApprovalStatus = MentorApprovalStatus.Pending }));
        Assert.Throws<InvalidOperationException>(() => CandidateWithdrawn(Candidate(CandidateStatus.Accepted)));
        Assert.Throws<ArgumentException>(() => GoalCompletionRequested(foreignGoal, relationship));
        Assert.Throws<ArgumentException>(() => GoalCompletionDecided(foreignGoal, relationship, GoalDecision.Approved));
    }

    private static MentorSelection Candidate(CandidateStatus status, MentorResponse response = MentorResponse.Accepted, Guid? withdrawnBy = null) => new()
    {
        Id = Guid.NewGuid(),
        RequestId = Guid.NewGuid(),
        ProtegeEmployeeId = Protege,
        MentorEmployeeId = Mentor,
        Status = status,
        MentorResponse = response,
        WithdrawnByEmployeeId = withdrawnBy,
    };

    private static WorkflowNotification Of(NotificationEventType type, MentorSelection candidate, Guid recipient, RecipientRole role, RequiredAction action) =>
        new(type, NotificationSubject.Candidate, candidate.Id, recipient, role, action);
}

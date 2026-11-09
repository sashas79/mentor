using Mentor.Core.Goals;

namespace Mentor.Tests.Unit.Goals;

// BR-005, DR-009, DR-010, DR-011: progress needs no approval; a completion request is pending until the relationship's
// mentor decides it, once; only an approval completes the goal, and a rejection leaves it as it was. A goal has at most
// one request awaiting the mentor, and a complete goal takes none. Each answer is derived, never stored.
public sealed class GoalCompletionTests
{
    private static readonly Guid Relationship = Guid.NewGuid();
    private static readonly Guid Protege = Guid.NewGuid();
    private static readonly Guid Mentor = Guid.NewGuid();

    // AC-DR-010-01, AC-E2E-04: ordinary progress needs no approval, so it has no standing and cannot be decided.
    [Fact]
    public void Ordinary_progress_has_no_completion_standing_and_takes_no_decision()
    {
        var progress = Entry(Goal(), completionRequested: false);

        Assert.Null(GoalCompletion.StandingOf(progress, decision: null));
        Assert.Equal(CompletionDecisionBlock.NotACompletionRequest, GoalCompletion.CheckDecision(progress, decision: null));
    }

    // AC-FR-036-01, AC-FR-037-01, AC-DR-011-01: a request is pending until decided, and then it is what the mentor decided.
    [Fact]
    public void A_completion_request_is_pending_until_the_mentor_decides_it_once()
    {
        var request = Entry(Goal(), completionRequested: true);

        Assert.Equal(CompletionStanding.Pending, GoalCompletion.StandingOf(request, decision: null));
        Assert.Equal(CompletionDecisionBlock.None, GoalCompletion.CheckDecision(request, decision: null));
        Assert.Equal(CompletionStanding.Approved, GoalCompletion.StandingOf(request, Decision(request, GoalDecision.Approved)));
        Assert.Equal(CompletionStanding.Rejected, GoalCompletion.StandingOf(request, Decision(request, GoalDecision.Rejected)));
        Assert.Equal(CompletionDecisionBlock.AlreadyDecided, GoalCompletion.CheckDecision(request, Decision(request, GoalDecision.Approved)));
        Assert.Equal(CompletionDecisionBlock.AlreadyDecided, GoalCompletion.CheckDecision(request, Decision(request, GoalDecision.Rejected)));
    }

    // A decision on one entry never stands for another's.
    [Fact]
    public void A_decision_on_another_entry_is_never_taken_for_this_ones()
    {
        var goal = Goal();
        var request = Entry(goal, completionRequested: true);
        var other = Entry(goal, completionRequested: true);

        Assert.Throws<ArgumentException>(() => GoalCompletion.StandingOf(request, Decision(other, GoalDecision.Approved)));
        Assert.Throws<ArgumentException>(() => GoalCompletion.CheckDecision(request, Decision(other, GoalDecision.Rejected)));
    }

    // AC-FR-037-02, AC-DR-011-01, AC-E2E-04: only an approval completes the goal; a rejection leaves it incomplete.
    [Fact]
    public void Only_an_approval_completes_the_goal_and_a_rejection_leaves_it_as_it_was()
    {
        Assert.Equal(GoalCompletionStatus.Complete, GoalCompletion.StatusAfter(Goal(), GoalDecision.Approved));
        Assert.Equal(GoalCompletionStatus.Incomplete, GoalCompletion.StatusAfter(Goal(), GoalDecision.Rejected));
    }

    // A value that is neither Approved nor Rejected is no decision, and is never read as either.
    [Fact]
    public void A_value_that_is_no_decision_is_refused_rather_than_read_as_one()
    {
        var request = Entry(Goal(), completionRequested: true);

        Assert.Throws<ArgumentOutOfRangeException>(() => GoalCompletion.StandingOf(request, Decision(request, (GoalDecision)7)));
    }

    // AC-FR-036-01: completion is asked for while the goal is incomplete and none of its requests awaits the mentor; a
    // decided request, ordinary progress and another goal's request do not stand in the way.
    [Fact]
    public void Completion_is_requested_while_the_goal_is_incomplete_and_none_of_its_requests_is_pending()
    {
        var goal = Goal();
        var progress = Entry(goal, completionRequested: false);
        var rejected = Entry(goal, completionRequested: true);
        var pending = Entry(goal, completionRequested: true);
        var elsewhere = Entry(Goal(), completionRequested: true);
        var rejection = Decision(rejected, GoalDecision.Rejected);

        Assert.Equal(CompletionRequestBlock.None, GoalCompletion.CheckRequest(goal, [], []));
        Assert.Equal(CompletionRequestBlock.None, GoalCompletion.CheckRequest(goal, [progress, rejected, elsewhere], [rejection]));
        Assert.Null(GoalCompletion.PendingRequestOf(goal, [progress, rejected, elsewhere], [rejection]));

        Assert.Equal(CompletionRequestBlock.RequestPending, GoalCompletion.CheckRequest(goal, [progress, rejected, pending, elsewhere], [rejection]));
        Assert.Same(pending, GoalCompletion.PendingRequestOf(goal, [progress, rejected, pending, elsewhere], [rejection]));
        Assert.Equal(CompletionRequestBlock.RequestPending, GoalCompletion.CheckRequest(goal, [rejected], []));
    }

    // AC-DR-009-01: completion is formal and given once; a complete goal takes no further request.
    [Fact]
    public void A_complete_goal_takes_no_completion_request()
    {
        var goal = Goal(GoalCompletionStatus.Complete);
        var approved = Entry(goal, completionRequested: true);

        Assert.Equal(CompletionRequestBlock.GoalComplete, GoalCompletion.CheckRequest(goal, [], []));
        Assert.Equal(CompletionRequestBlock.GoalComplete, GoalCompletion.CheckRequest(goal, [approved], [Decision(approved, GoalDecision.Approved)]));
    }

    private static DevelopmentGoal Goal(GoalCompletionStatus status = GoalCompletionStatus.Incomplete) => new()
    {
        Id = Guid.NewGuid(),
        RelationshipId = Relationship,
        FormedAgreementId = Guid.NewGuid(),
        Position = 1,
        AgreedDescription = "Lead a design review",
        CompletionStatus = status,
    };

    private static GoalProgress Entry(DevelopmentGoal goal, bool completionRequested) => new()
    {
        Id = Guid.NewGuid(),
        GoalId = goal.Id,
        RelationshipId = goal.RelationshipId,
        ProtegeEmployeeId = Protege,
        ProgressUpdate = "Drafted the review checklist",
        CompletionRequested = completionRequested,
    };

    private static GoalApproval Decision(GoalProgress request, GoalDecision decision) => new()
    {
        Id = Guid.NewGuid(),
        ProgressId = request.Id,
        RelationshipId = request.RelationshipId,
        MentorEmployeeId = Mentor,
        Decision = decision,
    };
}

using Mentor.Core.Agreements;
using Mentor.Core.Requests;

namespace Mentor.Tests.Unit.Agreements;

// ADR-004, WF-03: the step of a draft is derived from the candidate head and the acceptances, never stored, and only the
// identified participants' acceptances of the current revision count (BR-019, AC-FR-027-03).
public sealed class AgreementWorkflowTests
{
    private static readonly Guid Protege = Guid.NewGuid();
    private static readonly Guid Mentor = Guid.NewGuid();
    private static readonly Guid Current = Guid.NewGuid();
    private static readonly Guid Superseded = Guid.NewGuid();

    [Theory]
    [InlineData(CandidateStatus.Invited, false)]
    [InlineData(CandidateStatus.Declined, false)]
    [InlineData(CandidateStatus.Accepted, false)]
    [InlineData(CandidateStatus.Drafting, true)]
    [InlineData(CandidateStatus.Formed, false)]
    [InlineData(CandidateStatus.Withdrawn, false)]
    public void Only_a_drafting_candidate_admits_a_draft_operation(CandidateStatus status, bool admitted)
    {
        Assert.Equal(admitted, AgreementWorkflow.AdmitsDraftChange(Candidate(status, Current)));
    }

    [Fact]
    public void A_drafting_candidate_without_a_revision_is_at_draft_preparation()
    {
        Assert.Equal(AgreementWorkflow.DraftPreparation, AgreementWorkflow.StepOf(Candidate(CandidateStatus.Drafting, null), []));
    }

    [Fact]
    public void Each_participants_acceptance_of_the_current_revision_moves_the_step_on()
    {
        var drafting = Candidate(CandidateStatus.Drafting, Current);

        Assert.Equal(1, AgreementWorkflow.StepOf(drafting, []));
        Assert.Equal(2, AgreementWorkflow.StepOf(drafting, [Acceptance(Current, Mentor)]));
        Assert.Equal(3, AgreementWorkflow.StepOf(drafting, [Acceptance(Current, Mentor), Acceptance(Current, Protege)]));
    }

    // AC-FR-027-03, AC-FR-029-02, AC-BR-019-01: an acceptance of superseded content never combines with one of the current.
    [Fact]
    public void An_acceptance_of_a_superseded_revision_counts_for_nothing()
    {
        var drafting = Candidate(CandidateStatus.Drafting, Current);

        Assert.Equal(1, AgreementWorkflow.StepOf(drafting, [Acceptance(Superseded, Protege), Acceptance(Superseded, Mentor)]));
        Assert.Equal(2, AgreementWorkflow.StepOf(drafting, [Acceptance(Superseded, Protege), Acceptance(Current, Mentor)]));
    }

    [Fact]
    public void Only_the_identified_participants_count_and_each_once()
    {
        var drafting = Candidate(CandidateStatus.Drafting, Current);

        Assert.Equal(1, AgreementWorkflow.StepOf(drafting, [Acceptance(Current, Guid.NewGuid())]));
        Assert.Equal(2, AgreementWorkflow.StepOf(drafting, [Acceptance(Current, Protege), Acceptance(Current, Protege)]));
    }

    [Fact]
    public void A_formed_candidate_is_at_step_4_and_one_before_drafting_or_withdrawn_has_none()
    {
        var both = new[] { Acceptance(Current, Protege), Acceptance(Current, Mentor) };

        Assert.Equal(AgreementWorkflow.Formed, AgreementWorkflow.StepOf(Candidate(CandidateStatus.Formed, Current), both));
        Assert.Null(AgreementWorkflow.StepOf(Candidate(CandidateStatus.Withdrawn, Current), both));
        Assert.All(new[] { CandidateStatus.Invited, CandidateStatus.Accepted, CandidateStatus.Declined },
            status => Assert.Null(AgreementWorkflow.StepOf(Candidate(status, null), [])));
    }

    private static MentorSelection Candidate(CandidateStatus status, Guid? currentRevision) => new()
    {
        Id = Guid.NewGuid(),
        ProtegeEmployeeId = Protege,
        MentorEmployeeId = Mentor,
        Status = status,
        CurrentRevisionId = currentRevision,
    };

    private static AgreementAcceptance Acceptance(Guid revision, Guid participant) => new()
    {
        Id = Guid.NewGuid(),
        RevisionId = revision,
        ContentHash = new string('a', 64),
        ParticipantEmployeeId = participant,
    };
}

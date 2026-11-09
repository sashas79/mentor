using Mentor.Core.Requests;

namespace Mentor.Tests.Unit.Requests;

// ADR-015: the decisions each candidate status admits, and the willingness rule that makes mutual acceptance.
public sealed class CandidateLifecycleTests
{
    private static readonly Guid Protege = Guid.NewGuid();
    private static readonly Guid Mentor = Guid.NewGuid();

    [Fact]
    public void An_employee_is_the_protege_the_mentor_or_neither()
    {
        var candidate = Candidate(CandidateStatus.Invited);

        Assert.Equal(Participant.Protege, CandidateLifecycle.ParticipantOf(candidate, Protege));
        Assert.Equal(Participant.Mentor, CandidateLifecycle.ParticipantOf(candidate, Mentor));
        Assert.Equal(Participant.None, CandidateLifecycle.ParticipantOf(candidate, Guid.NewGuid()));
    }

    // AC-FR-019-01: a pending invitation, and only a pending one, is answered.
    [Fact]
    public void Only_a_pending_invitation_is_answered()
    {
        Assert.Equal(
            [CandidateStatus.Invited],
            Enum.GetValues<CandidateStatus>().Where(status => CandidateLifecycle.AdmitsResponse(Candidate(status))));
    }

    // AC-FR-021-02: a candidate is withdrawn before formation; a declined, withdrawn or formed one has ended.
    [Fact]
    public void A_candidate_is_withdrawn_only_before_it_has_ended_or_been_formed()
    {
        Assert.Equal(
            [CandidateStatus.Invited, CandidateStatus.Accepted, CandidateStatus.Drafting],
            Enum.GetValues<CandidateStatus>().Where(status => CandidateLifecycle.AdmitsWithdrawal(Candidate(status))));
    }

    // AC-FR-019-02, AC-FR-020-02: willingness follows the mentor's acceptance, and each participant records their own,
    // once; a declined invitation admits none.
    [Fact]
    public void Each_participant_records_their_own_willingness_once_after_the_mentor_has_accepted()
    {
        foreach (var status in Enum.GetValues<CandidateStatus>().Where(status => status != CandidateStatus.Accepted))
        {
            Assert.False(CandidateLifecycle.AdmitsWillingness(Candidate(status), Participant.Protege), status.ToString());
            Assert.False(CandidateLifecycle.AdmitsWillingness(Candidate(status), Participant.Mentor), status.ToString());
        }

        var accepted = Candidate(CandidateStatus.Accepted);
        Assert.True(CandidateLifecycle.AdmitsWillingness(accepted, Participant.Protege));
        Assert.True(CandidateLifecycle.AdmitsWillingness(accepted, Participant.Mentor));
        Assert.False(CandidateLifecycle.AdmitsWillingness(accepted, Participant.None));

        var protegeWilling = Candidate(CandidateStatus.Accepted, protegeWilling: true);
        Assert.False(CandidateLifecycle.AdmitsWillingness(protegeWilling, Participant.Protege));
        Assert.True(CandidateLifecycle.AdmitsWillingness(protegeWilling, Participant.Mentor));

        var mentorWilling = Candidate(CandidateStatus.Accepted, mentorWilling: true);
        Assert.True(CandidateLifecycle.AdmitsWillingness(mentorWilling, Participant.Protege));
        Assert.False(CandidateLifecycle.AdmitsWillingness(mentorWilling, Participant.Mentor));
    }

    // AC-FR-020-01: one participant's willingness is not mutual acceptance; the second one's completes it.
    [Fact]
    public void Only_the_second_willingness_completes_mutual_willingness()
    {
        var accepted = Candidate(CandidateStatus.Accepted);
        Assert.False(CandidateLifecycle.CompletesMutualWillingness(accepted, Participant.Protege));
        Assert.False(CandidateLifecycle.CompletesMutualWillingness(accepted, Participant.Mentor));

        Assert.True(CandidateLifecycle.CompletesMutualWillingness(Candidate(CandidateStatus.Accepted, mentorWilling: true), Participant.Protege));
        Assert.True(CandidateLifecycle.CompletesMutualWillingness(Candidate(CandidateStatus.Accepted, protegeWilling: true), Participant.Mentor));
        Assert.False(CandidateLifecycle.CompletesMutualWillingness(Candidate(CandidateStatus.Accepted, protegeWilling: true), Participant.None));
    }

    private static MentorSelection Candidate(CandidateStatus status, bool protegeWilling = false, bool mentorWilling = false) => new()
    {
        ProtegeEmployeeId = Protege,
        MentorEmployeeId = Mentor,
        Status = status,
        ProtegeWillingAt = protegeWilling ? DateTimeOffset.UtcNow : null,
        MentorWillingAt = mentorWilling ? DateTimeOffset.UtcNow : null,
    };
}

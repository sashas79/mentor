using Mentor.Core.Agreements;
using Mentor.Core.Formation;
using Mentor.Core.Requests;

namespace Mentor.Tests.Unit.Formation;

// ADR-005, FR-030, FR-031, FR-070, BR-003: a formation decision is taken on the current revision only, naming its exact
// hash, after both identified participants accepted exactly that revision, under current mutual willingness; each of
// these is re-derived from the head and the acceptance rows, never trusted from anything stored about them.
public sealed class FormationRulesTests
{
    private static readonly Guid Protege = Guid.NewGuid();
    private static readonly Guid Mentor = Guid.NewGuid();
    private static readonly RevisionReference Current = new(Guid.NewGuid(), new string('a', 64));
    private static readonly RevisionReference Superseded = new(Guid.NewGuid(), new string('b', 64));

    // AC-FR-031-02: all three current prerequisites for the same candidate, draft and parties.
    [Fact]
    public void Both_acceptances_of_the_current_revision_under_mutual_willingness_admit_a_decision()
    {
        Assert.Equal(FormationBlock.None, FormationRules.Check(Drafting(), Current, Current.ContentHash, Both(Current)));
    }

    // AC-E2E-02, AC-FR-031-01: without current mutual acceptance of the pairing nothing is decided.
    [Theory]
    [InlineData(CandidateStatus.Invited)]
    [InlineData(CandidateStatus.Declined)]
    [InlineData(CandidateStatus.Accepted)]
    [InlineData(CandidateStatus.Formed)]
    [InlineData(CandidateStatus.Withdrawn)]
    public void A_candidate_that_is_not_drafting_admits_no_decision(CandidateStatus status)
    {
        var candidate = Drafting();
        candidate.Status = status;

        Assert.Equal(FormationBlock.NotDrafting, FormationRules.Check(candidate, Current, Current.ContentHash, Both(Current)));
    }

    // ADR-005 check 1: each column of the mutual acceptance invariant is checked, not the status alone.
    [Fact]
    public void Each_column_of_mutual_acceptance_is_checked_and_not_the_status_alone()
    {
        var broken = new Action<MentorSelection>[]
        {
            candidate => candidate.MentorResponse = MentorResponse.Pending,
            candidate => candidate.MentorResponse = MentorResponse.Declined,
            candidate => candidate.ProtegeWillingAt = null,
            candidate => candidate.MentorWillingAt = null,
            candidate => (candidate.WithdrawnByEmployeeId, candidate.WithdrawnAt) = (Protege, DateTimeOffset.UtcNow),
        };

        Assert.True(FormationRules.HoldsCurrentMutualWillingness(Drafting()));
        foreach (var breakIt in broken)
        {
            var candidate = Drafting();
            breakIt(candidate);
            Assert.False(FormationRules.HoldsCurrentMutualWillingness(candidate));
            Assert.Equal(FormationBlock.NotDrafting, FormationRules.Check(candidate, Current, Current.ContentHash, Both(Current)));
        }
    }

    // AC-FR-030-03, AC-FR-070-01, AC-E2E-10: a superseded revision admits no decision, whatever was accepted of it.
    [Fact]
    public void A_superseded_revision_admits_no_decision_even_with_both_of_its_acceptances()
    {
        Assert.Equal(FormationBlock.RevisionSuperseded, FormationRules.Check(Drafting(), Superseded, Superseded.ContentHash, Both(Superseded)));
    }

    // ADR-004, ADR-005 check 2: the administrator names exactly the content reviewed, compared ordinally.
    [Fact]
    public void The_hash_named_must_be_exactly_the_current_revisions()
    {
        Assert.Equal(FormationBlock.ContentMismatch, FormationRules.Check(Drafting(), Current, Superseded.ContentHash, Both(Current)));
        Assert.Equal(FormationBlock.ContentMismatch, FormationRules.Check(Drafting(), Current, Current.ContentHash.ToUpperInvariant(), Both(Current)));
    }

    // AC-FR-030-01, AC-FR-031-01, AC-FR-070-02, AC-BR-019-01: only the two identified participants' acceptances of exactly
    // the current revision and its content count; one is not enough, and nothing else makes up the pair.
    [Fact]
    public void Only_both_identified_participants_acceptances_of_exactly_the_current_content_count()
    {
        var drafting = Drafting();

        Assert.Equal(FormationBlock.NotAccepted, FormationRules.Check(drafting, Current, Current.ContentHash, []));
        Assert.Equal(FormationBlock.NotAccepted, FormationRules.Check(drafting, Current, Current.ContentHash, [Acceptance(Current, Protege)]));
        Assert.Equal(FormationBlock.NotAccepted, FormationRules.Check(drafting, Current, Current.ContentHash, [Acceptance(Current, Mentor)]));
        Assert.Equal(FormationBlock.NotAccepted,
            FormationRules.Check(drafting, Current, Current.ContentHash, [Acceptance(Current, Protege), Acceptance(Superseded, Mentor)]));
        Assert.Equal(FormationBlock.NotAccepted,
            FormationRules.Check(drafting, Current, Current.ContentHash, [Acceptance(Current, Protege), Acceptance(Current, Guid.NewGuid())]));
        Assert.Equal(FormationBlock.NotAccepted,
            FormationRules.Check(drafting, Current, Current.ContentHash, [Acceptance(Current, Protege), Acceptance(Current, Mentor, Superseded.ContentHash)]));
        Assert.Equal(FormationBlock.NotAccepted,
            FormationRules.Check(drafting, Current, Current.ContentHash, [Acceptance(Current, Protege), Acceptance(Current, Protege)]));
        // An acceptance is of a revision, not of a hash alone: a superseded revision whose content the current one restores
        // has the same hash, and its acceptance still counts for nothing.
        Assert.Equal(FormationBlock.NotAccepted,
            FormationRules.Check(drafting, Current, Current.ContentHash, [Acceptance(Current, Protege), Acceptance(Superseded, Mentor, Current.ContentHash)]));
    }

    // ADR-005: the checks run in its order, so a stale decision is reported as stale before anything about acceptances.
    [Fact]
    public void The_checks_run_in_the_order_of_adr_005()
    {
        var withdrawn = Drafting();
        withdrawn.Status = CandidateStatus.Withdrawn;

        Assert.Equal(FormationBlock.NotDrafting, FormationRules.Check(withdrawn, Superseded, "other", []));
        Assert.Equal(FormationBlock.RevisionSuperseded, FormationRules.Check(Drafting(), Superseded, "other", []));
        Assert.Equal(FormationBlock.ContentMismatch, FormationRules.Check(Drafting(), Current, "other", []));
    }

    private static MentorSelection Drafting() => new()
    {
        Id = Guid.NewGuid(),
        RequestId = Guid.NewGuid(),
        ProtegeEmployeeId = Protege,
        MentorEmployeeId = Mentor,
        Status = CandidateStatus.Drafting,
        MentorResponse = MentorResponse.Accepted,
        MentorRespondedAt = DateTimeOffset.UtcNow.AddDays(-3),
        ProtegeWillingAt = DateTimeOffset.UtcNow.AddDays(-2),
        MentorWillingAt = DateTimeOffset.UtcNow.AddDays(-1),
        CurrentRevisionId = Current.RevisionId,
    };

    private static AgreementAcceptance[] Both(RevisionReference revision) => [Acceptance(revision, Protege), Acceptance(revision, Mentor)];

    private static AgreementAcceptance Acceptance(RevisionReference revision, Guid participant, string? contentHash = null) => new()
    {
        Id = Guid.NewGuid(),
        RevisionId = revision.RevisionId,
        ContentHash = contentHash ?? revision.ContentHash,
        ParticipantEmployeeId = participant,
    };
}

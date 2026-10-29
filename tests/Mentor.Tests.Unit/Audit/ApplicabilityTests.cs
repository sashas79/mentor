using Mentor.Core.Agreements;
using Mentor.Core.Requests;

namespace Mentor.Tests.Unit.Audit;

// Applicability is derived from the candidate head, never stored (ADR-004; AC-FR-028-02, AC-FR-069-01).
public sealed class ApplicabilityTests
{
    private static readonly Guid Current = Guid.NewGuid();
    private static readonly Guid Earlier = Guid.NewGuid();

    [Theory]
    [InlineData(CandidateStatus.Drafting, true, Applicability.Applicable)]
    [InlineData(CandidateStatus.Drafting, false, Applicability.Superseded)]
    [InlineData(CandidateStatus.Formed, true, Applicability.Formed)]
    [InlineData(CandidateStatus.Formed, false, Applicability.Superseded)]
    [InlineData(CandidateStatus.Withdrawn, true, Applicability.Withdrawn)]
    [InlineData(CandidateStatus.Withdrawn, false, Applicability.Superseded)]
    public void Evidence_applies_only_on_the_current_revision_of_a_drafting_candidate(CandidateStatus status, bool onCurrentRevision, Applicability expected) =>
        Assert.Equal(expected, ApplicabilityRule.Of(status, Current, onCurrentRevision ? Current : Earlier));

    [Fact]
    public void Evidence_of_a_withdrawn_candidate_that_never_had_a_revision_cannot_exist_but_would_be_superseded_by_nothing() =>
        Assert.Equal(Applicability.Superseded, ApplicabilityRule.Of(CandidateStatus.Withdrawn, null, Earlier));

    [Theory]
    [InlineData(CandidateStatus.Invited)]
    [InlineData(CandidateStatus.Declined)]
    [InlineData(CandidateStatus.Accepted)]
    public void A_candidate_before_drafting_holds_no_revision(CandidateStatus status) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ApplicabilityRule.Of(status, Current, Current));
}

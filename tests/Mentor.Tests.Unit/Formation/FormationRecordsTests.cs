using Mentor.Core.Agreements;
using Mentor.Core.Formation;
using Mentor.Core.Goals;
using Mentor.Core.Relationships;
using Mentor.Core.Requests;

namespace Mentor.Tests.Unit.Formation;

// ADR-004, ADR-005, ADR-006, FR-071, DR-005, DR-009: what an approval forms is composed from the candidate and the approved
// revision alone, so nothing an actor supplies reaches it.
public sealed class FormationRecordsTests
{
    private static readonly Guid Protege = Guid.NewGuid();
    private static readonly Guid Mentor = Guid.NewGuid();

    // AC-E2E-01, AC-FR-071-01, AC-DR-005-01: digital, the candidate's participants, In Progress, blank and N, and the
    // duration and dates of the accepted terms.
    [Fact]
    public void The_relationship_is_digital_in_progress_blank_and_not_active_with_the_accepted_terms()
    {
        var candidate = Candidate();

        var relationship = FormationRecords.Relationship(candidate, Content(DurationCategory.TwelveMonths, new DateOnly(2026, 12, 1), new DateOnly(2027, 11, 30)));

        Assert.Equal((Provenance.Digital, (Guid?)candidate.Id, Protege, Mentor),
            (relationship.Provenance, relationship.CandidateId, relationship.ProtegeEmployeeId, relationship.MentorEmployeeId));
        Assert.Equal((RelationshipState.InProgress, RelationshipStatus.Blank, ActiveFlag.N),
            (relationship.State!.Value, relationship.Status!.Value, relationship.IsActive!.Value));
        Assert.Equal((DurationCategory.TwelveMonths, new DateOnly(2026, 12, 1), new DateOnly(2027, 11, 30)),
            (relationship.DurationCategory!.Value, relationship.StartDate!.Value, relationship.EndDate!.Value));
        Assert.Null(relationship.EnteredByEmployeeId);
        Assert.Null(relationship.SourceFile);
    }

    // AC-BR-010-01, AC-BR-011-01: a 12+ agreement with no dates forms a relationship with no dates; none is invented.
    [Fact]
    public void Absent_dates_stay_absent()
    {
        var relationship = FormationRecords.Relationship(Candidate(), Content(DurationCategory.TwelvePlusMonths, null, null));

        Assert.Equal(DurationCategory.TwelvePlusMonths, relationship.DurationCategory);
        Assert.Null(relationship.StartDate);
        Assert.Null(relationship.EndDate);
    }

    // BR-021: the relationship's parties are the candidate's; content naming anyone else forms nothing.
    [Fact]
    public void Content_naming_other_parties_forms_nothing()
    {
        var other = Content(DurationCategory.SixMonths, null, null) with
        {
            Parties = new AgreementParties(new AgreementParty(Guid.NewGuid(), "Someone else"), new AgreementParty(Mentor, "Mentor")),
        };

        Assert.Throws<ArgumentException>(() => FormationRecords.Relationship(Candidate(), other));
    }

    // AC-FR-032-01, AC-DR-006-01: the formed agreement is the approved revision's content and hash, byte for byte, with the
    // annex as it stands kept beside the content and never inside it.
    [Fact]
    public void The_formed_agreement_keeps_the_accepted_bytes_and_the_annex_beside_them()
    {
        var candidate = Candidate();
        candidate.AdminReference = "TM-2026-014";
        candidate.AdminRemarks = "Filed with the register.";
        var revision = Revision(candidate.Id);
        var relationship = Guid.NewGuid();

        var agreement = FormationRecords.Agreement(candidate, revision, relationship);

        Assert.Equal((relationship, candidate.Id, revision.Id), (agreement.RelationshipId, agreement.CandidateId, agreement.RevisionId));
        Assert.Equal((revision.Content, revision.ContentHash), (agreement.Content, agreement.ContentHash));
        Assert.Equal(AgreementContentHash.Compute(agreement.Content), agreement.ContentHash);
        Assert.Equal(("TM-2026-014", "Filed with the register."), (agreement.AdminReference, agreement.AdminRemarks));
        Assert.DoesNotContain("TM-2026-014", agreement.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Another_candidates_revision_forms_no_agreement()
    {
        Assert.Throws<ArgumentException>(() => FormationRecords.Agreement(Candidate(), Revision(Guid.NewGuid()), Guid.NewGuid()));
    }

    // DR-009, AC-FR-032-02: one goal per agreed goal, in its order, worded as agreed, incomplete, and of this relationship
    // and its formed agreement only.
    [Fact]
    public void Each_agreed_goal_becomes_an_incomplete_goal_of_the_relationship_in_its_order()
    {
        var relationship = Guid.NewGuid();
        var agreement = Guid.NewGuid();

        var goals = FormationRecords.Goals(Content(DurationCategory.SixMonths, null, null), relationship, agreement);

        Assert.Equal([(1, "Lead a design review by June 2027"), (2, "Present at the architecture forum")],
            goals.Select(goal => (goal.Position, goal.AgreedDescription)));
        Assert.All(goals, goal =>
            Assert.Equal((relationship, agreement, GoalCompletionStatus.Incomplete), (goal.RelationshipId, goal.FormedAgreementId, goal.CompletionStatus)));
    }

    private static MentorSelection Candidate() => new()
    {
        Id = Guid.NewGuid(),
        RequestId = Guid.NewGuid(),
        ProtegeEmployeeId = Protege,
        MentorEmployeeId = Mentor,
        Status = CandidateStatus.Drafting,
        MentorResponse = MentorResponse.Accepted,
        ProtegeWillingAt = DateTimeOffset.UtcNow,
        MentorWillingAt = DateTimeOffset.UtcNow,
    };

    private static AgreementRevision Revision(Guid candidate)
    {
        var content = Content(DurationCategory.TwelveMonths, null, null).ToCanonicalJson();
        return new AgreementRevision
        {
            Id = Guid.NewGuid(),
            CandidateId = candidate,
            RevisionNumber = 2,
            Content = content,
            ContentHash = AgreementContentHash.Compute(content),
            CreatedByEmployeeId = Protege,
        };
    }

    private static AgreementContent Content(DurationCategory duration, DateOnly? start, DateOnly? end) => new(
        new AgreementParties(new AgreementParty(Protege, "Protégé"), new AgreementParty(Mentor, "Mentor")),
        AgreementPolicy.Clauses,
        new AgreementTerms(duration, start, end, 2, 45, new ContactLimit(1, ContactUnit.Weeks), "Propose a new date", null),
        ["Lead a design review by June 2027", "Present at the architecture forum"],
        ["Delivery deadlines"],
        [],
        new AgreementResponsibilities(["Share experience"], ["Prepare an agenda"]));
}

using Mentor.Api.Goals;
using Mentor.Api.Register;
using Mentor.Core.Goals;
using Mentor.Core.Legacy;
using Mentor.Core.Relationships;

namespace Mentor.Tests.Unit.Register;

// ADR-011, AC-FR-060-01: a register row copies the stored values and invents none. The review exception is derived from
// the relationship as it now reads, so a value an administrator has since set is no longer listed as unresolved. Historical
// rows come first in tracker order, then digital rows in formation order.
public sealed class RegisterEntriesTests
{
    private static readonly RegisterPersonView Mentor = new(Guid.NewGuid(), "Mentor", null);
    private static readonly RegisterPersonView Protege = new(Guid.NewGuid(), "Protégé", null);

    [Fact]
    public void A_digital_row_has_no_source_and_no_review_exception()
    {
        var entry = RegisterEntries.Of(Relationship(Provenance.Digital, RelationshipState.InProgress, RelationshipStatus.Blank, ActiveFlag.Y), Mentor, Protege);

        Assert.Equal((RegisterEntryKind.Relationship, Provenance.Digital), (entry.Kind, entry.Provenance));
        Assert.Null(entry.Source);
        Assert.Null(entry.ReviewException);
        Assert.Null(entry.ReviewExceptionId);
    }

    [Theory]
    [InlineData(null, null, null, new[] { RelationshipAttribute.State, RelationshipAttribute.Status, RelationshipAttribute.IsActive })]
    [InlineData(RelationshipState.Complete, null, null, new[] { RelationshipAttribute.Status, RelationshipAttribute.IsActive })]
    [InlineData(null, RelationshipStatus.NeedsReassigning, null, new[] { RelationshipAttribute.State, RelationshipAttribute.IsActive })]
    [InlineData(RelationshipState.OnHold, RelationshipStatus.Blank, null, new[] { RelationshipAttribute.IsActive })]
    public void A_legacy_row_lists_the_attributes_that_still_hold_no_value(
        RelationshipState? state, RelationshipStatus? status, ActiveFlag? isActive, RelationshipAttribute[] unresolved)
    {
        var review = RegisterEntries.ReviewOf(Relationship(Provenance.Legacy, state, status, isActive));

        Assert.NotNull(review);
        Assert.Equal(LegacyReviewReason.UnresolvedValue, review.Reason);
        Assert.Equal(unresolved, review.UnresolvedAttributes);
    }

    [Fact]
    public void A_legacy_row_whose_values_are_all_set_is_no_longer_a_review_exception()
    {
        var entry = RegisterEntries.Of(Relationship(Provenance.Legacy, RelationshipState.Complete, RelationshipStatus.Blank, ActiveFlag.N), Mentor, Protege);

        Assert.Null(entry.ReviewException);
        Assert.Equal(("Tracker.xlsx", 4), (entry.Source!.File, entry.Source.Row));
    }

    [Fact]
    public void Only_a_held_source_row_has_a_row_of_its_own()
    {
        var unresolvedValue = new LegacyReviewException
        {
            Id = Guid.NewGuid(),
            SourceFile = "Tracker.xlsx",
            SourceRow = 4,
            Reason = LegacyReviewReason.UnresolvedValue,
            RelationshipId = Guid.NewGuid(),
            Attribute = RelationshipAttribute.State,
        };

        Assert.Throws<ArgumentException>(() => RegisterEntries.Held(unresolvedValue));
    }

    [Fact]
    public void A_held_source_row_carries_its_reason_and_source_and_nothing_resolved()
    {
        var exception = new LegacyReviewException
        {
            Id = Guid.NewGuid(),
            SourceFile = "Tracker.xlsx",
            SourceRow = 9,
            Reason = LegacyReviewReason.NoMentor,
            SourceProtegeText = "S. Dlamini",
        };

        var held = RegisterEntries.Held(exception);

        Assert.Equal((RegisterEntryKind.HeldSourceRow, (Guid?)null, exception.Id, Provenance.Legacy), (held.Kind, held.RelationshipId, held.ReviewExceptionId, held.Provenance));
        Assert.Equal((LegacyReviewReason.NoMentor, 0), (held.ReviewException!.Reason, held.ReviewException.UnresolvedAttributes.Count));
        Assert.Equal(("Tracker.xlsx", 9, "S. Dlamini"), (held.Source!.File, held.Source.Row, held.Source.ProtegeText));
        Assert.Null(held.Mentor);
        Assert.Null(held.Protege);
        Assert.Null(held.State);
        Assert.Null(held.Status);
        Assert.Null(held.IsActive);
        Assert.Null(held.StartDate);
        Assert.Null(held.EndDate);
        Assert.Null(held.DurationCategory);
    }

    [Fact]
    public void Historical_rows_come_first_in_tracker_order_then_digital_rows_in_formation_order()
    {
        var formedFirst = RegisterEntries.Of(Relationship(Provenance.Digital, RelationshipState.InProgress, RelationshipStatus.Blank, ActiveFlag.Y), Mentor, Protege);
        var formedLater = RegisterEntries.Of(Relationship(Provenance.Digital, RelationshipState.InProgress, RelationshipStatus.Blank, ActiveFlag.Y), Mentor, Protege);
        var row12 = RegisterEntries.Of(Relationship(Provenance.Legacy, null, null, null, row: 12), Mentor, Protege);
        var row4 = RegisterEntries.Of(Relationship(Provenance.Legacy, null, null, null, row: 4), Mentor, Protege);
        var row9 = RegisterEntries.Held(new LegacyReviewException { Id = Guid.NewGuid(), SourceFile = "Tracker.xlsx", SourceRow = 9, Reason = LegacyReviewReason.NoMentor });
        var otherFile = RegisterEntries.Held(new LegacyReviewException { Id = Guid.NewGuid(), SourceFile = "Archive.xlsx", SourceRow = 30, Reason = LegacyReviewReason.UnresolvedPerson });
        var start = new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

        var ordered = RegisterEntries.Ordered(
        [
            (formedLater, start.AddDays(2)),
            (row12, start),
            (formedFirst, start.AddDays(1)),
            (row9, start.AddDays(3)),
            (otherFile, start.AddDays(4)),
            (row4, start.AddDays(5)),
        ]);

        Assert.Equal([otherFile, row4, row9, row12, formedFirst, formedLater], ordered);
    }

    [Fact]
    public void The_goal_summary_counts_completion_and_requests_awaiting_the_mentor()
    {
        GoalView Goal(GoalCompletionStatus status, Guid? pending) =>
            new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "Lead a design review", status, pending, [], default, default);

        var summary = RelationshipRegister.SummaryOf([
            Goal(GoalCompletionStatus.Complete, null),
            Goal(GoalCompletionStatus.Incomplete, Guid.NewGuid()),
            Goal(GoalCompletionStatus.Incomplete, null),
        ]);

        Assert.Equal(new GoalSummaryView(3, 1, 2, 1), summary);
        Assert.Equal(new GoalSummaryView(0, 0, 0, 0), RelationshipRegister.SummaryOf([]));
    }

    private static MentorshipRelationship Relationship(Provenance provenance, RelationshipState? state, RelationshipStatus? status, ActiveFlag? isActive, int row = 4) => new()
    {
        Id = Guid.NewGuid(),
        Provenance = provenance,
        CandidateId = provenance == Provenance.Digital ? Guid.NewGuid() : null,
        ProtegeEmployeeId = Protege.EmployeeId,
        MentorEmployeeId = Mentor.EmployeeId,
        State = state,
        Status = status,
        IsActive = isActive,
        SourceFile = provenance == Provenance.Legacy ? "Tracker.xlsx" : null,
        SourceRow = provenance == Provenance.Legacy ? row : null,
    };
}

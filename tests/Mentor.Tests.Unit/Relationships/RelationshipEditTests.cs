using Mentor.Core.Legacy;
using Mentor.Core.Relationships;

namespace Mentor.Tests.Unit.Relationships;

// ADR-006, FR-043 to FR-050, BR-006 to BR-009: an administrator's edit names any of State, Status and IsActive, each
// exactly one of its business values; it changes what it names and nothing else, refuses no combination and derives
// nothing, and an unchanged value is no change.
public sealed class RelationshipEditTests
{
    // AC-FR-044-01, AC-FR-045-01, AC-FR-046-01, AC-BR-006-01, AC-BR-007-01: each business value, as the API names it.
    [Theory]
    [InlineData("InProgress", null, null)]
    [InlineData("OnHold", null, null)]
    [InlineData("Complete", null, null)]
    [InlineData(null, "Blank", null)]
    [InlineData(null, "NeedsReassigning", null)]
    [InlineData(null, null, "Y")]
    [InlineData(null, null, "N")]
    [InlineData("Complete", "NeedsReassigning", "Y")]
    public void Each_business_value_is_accepted(string? state, string? status, string? isActive)
    {
        Assert.True(RelationshipEdit.TryCreate(state, status, isActive, out var edit, out var errors));
        Assert.Empty(errors);
        Assert.Equal((state, status, isActive), (edit.State?.ToString(), edit.Status?.ToString(), edit.IsActive?.ToString()));
    }

    // AC-FR-044-01, AC-BR-006-01, AC-FR-071-02: "Unknown" is no State, and IsActive is Y or N, never y, Yes, 0 or 1; any
    // other spelling is refused and named.
    [Theory]
    [InlineData("Unknown", null, null, "state")]
    [InlineData("IN_PROGRESS", null, null, "state")]
    [InlineData("inprogress", null, null, "state")]
    [InlineData("", null, null, "state")]
    [InlineData(null, "Needs reassigning", null, "status")]
    [InlineData(null, "BLANK", null, "status")]
    [InlineData(null, "", null, "status")]
    [InlineData(null, null, "y", "isActive")]
    [InlineData(null, null, "Yes", "isActive")]
    [InlineData(null, null, "0", "isActive")]
    [InlineData(null, null, "1", "isActive")]
    [InlineData(null, null, "True", "isActive")]
    [InlineData(null, null, " N", "isActive")]
    public void Any_other_value_is_refused_and_named(string? state, string? status, string? isActive, string member)
    {
        Assert.False(RelationshipEdit.TryCreate(state, status, isActive, out var edit, out var errors));
        Assert.Null(edit);
        Assert.Equal([member], errors.Keys);
    }

    [Fact]
    public void An_edit_names_at_least_one_attribute()
    {
        Assert.False(RelationshipEdit.TryCreate(null, null, null, out _, out var errors));
        Assert.Equal(["attributes"], errors.Keys);
    }

    // AC-FR-043-01, AC-FR-048-01, AC-BR-007-01: changing one attribute requires no change to either other, and the others
    // keep their values.
    [Fact]
    public void An_edit_changes_only_what_it_names()
    {
        var formed = Relationship(RelationshipState.InProgress, RelationshipStatus.Blank, ActiveFlag.N);

        Assert.Equal([new AttributeChange(RelationshipAttribute.State, RelationshipState.InProgress, RelationshipState.Complete)],
            Edit(state: "Complete").ChangesTo(formed));
        Assert.Equal([new AttributeChange(RelationshipAttribute.Status, RelationshipStatus.Blank, RelationshipStatus.NeedsReassigning)],
            Edit(status: "NeedsReassigning").ChangesTo(formed));
        Assert.Equal([new AttributeChange(RelationshipAttribute.IsActive, ActiveFlag.N, ActiveFlag.Y)], Edit(isActive: "Y").ChangesTo(formed));
    }

    // AC-FR-047-01, AC-FR-047-02, AC-BR-006-01: every State is reached from every other, directly.
    [Theory]
    [InlineData(RelationshipState.InProgress, "OnHold")]
    [InlineData(RelationshipState.InProgress, "Complete")]
    [InlineData(RelationshipState.OnHold, "InProgress")]
    [InlineData(RelationshipState.OnHold, "Complete")]
    [InlineData(RelationshipState.Complete, "InProgress")]
    [InlineData(RelationshipState.Complete, "OnHold")]
    public void Every_state_is_reached_from_every_other_directly(RelationshipState from, string to)
    {
        var change = Assert.Single(Edit(state: to).ChangesTo(Relationship(from, RelationshipStatus.Blank, ActiveFlag.N)));

        Assert.Equal((RelationshipAttribute.State, (Enum)from, (Enum)Enum.Parse<RelationshipState>(to)), (change.Attribute, change.Prior!, change.New));
    }

    // AC-FR-046-01, AC-FR-049-01, AC-FR-049-02, BR-009: from any combination to any other, every named attribute that
    // differs changes to exactly the value named; no combination is refused and nothing is derived or recalculated.
    [Fact]
    public void No_combination_is_refused_and_nothing_is_derived()
    {
        foreach (var (state, status, isActive) in Combinations())
        {
            var edit = Edit(state.ToString(), status.ToString(), isActive.ToString());
            foreach (var (fromState, fromStatus, fromIsActive) in Combinations())
            {
                var expected = new List<AttributeChange>();
                if (state != fromState)
                {
                    expected.Add(new AttributeChange(RelationshipAttribute.State, fromState, state));
                }

                if (status != fromStatus)
                {
                    expected.Add(new AttributeChange(RelationshipAttribute.Status, fromStatus, status));
                }

                if (isActive != fromIsActive)
                {
                    expected.Add(new AttributeChange(RelationshipAttribute.IsActive, fromIsActive, isActive));
                }

                Assert.Equal(expected, edit.ChangesTo(Relationship(fromState, fromStatus, fromIsActive)));
            }
        }
    }

    // AC-FR-050-01, AC-FR-071-03: naming the current values changes nothing, so no default or stale value is ever re-applied.
    [Fact]
    public void Naming_the_current_values_changes_nothing()
    {
        Assert.Empty(Edit("Complete", "NeedsReassigning", "Y").ChangesTo(Relationship(RelationshipState.Complete, RelationshipStatus.NeedsReassigning, ActiveFlag.Y)));
    }

    // AC-FR-044-01, AC-DR-005-01, AC-FR-051-01: a value a legacy row's source left unresolved is unknown (null), unlike
    // every value, the first of each enumeration included; an edit resolves it with any value and records it from unknown.
    [Fact]
    public void Every_unresolved_legacy_value_is_resolved_by_naming_any_of_its_values()
    {
        var legacy = new MentorshipRelationship { Id = Guid.NewGuid(), Provenance = Provenance.Legacy };

        foreach (var state in Enum.GetValues<RelationshipState>())
        {
            Assert.Equal([new AttributeChange(RelationshipAttribute.State, null, state)], Edit(state: state.ToString()).ChangesTo(legacy));
        }

        foreach (var status in Enum.GetValues<RelationshipStatus>())
        {
            Assert.Equal([new AttributeChange(RelationshipAttribute.Status, null, status)], Edit(status: status.ToString()).ChangesTo(legacy));
        }

        foreach (var isActive in Enum.GetValues<ActiveFlag>())
        {
            Assert.Equal([new AttributeChange(RelationshipAttribute.IsActive, null, isActive)], Edit(isActive: isActive.ToString()).ChangesTo(legacy));
        }
    }

    // FR-051: each change is recorded under the attribute's camel-case property name.
    [Fact]
    public void Each_change_is_recorded_under_the_attributes_property_name()
    {
        var changes = Edit("Complete", "NeedsReassigning", "Y").ChangesTo(Relationship(RelationshipState.InProgress, RelationshipStatus.Blank, ActiveFlag.N));

        Assert.Equal(["state", "status", "isActive"], changes.Select(change => change.Field));
    }

    private static RelationshipEdit Edit(string? state = null, string? status = null, string? isActive = null) =>
        RelationshipEdit.TryCreate(state, status, isActive, out var edit, out _) ? edit : throw new ArgumentException("Not an edit.");

    private static MentorshipRelationship Relationship(RelationshipState state, RelationshipStatus status, ActiveFlag isActive) => new()
    {
        Id = Guid.NewGuid(),
        Provenance = Provenance.Digital,
        CandidateId = Guid.NewGuid(),
        ProtegeEmployeeId = Guid.NewGuid(),
        MentorEmployeeId = Guid.NewGuid(),
        State = state,
        Status = status,
        IsActive = isActive,
        Version = 1,
    };

    private static IEnumerable<(RelationshipState State, RelationshipStatus Status, ActiveFlag IsActive)> Combinations() =>
        from state in Enum.GetValues<RelationshipState>()
        from status in Enum.GetValues<RelationshipStatus>()
        from isActive in Enum.GetValues<ActiveFlag>()
        select (state, status, isActive);
}

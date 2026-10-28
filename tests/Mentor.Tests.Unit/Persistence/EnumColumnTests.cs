using Mentor.Core.Audit;
using Mentor.Core.Relationships;
using Mentor.Infrastructure.Persistence;

namespace Mentor.Tests.Unit.Persistence;

public sealed class EnumColumnTests
{
    [Fact]
    public void Members_are_stored_as_the_values_the_architecture_names()
    {
        Assert.Equal("IN_PROGRESS", EnumColumn.StoredValue(RelationshipState.InProgress));
        Assert.Equal("NEEDS_REASSIGNING", EnumColumn.StoredValue(RelationshipStatus.NeedsReassigning));
        Assert.Equal("BLANK", EnumColumn.StoredValue(RelationshipStatus.Blank));
        Assert.Equal("Y", EnumColumn.StoredValue(ActiveFlag.Y));
        Assert.Equal("TWELVE_PLUS_MONTHS", EnumColumn.StoredValue(DurationCategory.TwelvePlusMonths));
        Assert.Equal("LEGACY_SOURCE", EnumColumn.StoredValue(AuditProvenance.LegacySource));
    }

    [Fact]
    public void Every_member_reads_back_as_itself()
    {
        Assert.All(Enum.GetValues<AuditAction>(), action => Assert.Equal(action, EnumColumn.FromStoredValue<AuditAction>(EnumColumn.StoredValue(action))));
    }

    [Fact]
    public void The_check_constraint_lists_every_stored_value()
    {
        Assert.Equal("is_active IN ('Y', 'N')", EnumColumn.Check<ActiveFlag>("is_active"));
        Assert.Equal("state IN ('IN_PROGRESS', 'ON_HOLD', 'COMPLETE')", EnumColumn.Check<RelationshipState>("state"));
    }
}

using Mentor.Core.Audit;
using Mentor.Core.Relationships;
using Mentor.Infrastructure.Persistence;

namespace Mentor.Tests.Unit.Audit;

// Prior and new values are structured fields, stored as the columns they came from read (ADR-007, ADR-013).
public sealed class AuditValuesTests
{
    [Fact]
    public void Values_are_a_json_object_whose_enumerations_read_as_the_database_stores_them()
    {
        var relationship = Guid.Parse("8d2f6c4a-1b3e-4f5a-9c7d-2e1f0a9b8c7d");
        var values = new AuditValues()
            .Set("state", RelationshipState.InProgress)
            .Set("status", (RelationshipStatus?)null)
            .Set("isActive", ActiveFlag.N)
            .Set("relationshipId", relationship)
            .Set("completionRequested", true)
            .Set("position", 2)
            .Set("version", 7L)
            .Set("startDate", new DateOnly(2026, 1, 10))
            .Set("decidedAt", new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero))
            .Set("adminReference", "TM-2026-001");

        var json = AuditValuesJson.Serialize(values);

        Assert.Equal(
            """{"state":"IN_PROGRESS","status":null,"isActive":"N","relationshipId":"8d2f6c4a-1b3e-4f5a-9c7d-2e1f0a9b8c7d","completionRequested":true,"position":2,"version":7,"startDate":"2026-01-10","decidedAt":"2026-10-28T12:00:00+00:00","adminReference":"TM-2026-001"}""",
            json);
    }

    [Fact]
    public void No_values_serialise_to_no_document()
    {
        Assert.Null(AuditValuesJson.Serialize(null));
        Assert.Equal("{}", AuditValuesJson.Serialize(new AuditValues()));
    }

    [Fact]
    public void A_field_is_set_once()
    {
        var values = new AuditValues().Set("state", RelationshipState.OnHold);

        var refusal = Assert.Throws<ArgumentException>(() => values.Set("state", RelationshipState.Complete));

        Assert.Contains("state", refusal.Message);
        Assert.Single(values.Fields);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_field_has_a_name(string name) =>
        Assert.Throws<ArgumentException>(() => new AuditValues().Set(name, ActiveFlag.Y));
}

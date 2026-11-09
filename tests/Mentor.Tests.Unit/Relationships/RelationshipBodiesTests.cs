using System.Text.Json;
using System.Text.Json.Serialization;
using Mentor.Api.Relationships;

namespace Mentor.Tests.Unit.Relationships;

// ADR-006, FR-047, FR-071: an attribute edit names state, status and isActive, as text, and nothing else. The participants,
// the provenance, the candidate, the duration and dates, the version and any reassignment are not the edit's to name, and
// a number or a flag is not a value of IsActive.
public sealed class RelationshipBodiesTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public void An_edit_names_any_of_the_three_attributes()
    {
        Assert.Equal(new RelationshipAttributesRequest("Complete", "NeedsReassigning", "Y"),
            JsonSerializer.Deserialize<RelationshipAttributesRequest>("""{"state":"Complete","status":"NeedsReassigning","isActive":"Y"}""", Web));
        Assert.Equal(new RelationshipAttributesRequest(null, null, "N"), JsonSerializer.Deserialize<RelationshipAttributesRequest>("""{"isActive":"N"}""", Web));
    }

    [Theory]
    [InlineData("""{"state":"Complete","provenance":"Legacy"}""")]
    [InlineData("""{"state":"Complete","candidateId":"8f14e45f-ceea-467a-9575-7b9b1a7e0c11"}""")]
    [InlineData("""{"state":"Complete","mentorEmployeeId":"8f14e45f-ceea-467a-9575-7b9b1a7e0c11"}""")]
    [InlineData("""{"state":"Complete","durationCategory":"SixMonths"}""")]
    [InlineData("""{"state":"Complete","startDate":"2027-01-01"}""")]
    [InlineData("""{"state":"Complete","version":3}""")]
    [InlineData("""{"status":"NeedsReassigning","reassignTo":"8f14e45f-ceea-467a-9575-7b9b1a7e0c11"}""")]
    [InlineData("""{"isActive":1}""")]
    [InlineData("""{"isActive":true}""")]
    public void An_edit_naming_anything_else_is_malformed(string body)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RelationshipAttributesRequest>(body, Web));
    }
}

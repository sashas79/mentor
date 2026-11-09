using System.Text.Json;
using System.Text.Json.Serialization;
using Mentor.Api.Formation;
using Mentor.Core.Formation;

namespace Mentor.Tests.Unit.Formation;

// ADR-005, FR-071: a formation decision names its outcome and the content hash reviewed, and nothing else. No actor
// supplies the relationship's attributes, IsActive among them, its participants, its goals or the decision's time.
public sealed class FormationBodiesTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Theory]
    [InlineData("Approved", FormationOutcome.Approved)]
    [InlineData("Rejected", FormationOutcome.Rejected)]
    public void A_decision_names_its_outcome_and_the_content_hash_reviewed(string outcome, FormationOutcome expected)
    {
        Assert.Equal(new FormationDecisionRequest(expected, "ab12"),
            JsonSerializer.Deserialize<FormationDecisionRequest>($$"""{"outcome":"{{outcome}}","contentHash":"ab12"}""", Web));
    }

    [Theory]
    [InlineData("""{"outcome":"Approved","contentHash":"ab12","isActive":"Y"}""")]
    [InlineData("""{"outcome":"Approved","contentHash":"ab12","state":"OnHold"}""")]
    [InlineData("""{"outcome":"Approved","contentHash":"ab12","status":"NeedsReassigning"}""")]
    [InlineData("""{"outcome":"Approved","contentHash":"ab12","administratorEmployeeId":"8f14e45f-ceea-467a-9575-7b9b1a7e0c11"}""")]
    [InlineData("""{"outcome":"Approved","contentHash":"ab12","mentorEmployeeId":"8f14e45f-ceea-467a-9575-7b9b1a7e0c11"}""")]
    [InlineData("""{"outcome":"Approved","contentHash":"ab12","goals":["Another goal"]}""")]
    [InlineData("""{"outcome":"Approved","contentHash":"ab12","decidedAt":"2026-01-01T00:00:00Z"}""")]
    [InlineData("""{"outcome":"Formed","contentHash":"ab12"}""")]
    public void A_decision_naming_anything_else_is_malformed(string body)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<FormationDecisionRequest>(body, Web));
    }
}

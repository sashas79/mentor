using System.Text.Json;
using System.Text.Json.Serialization;
using Mentor.Api.Goals;
using Mentor.Core.Goals;

namespace Mentor.Tests.Unit.Goals;

// BR-005, FR-059, DEC-008, AC-E2E-08: an entry states its progress update and nothing else, and a decision is Approved or
// Rejected and nothing else: no meeting notes, no reason, no wording, status, date or person reaches the application
// through these bodies, and the views have no field for any of them.
public sealed class GoalBodiesTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public void An_entry_states_its_progress_update()
    {
        Assert.Equal(new GoalProgressRequest("Drafted the review checklist"),
            JsonSerializer.Deserialize<GoalProgressRequest>("""{"progressUpdate":"Drafted the review checklist"}""", Web));
    }

    [Theory]
    [InlineData("""{"progressUpdate":"Drafted","notes":"What we discussed"}""")]
    [InlineData("""{"progressUpdate":"Drafted","meetingNotes":"What we discussed"}""")]
    [InlineData("""{"progressUpdate":"Drafted","discussion":"What we discussed"}""")]
    [InlineData("""{"progressUpdate":"Drafted","completionRequested":true}""")]
    [InlineData("""{"progressUpdate":"Drafted","completionStatus":"Complete"}""")]
    [InlineData("""{"progressUpdate":"Drafted","agreedDescription":"Lead every design review"}""")]
    [InlineData("""{"progressUpdate":"Drafted","submittedAt":"2026-01-01T00:00:00Z"}""")]
    [InlineData("""{"progressUpdate":"Drafted","protegeEmployeeId":"8f14e45f-ceea-467a-9575-7b9b1a7e0c11"}""")]
    [InlineData("""{"progressUpdate":"Drafted","goalId":"8f14e45f-ceea-467a-9575-7b9b1a7e0c11"}""")]
    public void An_entry_naming_anything_else_is_malformed(string body)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<GoalProgressRequest>(body, Web));
    }

    [Theory]
    [InlineData("Approved", GoalDecision.Approved)]
    [InlineData("Rejected", GoalDecision.Rejected)]
    public void A_decision_is_approved_or_rejected(string decision, GoalDecision expected)
    {
        Assert.Equal(new GoalDecisionRequest(expected), JsonSerializer.Deserialize<GoalDecisionRequest>($$"""{"decision":"{{decision}}"}""", Web));
    }

    // AC-FR-037-02, DEC-008: no reason, comment or other value goes with a decision, and Pending is no decision.
    [Theory]
    [InlineData("""{"decision":"Rejected","reason":"Not yet"}""")]
    [InlineData("""{"decision":"Rejected","rejectionReason":"Not yet"}""")]
    [InlineData("""{"decision":"Approved","comment":"Well done"}""")]
    [InlineData("""{"decision":"Approved","mentorEmployeeId":"8f14e45f-ceea-467a-9575-7b9b1a7e0c11"}""")]
    [InlineData("""{"decision":"Approved","decidedAt":"2026-01-01T00:00:00Z"}""")]
    [InlineData("""{"decision":"Approved","completionStatus":"Complete"}""")]
    [InlineData("""{"decision":"Pending"}""")]
    [InlineData("""{"decision":"Complete"}""")]
    public void A_decision_naming_anything_else_is_malformed(string body)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<GoalDecisionRequest>(body, Web));
    }

    // AC-E2E-08, AC-FR-066-01, AC-FR-037-02: the views carry structured goals and progress only, with no field for meeting
    // discussion content and no rejection reason.
    [Fact]
    public void The_views_hold_structured_goals_and_progress_only()
    {
        Assert.Equal(
            ["AgreedDescription", "CompletionStatus", "CreatedAt", "FormedAgreementId", "Id", "PendingCompletionRequestId", "Position", "Progress", "RelationshipId", "UpdatedAt"],
            PropertiesOf<GoalView>());
        Assert.Equal(
            ["Completion", "CompletionRequested", "Decision", "Id", "ProgressUpdate", "ProtegeEmployeeId", "SubmittedAt"],
            PropertiesOf<GoalProgressView>());
        Assert.Equal(["DecidedAt", "Decision", "Id", "MentorEmployeeId"], PropertiesOf<GoalDecisionView>());
    }

    private static string[] PropertiesOf<T>() =>
        [.. typeof(T).GetProperties().Select(property => property.Name).Where(name => name != "EqualityContract").Order(StringComparer.Ordinal)];
}

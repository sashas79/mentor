using System.Text.Json;
using Mentor.Api.Requests;

namespace Mentor.Tests.Unit.Requests;

// AC-FR-014-01, BR-021: a request names no protégé and a selection names nothing but the mentor; a body that names more
// is malformed, not quietly stripped.
public sealed class PairingBodiesTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void A_submission_names_the_three_statements_alone()
    {
        var submission = JsonSerializer.Deserialize<MentorshipRequestSubmission>(
            """{"motivation":"To grow","developmentGoals":"Lead a design review","nontechnicalExpectations":"Confidence"}""", Web);

        Assert.Equal(new MentorshipRequestSubmission("To grow", "Lead a design review", "Confidence"), submission);
    }

    [Theory]
    [InlineData("""{"motivation":"To grow","protegeEmployeeId":"8f14e45f-ceea-467a-9575-7b9b1a7e0c11"}""")]
    [InlineData("""{"motivation":"To grow","disposition":"Formed"}""")]
    [InlineData("""{"motivation":"To grow","submittedAt":"2020-01-01T00:00:00Z"}""")]
    [InlineData("""{"motivation":"To grow","ProtegeEmployeeId":"8f14e45f-ceea-467a-9575-7b9b1a7e0c11"}""")]
    public void A_submission_that_names_anything_else_is_malformed(string body)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<MentorshipRequestSubmission>(body, Web));
    }

    [Fact]
    public void A_selection_names_the_mentor_alone()
    {
        Assert.Equal(
            new CandidateSelection(Guid.Parse("8f14e45f-ceea-467a-9575-7b9b1a7e0c11")),
            JsonSerializer.Deserialize<CandidateSelection>("""{"mentorEmployeeId":"8f14e45f-ceea-467a-9575-7b9b1a7e0c11"}""", Web));
    }

    [Theory]
    [InlineData("""{"mentorEmployeeId":"8f14e45f-ceea-467a-9575-7b9b1a7e0c11","protegeEmployeeId":"0f14e45f-ceea-467a-9575-7b9b1a7e0c11"}""")]
    [InlineData("""{"mentorEmployeeId":"8f14e45f-ceea-467a-9575-7b9b1a7e0c11","status":"Drafting"}""")]
    [InlineData("""{"mentorEmployeeId":"8f14e45f-ceea-467a-9575-7b9b1a7e0c11","mentorResponse":"Accepted"}""")]
    public void A_selection_that_names_anything_else_is_malformed(string body)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CandidateSelection>(body, Web));
    }
}

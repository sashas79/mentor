using Mentor.Core.Requests;

namespace Mentor.Tests.Unit.Requests;

// FR-014, FR-015, FR-016, DR-003: the protégé's motivation, their technical and career goals and, apart from those, what
// they expect beyond technical skills.
public sealed class RequestDetailsTests
{
    [Fact]
    public void Submitted_statements_are_trimmed()
    {
        Assert.True(RequestDetails.TryCreate(" To grow ", "  Lead a design review ", " Confidence ", out var details, out var errors));

        Assert.Empty(errors);
        Assert.Equal(new RequestDetails("To grow", "Lead a design review", "Confidence"), details);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Every_statement_is_required(string? blank)
    {
        Assert.False(RequestDetails.TryCreate(blank, "Goals", "Expectations", out _, out var motivation));
        Assert.False(RequestDetails.TryCreate("Motivation", blank, "Expectations", out _, out var goals));
        Assert.False(RequestDetails.TryCreate("Motivation", "Goals", blank, out var details, out var expectations));

        Assert.Null(details);
        Assert.Equal(["Motivation"], motivation.Keys.ToArray());
        Assert.Equal(["DevelopmentGoals"], goals.Keys.ToArray());
        Assert.Equal(["NontechnicalExpectations"], expectations.Keys.ToArray());
    }

    // NFR-002: a refusal never repeats what was submitted.
    [Fact]
    public void Overlong_statements_are_refused_without_being_repeated()
    {
        var motivation = new string('m', RequestDetails.MaxLength + 1);
        var goals = new string('g', RequestDetails.MaxLength + 1);
        var expectations = new string('e', RequestDetails.MaxLength + 1);

        Assert.False(RequestDetails.TryCreate(motivation, goals, expectations, out _, out var errors));

        Assert.Equal(["DevelopmentGoals", "Motivation", "NontechnicalExpectations"], errors.Keys.Order().ToArray());
        Assert.DoesNotContain(errors.Values.SelectMany(messages => messages), message =>
            message.Contains(motivation) || message.Contains(goals) || message.Contains(expectations));
        Assert.True(RequestDetails.TryCreate(motivation[..^1], goals[..^1], expectations[..^1], out _, out _));
    }

    // AC-FR-016-01, AC-FR-014-01: the expectations are held apart from the goals, and nothing a protégé states names the
    // protégé, the date or the disposition.
    [Fact]
    public void The_statements_are_three_separate_values_and_name_no_protege_date_or_disposition()
    {
        Assert.Equal(
            ["DevelopmentGoals", "Motivation", "NontechnicalExpectations"],
            typeof(RequestDetails).GetProperties().Select(property => property.Name).Order().ToArray());
    }
}

using Mentor.Core.Goals;

namespace Mentor.Tests.Unit.Goals;

// FR-035, FR-036, FR-059, DR-010: a progress entry, and a completion request with it, states the protégé's progress update
// and nothing else; the goal, the protégé, the date and the completion flag come from elsewhere.
public sealed class ProgressSubmissionTests
{
    [Fact]
    public void The_update_is_trimmed()
    {
        Assert.True(ProgressSubmission.TryCreate("  Drafted the review checklist  ", out var submission, out var errors));

        Assert.Empty(errors);
        Assert.Equal(new ProgressSubmission("Drafted the review checklist"), submission);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void The_update_is_required(string? blank)
    {
        Assert.False(ProgressSubmission.TryCreate(blank, out var submission, out var errors));

        Assert.Null(submission);
        Assert.Equal(["progressUpdate"], errors.Keys.ToArray());
    }

    // The update is plain text: a control character other than a line break or a tab is refused, NUL included, which the
    // database could not store; line breaks and tabs are kept.
    [Theory]
    [InlineData("Ran the review\u0000")]
    [InlineData("Ran the\u0007 review")]
    [InlineData("Ran the review\u001b[0m")]
    public void An_update_holding_a_control_character_is_refused(string update)
    {
        Assert.False(ProgressSubmission.TryCreate(update, out var submission, out var errors));

        Assert.Null(submission);
        Assert.Equal(["progressUpdate"], errors.Keys.ToArray());
        Assert.True(ProgressSubmission.TryCreate("Ran the review\r\nwith\tthe team\nand wrote it up", out var multiline, out _));
        Assert.Equal("Ran the review\r\nwith\tthe team\nand wrote it up", multiline.ProgressUpdate);
    }

    // NFR-002: a refusal never repeats what was submitted.
    [Fact]
    public void An_overlong_update_is_refused_without_being_repeated()
    {
        var overlong = new string('u', ProgressSubmission.MaxLength + 1);

        Assert.False(ProgressSubmission.TryCreate(overlong, out _, out var errors));

        Assert.Equal(["progressUpdate"], errors.Keys.ToArray());
        Assert.DoesNotContain(errors.Values.SelectMany(messages => messages), message => message.Contains(overlong[..100], StringComparison.Ordinal));
        Assert.True(ProgressSubmission.TryCreate(overlong[..^1], out var longest, out _));
        Assert.Equal(ProgressSubmission.MaxLength, longest.ProgressUpdate.Length);
    }

    // AC-DR-010-01, FR-059: the update is the only value an entry states; nothing holds meeting notes, a date or a person.
    [Fact]
    public void The_update_is_the_only_value_a_submission_states()
    {
        Assert.Equal(["ProgressUpdate"], typeof(ProgressSubmission).GetProperties().Select(property => property.Name).ToArray());
    }
}

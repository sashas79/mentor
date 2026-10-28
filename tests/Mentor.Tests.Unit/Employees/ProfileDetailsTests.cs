using Mentor.Core.Employees;

namespace Mentor.Tests.Unit.Employees;

public sealed class ProfileDetailsTests
{
    [Fact]
    public void Submitted_values_are_trimmed_and_a_blank_level_means_none()
    {
        Assert.True(ProfileDetails.TryCreate("  Naledi Zulu ", "   ", out var details, out var errors));
        Assert.Empty(errors);
        Assert.Equal(new ProfileDetails("Naledi Zulu", null), details);

        Assert.True(ProfileDetails.TryCreate("Naledi Zulu", " Senior ", out details, out _));
        Assert.Equal("Senior", details.LevelTitle);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_display_name_is_required(string? displayName)
    {
        Assert.False(ProfileDetails.TryCreate(displayName, "Senior", out var details, out var errors));

        Assert.Null(details);
        Assert.Equal(["DisplayName"], errors.Keys.ToArray());
    }

    // NFR-002: a refusal never repeats what was submitted.
    [Fact]
    public void Overlong_values_are_refused_without_being_repeated()
    {
        var name = new string('n', ProfileDetails.DisplayNameMaxLength + 1);
        var level = new string('l', ProfileDetails.LevelTitleMaxLength + 1);

        Assert.False(ProfileDetails.TryCreate(name, level, out _, out var errors));

        Assert.Equal(["DisplayName", "LevelTitle"], errors.Keys.Order().ToArray());
        Assert.DoesNotContain(errors.Values.SelectMany(messages => messages), message => message.Contains(name) || message.Contains(level));
        Assert.True(ProfileDetails.TryCreate(name[..^1], level[..^1], out _, out _));
    }

    // FR-006: what an employee edits has no identity key to change.
    [Fact]
    public void The_details_carry_no_identity_key()
    {
        Assert.Equal(["DisplayName", "LevelTitle"], typeof(ProfileDetails).GetProperties().Select(property => property.Name).Order().ToArray());
    }

    // ADR-002: the identity seeds the display name and nothing else.
    [Fact]
    public void An_identity_seeds_its_display_name_and_no_level()
    {
        Assert.True(ProfileDetails.TrySeed(" Naledi Zulu ", out var details));

        Assert.Equal(new ProfileDetails("Naledi Zulu", null), details);
    }

    // DR-001: a seed is held to the rule an edit is held to, so no record ever carries a name a save would refuse, and
    // nothing is invented for an identity whose name is absent or over-long.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_identity_whose_display_name_a_save_would_refuse_seeds_nothing(string? name)
    {
        name ??= new string('n', ProfileDetails.DisplayNameMaxLength + 1);

        Assert.False(ProfileDetails.TrySeed(name, out var details));

        Assert.Null(details);
        Assert.False(ProfileDetails.TryCreate(name, null, out _, out _));
    }
}

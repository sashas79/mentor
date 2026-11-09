using Mentor.Core.MentorRegistry;

namespace Mentor.Tests.Unit.MentorRegistry;

// FR-010, FR-011, DR-002: the biography and the three separately captured attributes an applicant or mentor maintains.
public sealed class MentorProfileDetailsTests
{
    [Fact]
    public void Submitted_values_are_trimmed_and_a_blank_attribute_means_none()
    {
        Assert.True(MentorProfileDetails.TryCreate("  Ten years of delivery ", "   ", " Fintech ", null, out var details, out var errors));
        Assert.Empty(errors);
        Assert.Equal(new MentorProfileDetails("Ten years of delivery", null, "Fintech", null), details);

        Assert.True(MentorProfileDetails.TryCreate("Ten years of delivery", " Principal Engineer ", "", " Two graduates ", out details, out _));
        Assert.Equal(new MentorProfileDetails("Ten years of delivery", "Principal Engineer", null, "Two graduates"), details);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_biography_is_required(string? biography)
    {
        Assert.False(MentorProfileDetails.TryCreate(biography, "Principal Engineer", "Fintech", "Two graduates", out var details, out var errors));

        Assert.Null(details);
        Assert.Equal(["Biography"], errors.Keys.ToArray());
    }

    // NFR-002: a refusal never repeats what was submitted.
    [Fact]
    public void Overlong_values_are_refused_without_being_repeated()
    {
        var biography = new string('b', MentorProfileDetails.BiographyMaxLength + 1);
        var role = new string('r', MentorProfileDetails.RoleTitleMaxLength + 1);
        var industry = new string('i', MentorProfileDetails.IndustryExperienceMaxLength + 1);
        var mentoring = new string('m', MentorProfileDetails.MentoringExperienceMaxLength + 1);

        Assert.False(MentorProfileDetails.TryCreate(biography, role, industry, mentoring, out _, out var errors));

        Assert.Equal(["Biography", "IndustryExperience", "MentoringExperience", "RoleTitle"], errors.Keys.Order().ToArray());
        Assert.DoesNotContain(errors.Values.SelectMany(messages => messages), message =>
            message.Contains(biography) || message.Contains(role) || message.Contains(industry) || message.Contains(mentoring));
        Assert.True(MentorProfileDetails.TryCreate(biography[..^1], role[..^1], industry[..^1], mentoring[..^1], out _, out _));
    }

    // AC-FR-011-01, FR-008: role/title, industry experience and mentoring experience are told apart, and the details
    // carry no approval and no employee to change.
    [Fact]
    public void The_details_distinguish_the_three_attributes_and_carry_no_approval_or_employee()
    {
        Assert.Equal(
            ["Biography", "IndustryExperience", "MentoringExperience", "RoleTitle"],
            typeof(MentorProfileDetails).GetProperties().Select(property => property.Name).Order().ToArray());
    }
}

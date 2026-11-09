using System.Diagnostics.CodeAnalysis;

namespace Mentor.Core.MentorRegistry;

/// <summary>
/// What an applicant or mentor maintains on their own mentor profile (FR-010, FR-011, DR-002): a concise biography,
/// which is required, and the separately captured role/title, industry experience and mentoring experience. The
/// approval and its decision evidence are not among them and cannot be: the profile's owner never reaches them (FR-008).
/// </summary>
public sealed record MentorProfileDetails(string Biography, string? RoleTitle, string? IndustryExperience, string? MentoringExperience)
{
    public const int BiographyMaxLength = 2000;

    public const int RoleTitleMaxLength = 100;

    public const int IndustryExperienceMaxLength = 2000;

    public const int MentoringExperienceMaxLength = 2000;

    /// <summary>
    /// Trims the submitted values and validates them: a biography is required, and a blank attribute means none. The
    /// errors are keyed by property and empty when the details are valid; no message repeats what was submitted.
    /// </summary>
    public static bool TryCreate(
        string? biography,
        string? roleTitle,
        string? industryExperience,
        string? mentoringExperience,
        [NotNullWhen(true)] out MentorProfileDetails? details,
        out IReadOnlyDictionary<string, string[]> errors)
    {
        var failures = new Dictionary<string, string[]>();
        var text = biography?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            failures[nameof(Biography)] = ["A biography is required."];
        }
        else if (text.Length > BiographyMaxLength)
        {
            failures[nameof(Biography)] = [$"A biography is at most {BiographyMaxLength} characters."];
        }

        var role = Optional(roleTitle, nameof(RoleTitle), RoleTitleMaxLength, "A role or title", failures);
        var industry = Optional(industryExperience, nameof(IndustryExperience), IndustryExperienceMaxLength, "Industry experience", failures);
        var mentoring = Optional(mentoringExperience, nameof(MentoringExperience), MentoringExperienceMaxLength, "Mentoring experience", failures);

        errors = failures;
        details = failures.Count == 0 ? new MentorProfileDetails(text, role, industry, mentoring) : null;
        return details is not null;
    }

    private static string? Optional(string? submitted, string property, int maxLength, string what, Dictionary<string, string[]> failures)
    {
        var value = string.IsNullOrWhiteSpace(submitted) ? null : submitted.Trim();
        if (value?.Length > maxLength)
        {
            failures[property] = [$"{what} is at most {maxLength} characters."];
        }

        return value;
    }
}

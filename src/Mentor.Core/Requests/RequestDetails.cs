using System.Diagnostics.CodeAnalysis;

namespace Mentor.Core.Requests;

/// <summary>
/// What a protégé states in a mentorship request (FR-014, FR-015, FR-016, DR-003): their motivation for seeking
/// mentorship, their technical and career development goals and, kept apart from those, what they expect beyond
/// technical skills. All three are required. The protégé, the submission date and the disposition are not among them:
/// authentication, the database and the workflow supply those.
/// </summary>
public sealed record RequestDetails(string Motivation, string DevelopmentGoals, string NontechnicalExpectations)
{
    public const int MaxLength = 4000;

    /// <summary>
    /// Trims the submitted values and validates them. The errors are keyed by property and empty when the details are
    /// valid; no message repeats what was submitted.
    /// </summary>
    public static bool TryCreate(
        string? motivation,
        string? developmentGoals,
        string? nontechnicalExpectations,
        [NotNullWhen(true)] out RequestDetails? details,
        out IReadOnlyDictionary<string, string[]> errors)
    {
        var failures = new Dictionary<string, string[]>();
        var motive = Required(motivation, nameof(Motivation), "A motivation", failures);
        var goals = Required(developmentGoals, nameof(DevelopmentGoals), "Technical and career development goals", failures);
        var expectations = Required(nontechnicalExpectations, nameof(NontechnicalExpectations), "Expectations beyond technical skills", failures);

        errors = failures;
        details = failures.Count == 0 ? new RequestDetails(motive, goals, expectations) : null;
        return details is not null;
    }

    private static string Required(string? submitted, string property, string what, Dictionary<string, string[]> failures)
    {
        var value = submitted?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            failures[property] = [$"{what} must be stated."];
        }
        else if (value.Length > MaxLength)
        {
            failures[property] = [$"{what} must be at most {MaxLength} characters."];
        }

        return value;
    }
}

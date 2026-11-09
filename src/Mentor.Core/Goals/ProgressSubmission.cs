using System.Diagnostics.CodeAnalysis;

namespace Mentor.Core.Goals;

/// <summary>
/// What the protégé states in a progress entry (FR-035, FR-036, DR-010): their progress update against the goal, which a
/// completion request carries too. It is required. The goal, the protégé, the date and whether the entry asks for
/// completion are not among it: the route, authentication, the database and the action taken supply those. Nothing else
/// is taken, so no field for meeting discussion content exists (FR-059).
/// </summary>
public sealed record ProgressSubmission(string ProgressUpdate)
{
    public const int MaxLength = 4000;

    /// <summary>
    /// Trims the submitted update and validates it: it is plain text, so a control character other than a line break or a
    /// tab is refused, as agreement text refuses it. The errors are keyed by property and empty when the update is valid;
    /// no message repeats what was submitted.
    /// </summary>
    public static bool TryCreate(string? progressUpdate, [NotNullWhen(true)] out ProgressSubmission? submission, out IReadOnlyDictionary<string, string[]> errors)
    {
        var update = progressUpdate?.Trim() ?? string.Empty;
        var failures = new Dictionary<string, string[]>();
        if (update.Length == 0)
        {
            failures["progressUpdate"] = ["A progress update must be stated."];
        }
        else if (update.Any(character => char.IsControl(character) && character is not ('\n' or '\r' or '\t')))
        {
            failures["progressUpdate"] = ["A progress update must be plain text, without control characters."];
        }
        else if (update.Length > MaxLength)
        {
            failures["progressUpdate"] = [$"A progress update must be at most {MaxLength} characters."];
        }

        errors = failures;
        submission = failures.Count == 0 ? new ProgressSubmission(update) : null;
        return submission is not null;
    }
}

using System.Diagnostics.CodeAnalysis;

namespace Mentor.Core.Employees;

/// <summary>
/// The display attributes an employee maintains on their own application profile (FR-005, BR-015, DR-001): the name
/// the register shows and an optional seniority level or title. The Entra object identifier is not among them and
/// cannot be: profile editing never reaches the identity key (FR-006).
/// </summary>
public sealed record ProfileDetails(string DisplayName, string? LevelTitle)
{
    public const int DisplayNameMaxLength = 200;

    public const int LevelTitleMaxLength = 100;

    /// <summary>
    /// Trims the submitted values and validates them: a display name is required, and a blank level/title means none.
    /// The errors are keyed by property and empty when the details are valid; no message repeats what was submitted.
    /// </summary>
    public static bool TryCreate(
        string? displayName,
        string? levelTitle,
        [NotNullWhen(true)] out ProfileDetails? details,
        out IReadOnlyDictionary<string, string[]> errors)
    {
        var name = displayName?.Trim() ?? string.Empty;
        var level = string.IsNullOrWhiteSpace(levelTitle) ? null : levelTitle.Trim();
        var failures = new Dictionary<string, string[]>();
        if (name.Length == 0)
        {
            failures[nameof(DisplayName)] = ["A display name is required."];
        }
        else if (name.Length > DisplayNameMaxLength)
        {
            failures[nameof(DisplayName)] = [$"A display name is at most {DisplayNameMaxLength} characters."];
        }

        if (level?.Length > LevelTitleMaxLength)
        {
            failures[nameof(LevelTitle)] = [$"A level or title is at most {LevelTitleMaxLength} characters."];
        }

        errors = failures;
        details = failures.Count == 0 ? new ProfileDetails(name, level) : null;
        return details is not null;
    }

    /// <summary>
    /// What an authenticated identity seeds a new record with (ADR-002): the display name the token or roster carries
    /// and no level/title. The employee maintains both from then on. The seed is held to the rule an edit is held to,
    /// so a record never carries a name a save would refuse (DR-001): an identity whose name is absent (a token issued
    /// without the profile scope) or over-long seeds nothing, and nothing is invented in its place.
    /// </summary>
    public static bool TrySeed(string identityDisplayName, [NotNullWhen(true)] out ProfileDetails? details) =>
        TryCreate(identityDisplayName, levelTitle: null, out details, out _);
}

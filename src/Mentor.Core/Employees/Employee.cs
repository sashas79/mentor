namespace Mentor.Core.Employees;

/// <summary>
/// An employee known to the application (DR-001), keyed by the Entra object identifier. A historic person who could
/// not be resolved against the directory has no employee record (DR-016).
/// </summary>
public sealed class Employee
{
    public Guid Id { get; init; }

    /// <summary>The Entra object identifier (oid claim). Profile edits never change it.</summary>
    public Guid EntraObjectId { get; init; }

    public required string DisplayName { get; set; }

    /// <summary>Optional seniority level or title, as the register shows it.</summary>
    public string? LevelTitle { get; set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }
}

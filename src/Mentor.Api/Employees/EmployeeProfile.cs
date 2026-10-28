using System.Text.Json.Serialization;
using Mentor.Core.Employees;

namespace Mentor.Api.Employees;

/// <summary>
/// An employee's application profile (DR-001): the record id and the Entra object identifier, which are read-only, and
/// the display attributes the employee maintains (<see cref="ProfileDetails"/>).
/// </summary>
public sealed record EmployeeProfile(
    Guid Id,
    Guid EntraObjectId,
    string DisplayName,
    string? LevelTitle,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static EmployeeProfile Of(Employee employee) =>
        new(employee.Id, employee.EntraObjectId, employee.DisplayName, employee.LevelTitle, employee.CreatedAt, employee.UpdatedAt);
}

/// <summary>
/// What a profile edit may carry (FR-005): the display attributes and nothing else. A body that names any other
/// member, the identity key or the record id among them, is refused as malformed rather than silently ignored
/// (FR-006), so no client can be left believing it changed the identity.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EmployeeProfileEdit(string? DisplayName, string? LevelTitle);

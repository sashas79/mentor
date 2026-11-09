using System.Text.Json.Serialization;
using Mentor.Core.Employees;
using Mentor.Core.MentorRegistry;

namespace Mentor.Api.MentorRegistry;

/// <summary>
/// A mentor application and profile as the API shows it (DR-002): the employee it belongs to, with the display name and
/// level/title of their employee profile, the biography, the separately captured role/title, industry experience and
/// mentoring experience (FR-011), and the approval with its decision evidence. Discovery shows approved profiles only
/// and nothing about a mentor's relationships (FR-009, BR-002).
/// </summary>
public sealed record MentorProfileView(
    Guid Id,
    Guid EmployeeId,
    string DisplayName,
    string? LevelTitle,
    string Biography,
    string? RoleTitle,
    string? IndustryExperience,
    string? MentoringExperience,
    MentorApprovalStatus ApprovalStatus,
    Guid? ApprovalDecidedByEmployeeId,
    DateTimeOffset? ApprovalDecidedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static MentorProfileView Of(MentorProfile profile, Employee employee) => new(
        profile.Id,
        profile.EmployeeId,
        employee.DisplayName,
        employee.LevelTitle,
        profile.Biography,
        profile.RoleTitle,
        profile.IndustryExperience,
        profile.MentoringExperience,
        profile.ApprovalStatus,
        profile.ApprovalDecidedByEmployeeId,
        profile.ApprovalDecidedAt,
        profile.CreatedAt,
        profile.UpdatedAt);
}

/// <summary>
/// What an application or a profile edit may carry (FR-007, FR-010): the biography and attributes of
/// <see cref="MentorProfileDetails"/> and nothing else. A body that names any other member, the approval or the employee
/// among them, is refused as malformed rather than silently ignored, so no applicant can be left believing they
/// approved themselves (FR-008).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MentorProfileEdit(string? Biography, string? RoleTitle, string? IndustryExperience, string? MentoringExperience);

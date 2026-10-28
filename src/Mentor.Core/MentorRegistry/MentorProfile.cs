namespace Mentor.Core.MentorRegistry;

public enum MentorApprovalStatus
{
    Pending,
    Approved,
    Declined,
}

/// <summary>
/// An employee's mentor application and profile (DR-002). Only an approved profile is discoverable; there is no
/// capacity cap and no availability flag (BR-002).
/// </summary>
public sealed class MentorProfile
{
    public Guid Id { get; init; }

    public Guid EmployeeId { get; init; }

    public required string Biography { get; set; }

    public string? RoleTitle { get; set; }

    public string? IndustryExperience { get; set; }

    public string? MentoringExperience { get; set; }

    public MentorApprovalStatus ApprovalStatus { get; set; }

    /// <summary>The Talent Management administrator who approved or declined the application.</summary>
    public Guid? ApprovalDecidedByEmployeeId { get; set; }

    public DateTimeOffset? ApprovalDecidedAt { get; set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }
}

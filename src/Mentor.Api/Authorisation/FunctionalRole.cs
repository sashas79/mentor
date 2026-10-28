using Mentor.Core.MentorRegistry;

namespace Mentor.Api.Authorisation;

/// <summary>
/// The actors of the authorisation matrix (ADR-003, FR-003). An IT or system operator holds none of them by virtue of
/// that function: operating the environment grants no application permission.
/// </summary>
public enum FunctionalRole
{
    /// <summary>Any authenticated employee, who is also the protégé of their own requests.</summary>
    Employee,

    /// <summary>An employee with a mentor application that is not approved.</summary>
    Applicant,

    /// <summary>An approved mentor.</summary>
    Mentor,

    /// <summary>A Talent Management administrator, by Entra app-role assignment.</summary>
    TalentManagement,
}

public static class FunctionalRoles
{
    /// <summary>
    /// The roles that follow from the app role and the caller's mentor application. Every caller is an employee, so an
    /// approved mentor or an administrator can also act as a protégé (AC-FR-003-02).
    /// </summary>
    public static IReadOnlySet<FunctionalRole> Resolve(bool talentManagementAppRole, MentorApprovalStatus? mentorApplication)
    {
        var roles = new HashSet<FunctionalRole> { FunctionalRole.Employee };
        switch (mentorApplication)
        {
            case MentorApprovalStatus.Approved:
                roles.Add(FunctionalRole.Mentor);
                break;
            case MentorApprovalStatus.Pending or MentorApprovalStatus.Declined:
                roles.Add(FunctionalRole.Applicant);
                break;
        }

        if (talentManagementAppRole)
        {
            roles.Add(FunctionalRole.TalentManagement);
        }

        return roles;
    }
}

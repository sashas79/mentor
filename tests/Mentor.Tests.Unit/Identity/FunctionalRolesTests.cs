using Mentor.Api.Authorisation;
using Mentor.Core.MentorRegistry;

namespace Mentor.Tests.Unit.Identity;

public sealed class FunctionalRolesTests
{
    [Theory]
    [InlineData(false, null, new[] { FunctionalRole.Employee })]
    [InlineData(false, MentorApprovalStatus.Pending, new[] { FunctionalRole.Employee, FunctionalRole.Applicant })]
    [InlineData(false, MentorApprovalStatus.Declined, new[] { FunctionalRole.Employee, FunctionalRole.Applicant })]
    [InlineData(false, MentorApprovalStatus.Approved, new[] { FunctionalRole.Employee, FunctionalRole.Mentor })]
    [InlineData(true, null, new[] { FunctionalRole.Employee, FunctionalRole.TalentManagement })]
    [InlineData(true, MentorApprovalStatus.Approved, new[] { FunctionalRole.Employee, FunctionalRole.Mentor, FunctionalRole.TalentManagement })]
    public void Roles_follow_from_the_app_role_and_the_mentor_application(bool appRole, MentorApprovalStatus? application, FunctionalRole[] expected)
    {
        Assert.Equal(expected.Order(), FunctionalRoles.Resolve(appRole, application).Order());
    }

    // AC-FR-003-02: no role replaces Employee, so every caller can still act as a protégé.
    [Fact]
    public void Every_caller_is_an_employee()
    {
        foreach (var appRole in new[] { false, true })
        {
            foreach (var application in Enum.GetValues<MentorApprovalStatus>().Cast<MentorApprovalStatus?>().Append(null))
            {
                Assert.Contains(FunctionalRole.Employee, FunctionalRoles.Resolve(appRole, application));
            }
        }
    }
}

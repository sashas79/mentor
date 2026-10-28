using Mentor.Api.Authorisation;
using Mentor.Api.Identity;
using Mentor.Core.Audit;
using Mentor.Core.MentorRegistry;
using Mentor.Tests.Integration.Support;

namespace Mentor.Tests.Integration.Authorisation;

// The matrix grants exactly what ADR-003 grants and denies every other actor/resource/action combination (FR-003,
// FR-004, NFR-009); MatrixEnforcementTests shows the server enforcing the same decisions over HTTP.
public sealed class AuthorisationMatrixTests
{
    private static readonly Guid Self = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    [Fact]
    public void The_matrix_holds_exactly_the_permissions_ADR_003_states()
    {
        Assert.Equal(ExpectedMatrix.Permissions.Order(), AuthorisationMatrix.All.Select(permission => permission.Name).Order());
        Assert.Equal(AuthorisationMatrix.All.Count, AuthorisationMatrix.All.Select(permission => permission.Name).Distinct().Count());
    }

    [Fact]
    public void Every_caller_reaches_exactly_the_actions_ADR_003_grants_their_role()
    {
        var mismatches =
            from permission in AuthorisationMatrix.All
            from profile in Enum.GetValues<Profile>()
            let expected = ExpectedMatrix.ReachesEndpoint(permission.Name, profile)
            where permission.Admits(CallerOf(profile), record: null) != expected
            select $"{profile} {(expected ? "is refused" : "reaches")} {permission.Name}";

        Assert.Empty(mismatches);
    }

    [Fact]
    public void Every_action_on_a_record_is_admitted_exactly_where_ADR_003_admits_it_and_denied_everywhere_else()
    {
        var mismatches =
            from permission in AuthorisationMatrix.All
            from profile in Enum.GetValues<Profile>()
            from standing in ExpectedMatrix.StandingsOf(profile)
            let expected = ExpectedMatrix.Admits(permission.Name, profile, standing)
            where permission.Admits(CallerOf(profile), RecordFor(standing)) != expected
            select $"{profile} as {standing} {(expected ? "is refused" : "is admitted to")} {permission.Name}";

        Assert.Empty(mismatches);
    }

    // AC-BR-022-02: historical relationships are entered only through the Talent Management database role (ADR-008);
    // no caller of the application, an operator included, has any way to enter one.
    [Fact]
    public void No_permission_enters_or_validates_a_historical_relationship()
    {
        Assert.DoesNotContain(AuthorisationMatrix.All, permission => permission.Name.Contains("legacy", StringComparison.OrdinalIgnoreCase));
    }

    // AC-FR-006-01: no action changes the identity key; profile updates are display attributes only.
    [Fact]
    public void No_permission_changes_an_employee_identity()
    {
        var employeeActions = AuthorisationMatrix.All.Where(permission => permission.Name.StartsWith("employee-profile.", StringComparison.Ordinal));

        Assert.Equal(["employee-profile.read", "employee-profile.update"], employeeActions.Select(permission => permission.Name).Order());
    }

    private static Caller CallerOf(Profile profile) => new(
        Guid.NewGuid(),
        profile.ToString(),
        IdentityScheme.Entra,
        profile == Profile.Newcomer ? null : Self,
        FunctionalRoles.Resolve(
            profile == Profile.Administrator,
            profile switch
            {
                Profile.Applicant => MentorApprovalStatus.Pending,
                Profile.Mentor => MentorApprovalStatus.Approved,
                _ => null,
            }));

    private static ProtectedRecord RecordFor(Standing standing) => new()
    {
        Owner = standing == Standing.Owner ? Self : Other,
        Protege = standing == Standing.Protege ? Self : Other,
        NamedMentors = new HashSet<Guid> { standing == Standing.NamedMentor ? Self : Other },
        Discoverable = standing == Standing.Discoverable,
    };
}

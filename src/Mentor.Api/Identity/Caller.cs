using System.Security.Claims;
using Mentor.Api.Authorisation;
using Mentor.Core.Audit;
using Mentor.Core.MentorRegistry;
using Mentor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Mentor.Api.Identity;

/// <summary>
/// Who is calling (COMP-IDENTITY-ACCESS): the authenticated identity, the employee record its Entra object identifier
/// keys, and the functional roles that follow. The employee is found by object identifier alone, never by name, so a
/// changed display name or two employees of one name cannot move a record to someone else (FR-002, FR-006). A caller
/// without an employee record yet is an employee who owns nothing.
/// </summary>
public sealed record Caller(Guid ObjectId, string DisplayName, IdentityScheme Scheme, Guid? EmployeeId, IReadOnlySet<FunctionalRole> Roles)
{
    public bool Holds(FunctionalRole role) => Roles.Contains(role);
}

/// <summary>Resolves the caller of the current request once, from its principal and the authoritative store.</summary>
public sealed class CallerResolver(MentorDbContext db)
{
    private ClaimsPrincipal? resolvedPrincipal;
    private Caller? resolved;

    /// <summary>The caller, or none when the principal was not established by a Mentor identity scheme.</summary>
    public async Task<Caller?> ResolveAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (ReferenceEquals(principal, resolvedPrincipal))
        {
            return resolved;
        }

        resolved = MentorPrincipal.IdentityOf(principal) is { } identity ? await ResolveAsync(identity, cancellationToken) : null;
        resolvedPrincipal = principal;
        return resolved;
    }

    private async Task<Caller> ResolveAsync(AuthenticatedIdentity identity, CancellationToken cancellationToken)
    {
        var employee = await db.Employees.AsNoTracking()
            .Where(employee => employee.EntraObjectId == identity.ObjectId)
            .Select(employee => new
            {
                employee.Id,
                MentorApplication = db.MentorProfiles
                    .Where(profile => profile.EmployeeId == employee.Id)
                    .Select(profile => (MentorApprovalStatus?)profile.ApprovalStatus)
                    .SingleOrDefault(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        return new Caller(
            identity.ObjectId,
            identity.DisplayName,
            identity.Scheme,
            employee?.Id,
            FunctionalRoles.Resolve(identity.TalentManagementAppRole, employee?.MentorApplication));
    }
}

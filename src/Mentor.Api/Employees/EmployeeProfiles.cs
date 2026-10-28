using System.Security.Claims;
using Mentor.Api.Authorisation;
using Mentor.Api.Identity;
using Mentor.Core.Employees;
using Mentor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Mentor.Api.Employees;

/// <summary>The outcome of looking a profile up on behalf of a caller: the profile, or why there is none.</summary>
public sealed record ProfileLookup(EmployeeProfile? Profile, bool Exists)
{
    public static readonly ProfileLookup NotFound = new(null, false);

    public static readonly ProfileLookup Forbidden = new(null, true);
}

/// <summary>
/// The outcome of reading the caller's own profile: the profile; none because the caller is refused; or none because
/// there is no record yet and the identity's display name cannot seed one (<see cref="ProfileDetails.TrySeed"/>), so
/// only a save of the caller's own details creates it.
/// </summary>
public sealed record OwnProfileLookup(EmployeeProfile? Profile, bool AwaitsDetails)
{
    public static readonly OwnProfileLookup Forbidden = new(null, AwaitsDetails: false);

    public static readonly OwnProfileLookup AwaitingDetails = new(null, AwaitsDetails: true);

    public static OwnProfileLookup Of(Employee employee) => new(EmployeeProfile.Of(employee), AwaitsDetails: false);
}

/// <summary>
/// The employee profile module (COMP-EMPLOYEE-PROFILE, ADR-014): the Employee record keyed by the Entra object
/// identifier, created on the caller's first authenticated access to their profile and maintained by the employee
/// alone, with no Profile Creator, Data Lake or other employee-profile integration. Every read and every change is
/// checked against the authorisation matrix (ADR-003). Edits reach the display attributes only: the identity key and
/// every record that references the employee are untouched (FR-002, FR-006), and nothing here creates a record for
/// anyone but the authenticated caller (DR-001, DR-016).
/// </summary>
public sealed class EmployeeProfiles(MentorDbContext db, CallerResolver callers, IAuthorizationService authorization)
{
    /// <summary>
    /// The caller's own profile. The first access creates the record, keyed by the caller's object identifier and
    /// seeded with the display name their identity carries (ADR-002); from then on the application's record is
    /// authoritative for the display attributes. The seed is validated as an edit is, so a record never holds a name a
    /// save would refuse (DR-001): when the identity's name cannot seed one, nothing is created or invented, and the
    /// outcome says that the caller's own save has to create the record. Forbidden only when the caller is not a
    /// Mentor identity.
    /// </summary>
    public async Task<OwnProfileLookup> ReadOwnAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (await callers.ResolveAsync(user, cancellationToken) is not { } caller)
        {
            return OwnProfileLookup.Forbidden;
        }

        if (caller.EmployeeId is not { } id)
        {
            if (!ProfileDetails.TrySeed(caller.DisplayName, out var seed))
            {
                return OwnProfileLookup.AwaitingDetails;
            }

            var (created, _) = await CreateOwnAsync(caller, seed, cancellationToken);
            return OwnProfileLookup.Of(created);
        }

        if (!await PermittedAsync(user, AuthorisationMatrix.EmployeeProfile.Read, id))
        {
            return OwnProfileLookup.Forbidden;
        }

        return OwnProfileLookup.Of(await db.Employees.AsNoTracking().SingleAsync(row => row.Id == id, cancellationToken));
    }

    /// <summary>
    /// Saves the caller's display attributes, creating the record when this is their first access. Nothing in the
    /// details can name the identity key. None only when the caller may not update the record.
    /// </summary>
    public async Task<EmployeeProfile?> SaveOwnAsync(ClaimsPrincipal user, ProfileDetails details, CancellationToken cancellationToken)
    {
        if (await callers.ResolveAsync(user, cancellationToken) is not { } caller)
        {
            return null;
        }

        Employee employee;
        if (caller.EmployeeId is { } id)
        {
            if (!await PermittedAsync(user, AuthorisationMatrix.EmployeeProfile.Update, id))
            {
                return null;
            }

            employee = await db.Employees.SingleAsync(row => row.Id == id, cancellationToken);
        }
        else
        {
            bool created;
            (employee, created) = await CreateOwnAsync(caller, details, cancellationToken);
            if (created)
            {
                return EmployeeProfile.Of(employee);
            }
        }

        employee.DisplayName = details.DisplayName;
        employee.LevelTitle = details.LevelTitle;
        await db.SaveChangesAsync(cancellationToken);
        return EmployeeProfile.Of(employee);
    }

    /// <summary>
    /// A profile by record id: its owner's or, for Talent Management, anyone's. A caller who may not read it learns
    /// nothing, not even whether it exists.
    /// </summary>
    public async Task<ProfileLookup> ReadAsync(ClaimsPrincipal user, Guid id, CancellationToken cancellationToken)
    {
        if (!await PermittedAsync(user, AuthorisationMatrix.EmployeeProfile.Read, id))
        {
            return ProfileLookup.Forbidden;
        }

        var employee = await db.Employees.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        return employee is null ? ProfileLookup.NotFound : new ProfileLookup(EmployeeProfile.Of(employee), Exists: true);
    }

    // An employee record is checked before it is read: the matrix needs nothing of it but its id, which is its owner
    // (ProtectedRecord.Of(Employee)), so the check never has to load the row first.
    private Task<bool> PermittedAsync(ClaimsPrincipal user, Permission permission, Guid employeeId) =>
        authorization.IsPermittedAsync(user, permission, new ProtectedRecord { Owner = employeeId });

    // The record is the caller's own by construction: its key is their object identifier, which only authentication
    // supplies. Two first accesses racing each other end with that one record, because the unique index refuses the
    // second insert and the loser adopts the row the winner created.
    private async Task<(Employee Employee, bool Created)> CreateOwnAsync(Caller caller, ProfileDetails details, CancellationToken cancellationToken)
    {
        var employee = new Employee { EntraObjectId = caller.ObjectId, DisplayName = details.DisplayName, LevelTitle = details.LevelTitle };
        db.Employees.Add(employee);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return (employee, true);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(employee).State = EntityState.Detached;
            return (await db.Employees.SingleAsync(row => row.EntraObjectId == caller.ObjectId, cancellationToken), false);
        }
    }
}

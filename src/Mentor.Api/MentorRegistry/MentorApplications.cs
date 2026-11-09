using System.Security.Claims;
using Mentor.Api.Audit;
using Mentor.Api.Authorisation;
using Mentor.Api.Identity;
using Mentor.Api.Notifications;
using Mentor.Core.Audit;
using Mentor.Core.MentorRegistry;
using Mentor.Core.Notifications;
using Mentor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Mentor.Api.MentorRegistry;

/// <summary>Why an operation on a mentor application or profile yielded no profile.</summary>
public enum MentorRegistryRefusal
{
    None,

    /// <summary>The matrix refuses the caller the action, on this record or at all.</summary>
    Forbidden,

    /// <summary>No such profile; for the caller's own, no application yet.</summary>
    NotFound,

    /// <summary>The caller has no employee record yet, so nothing can be attributed to them; their employee profile creates it.</summary>
    NoEmployeeRecord,

    /// <summary>The caller has applied already: one application per employee (ADR-005).</summary>
    AlreadyApplied,

    /// <summary>The application has been approved or declined already; a decision is taken once.</summary>
    AlreadyDecided,
}

/// <summary>The outcome of an operation: the profile as the operation leaves it, or why there is none.</summary>
public sealed record MentorRegistryOutcome(MentorProfileView? Profile, MentorRegistryRefusal Refusal)
{
    public static readonly MentorRegistryOutcome Forbidden = new(null, MentorRegistryRefusal.Forbidden);

    public static readonly MentorRegistryOutcome NotFound = new(null, MentorRegistryRefusal.NotFound);

    public static readonly MentorRegistryOutcome NoEmployeeRecord = new(null, MentorRegistryRefusal.NoEmployeeRecord);

    public static readonly MentorRegistryOutcome AlreadyApplied = new(null, MentorRegistryRefusal.AlreadyApplied);

    public static readonly MentorRegistryOutcome AlreadyDecided = new(null, MentorRegistryRefusal.AlreadyDecided);

    public static MentorRegistryOutcome Of(MentorProfileView profile) => new(profile, MentorRegistryRefusal.None);
}

/// <summary>
/// The mentor registry (COMP-MENTOR-REGISTRY): the application any authenticated employee makes once and maintains
/// (FR-007, FR-010), the Talent Management decision that makes it discoverable (FR-008), and discovery, a plain query
/// over approved profiles with no capacity cap, availability flag, relationship count or ranking (FR-009, FR-013,
/// BR-002, ADR-014). Every read and change is checked against the authorisation matrix (ADR-003); the decision is
/// written with its audit entry and the applicant's notification in one transaction (ADR-007, ADR-009).
/// </summary>
public sealed class MentorApplications(MentorDbContext db, CallerResolver callers, IAuthorizationService authorization, AuditTrail audit, NotificationEvents notifications)
{
    /// <summary>
    /// The caller's own application, pending until Talent Management decides it. Refused for a caller with no employee
    /// record yet: the record is created by their own employee profile (COMP-EMPLOYEE-PROFILE), never here, so every
    /// application is attributed to one.
    /// </summary>
    public async Task<MentorRegistryOutcome> ApplyAsync(ClaimsPrincipal user, MentorProfileDetails details, CancellationToken cancellationToken)
    {
        if (await callers.ResolveAsync(user, cancellationToken) is not { } caller)
        {
            return MentorRegistryOutcome.Forbidden;
        }

        if (caller.EmployeeId is not { } employee)
        {
            return MentorRegistryOutcome.NoEmployeeRecord;
        }

        if (!await authorization.IsPermittedAsync(user, AuthorisationMatrix.MentorApplication.Create, new ProtectedRecord { Owner = employee }))
        {
            return MentorRegistryOutcome.Forbidden;
        }

        if (await db.MentorProfiles.AnyAsync(profile => profile.EmployeeId == employee, cancellationToken))
        {
            return MentorRegistryOutcome.AlreadyApplied;
        }

        var profile = new MentorProfile
        {
            EmployeeId = employee,
            Biography = details.Biography,
            RoleTitle = details.RoleTitle,
            IndustryExperience = details.IndustryExperience,
            MentoringExperience = details.MentoringExperience,
            ApprovalStatus = MentorApprovalStatus.Pending,
        };
        db.MentorProfiles.Add(profile);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two applications racing each other: the unique index on employee_id admits one (ADR-005).
            return MentorRegistryOutcome.AlreadyApplied;
        }

        return MentorRegistryOutcome.Of(await ViewAsync(profile, cancellationToken));
    }

    /// <summary>The caller's own application and profile; none when they have not applied.</summary>
    public async Task<MentorRegistryOutcome> ReadOwnAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (await callers.ResolveAsync(user, cancellationToken) is not { } caller)
        {
            return MentorRegistryOutcome.Forbidden;
        }

        if (caller.EmployeeId is not { } employee)
        {
            return MentorRegistryOutcome.NotFound;
        }

        var profile = await db.MentorProfiles.AsNoTracking().SingleOrDefaultAsync(profile => profile.EmployeeId == employee, cancellationToken);
        if (profile is null)
        {
            return MentorRegistryOutcome.NotFound;
        }

        if (!await authorization.IsPermittedAsync(user, AuthorisationMatrix.MentorProfile.Read, ProtectedRecord.Of(profile)))
        {
            return MentorRegistryOutcome.Forbidden;
        }

        return MentorRegistryOutcome.Of(await ViewAsync(profile, cancellationToken));
    }

    /// <summary>
    /// A profile by id: its owner's; an approved one, which discovery shows, for any employee; any for Talent
    /// Management. A pending or declined profile is nobody else's to read (ADR-003).
    /// </summary>
    public async Task<MentorRegistryOutcome> ReadAsync(ClaimsPrincipal user, Guid id, CancellationToken cancellationToken)
    {
        var profile = await db.MentorProfiles.AsNoTracking().SingleOrDefaultAsync(profile => profile.Id == id, cancellationToken);
        if (profile is null)
        {
            return MentorRegistryOutcome.NotFound;
        }

        if (!await authorization.IsPermittedAsync(user, AuthorisationMatrix.MentorProfile.Read, ProtectedRecord.Of(profile)))
        {
            return MentorRegistryOutcome.Forbidden;
        }

        return MentorRegistryOutcome.Of(await ViewAsync(profile, cancellationToken));
    }

    /// <summary>Saves the caller's own biography and attributes. The approval and its evidence are not among them.</summary>
    public async Task<MentorRegistryOutcome> SaveOwnAsync(ClaimsPrincipal user, MentorProfileDetails details, CancellationToken cancellationToken)
    {
        if (await callers.ResolveAsync(user, cancellationToken) is not { EmployeeId: { } employee })
        {
            return MentorRegistryOutcome.Forbidden;
        }

        var profile = await db.MentorProfiles.SingleOrDefaultAsync(profile => profile.EmployeeId == employee, cancellationToken);
        if (profile is null)
        {
            return MentorRegistryOutcome.NotFound;
        }

        if (!await authorization.IsPermittedAsync(user, AuthorisationMatrix.MentorProfile.Update, ProtectedRecord.Of(profile)))
        {
            return MentorRegistryOutcome.Forbidden;
        }

        profile.Biography = details.Biography;
        profile.RoleTitle = details.RoleTitle;
        profile.IndustryExperience = details.IndustryExperience;
        profile.MentoringExperience = details.MentoringExperience;
        await db.SaveChangesAsync(cancellationToken);
        return MentorRegistryOutcome.Of(await ViewAsync(profile, cancellationToken));
    }

    /// <summary>
    /// Every approved mentor with their profile, by display name (FR-009, FR-012): no pending or declined profile, no
    /// capacity cap, no availability flag, no relationship count and no score or ranking of any kind (FR-013, BR-002).
    /// </summary>
    public Task<IReadOnlyList<MentorProfileView>> DiscoverAsync(CancellationToken cancellationToken) =>
        ListAsync(MentorApprovalStatus.Approved, byDisplayName: true, cancellationToken);

    /// <summary>The applications awaiting a Talent Management decision, oldest first.</summary>
    public Task<IReadOnlyList<MentorProfileView>> PendingAsync(CancellationToken cancellationToken) =>
        ListAsync(MentorApprovalStatus.Pending, byDisplayName: false, cancellationToken);

    /// <summary>
    /// Approves or declines a pending application (FR-008): the decision, the deciding administrator and the server
    /// time go on the profile, the entry goes to the audit trail and the applicant is notified, in one transaction. A
    /// decision is taken once: an application decided already, even an instant earlier by another administrator, is
    /// refused and nothing is written.
    /// </summary>
    public async Task<MentorRegistryOutcome> DecideAsync(ClaimsPrincipal user, Guid id, MentorApprovalStatus decision, CancellationToken cancellationToken)
    {
        if (decision == MentorApprovalStatus.Pending)
        {
            throw new ArgumentOutOfRangeException(nameof(decision), decision, "A decision approves or declines the application.");
        }

        if (await callers.ResolveAsync(user, cancellationToken) is not { } caller)
        {
            return MentorRegistryOutcome.Forbidden;
        }

        if (caller.EmployeeId is not { } administrator)
        {
            return MentorRegistryOutcome.NoEmployeeRecord;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var profile = await db.MentorProfiles.AsNoTracking().SingleOrDefaultAsync(profile => profile.Id == id, cancellationToken);
        if (profile is null)
        {
            return MentorRegistryOutcome.NotFound;
        }

        if (!await authorization.IsPermittedAsync(user, AuthorisationMatrix.MentorApplication.Decide, ProtectedRecord.Of(profile)))
        {
            return MentorRegistryOutcome.Forbidden;
        }

        if (profile.ApprovalStatus != MentorApprovalStatus.Pending)
        {
            return MentorRegistryOutcome.AlreadyDecided;
        }

        // The status guard in the statement, not the read above, settles a race between two administrators: the
        // second statement finds no pending row and changes nothing. The database clock supplies the time (ADR-013).
        var decided = await db.MentorProfiles
            .Where(row => row.Id == id && row.ApprovalStatus == MentorApprovalStatus.Pending)
            .ExecuteUpdateAsync(set => set
                .SetProperty(row => row.ApprovalStatus, decision)
                .SetProperty(row => row.ApprovalDecidedByEmployeeId, administrator)
                .SetProperty(row => row.ApprovalDecidedAt, _ => DateTimeOffset.UtcNow), cancellationToken);
        if (decided == 0)
        {
            return MentorRegistryOutcome.AlreadyDecided;
        }

        profile = await db.MentorProfiles.AsNoTracking().SingleAsync(row => row.Id == id, cancellationToken);
        audit.Append(caller, new AuditEvent(AuditAction.MentorApplicationDecided, AuditRecordType.MentorProfile, profile.Id)
        {
            PriorValues = new AuditValues()
                .Set("approvalStatus", MentorApprovalStatus.Pending)
                .Set("approvalDecidedByEmployeeId", (Guid?)null)
                .Set("approvalDecidedAt", (DateTimeOffset?)null),
            NewValues = new AuditValues()
                .Set("approvalStatus", profile.ApprovalStatus)
                .Set("approvalDecidedByEmployeeId", profile.ApprovalDecidedByEmployeeId)
                .Set("approvalDecidedAt", profile.ApprovalDecidedAt),
            Outcome = decision == MentorApprovalStatus.Approved ? AuditOutcome.Approved : AuditOutcome.Declined,
        });
        notifications.Raise(WorkflowNotifications.MentorApplicationDecided(profile));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return MentorRegistryOutcome.Of(await ViewAsync(profile, cancellationToken));
    }

    private async Task<IReadOnlyList<MentorProfileView>> ListAsync(MentorApprovalStatus status, bool byDisplayName, CancellationToken cancellationToken)
    {
        var rows = db.MentorProfiles.AsNoTracking()
            .Where(profile => profile.ApprovalStatus == status)
            .Join(db.Employees, profile => profile.EmployeeId, employee => employee.Id, (profile, employee) => new { profile, employee });
        var ordered = byDisplayName
            ? rows.OrderBy(row => row.employee.DisplayName).ThenBy(row => row.profile.Id)
            : rows.OrderBy(row => row.profile.CreatedAt).ThenBy(row => row.profile.Id);
        return [.. (await ordered.ToListAsync(cancellationToken)).Select(row => MentorProfileView.Of(row.profile, row.employee))];
    }

    private async Task<MentorProfileView> ViewAsync(MentorProfile profile, CancellationToken cancellationToken)
    {
        var employee = await db.Employees.AsNoTracking().SingleAsync(employee => employee.Id == profile.EmployeeId, cancellationToken);
        return MentorProfileView.Of(profile, employee);
    }
}

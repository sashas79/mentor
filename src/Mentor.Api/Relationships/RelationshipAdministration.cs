using System.Security.Claims;
using Mentor.Api.Audit;
using Mentor.Api.Authorisation;
using Mentor.Api.Identity;
using Mentor.Core.Audit;
using Mentor.Core.Legacy;
using Mentor.Core.Relationships;
using Mentor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Mentor.Api.Relationships;

/// <summary>
/// Formed relationship administration (COMP-RELATIONSHIP-ADMIN, ADR-005, ADR-006). The parties read their own
/// relationships and Talent Management reads any (ADR-003). A Talent Management administrator edits State, Status and
/// IsActive, any subset at once, by a versioned relationship command: the version claim, then each named attribute whose
/// value differs is written and audited on its own with its prior and new value, the administrator and the server time
/// (FR-051). Nothing is derived, defaulted or refused for a combination; no edit touches the participants, the provenance,
/// the formation evidence or a candidate, raises a notification or starts a reassignment; and since an edit needs an
/// existing relationship, it can neither form nor activate a candidate.
/// </summary>
public sealed class RelationshipAdministration(MentorDbContext db, CallerResolver callers, IAuthorizationService authorization, AuditTrail audit)
{
    /// <summary>The caller's own relationships, as protégé or mentor, oldest first; any number involving the same person (FR-054).</summary>
    public async Task<IReadOnlyList<RelationshipView>> ListOwnAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (await callers.ResolveAsync(user, cancellationToken) is not { EmployeeId: { } employee })
        {
            return [];
        }

        var readable = new List<RelationshipView>();
        var own = db.MentorshipRelationships.Where(relationship => relationship.ProtegeEmployeeId == employee || relationship.MentorEmployeeId == employee);
        foreach (var (relationship, view) in await WithNamesAsync(own, cancellationToken))
        {
            if (await authorization.IsPermittedAsync(user, AuthorisationMatrix.Relationship.Read, ProtectedRecord.Of(relationship)))
            {
                readable.Add(view);
            }
        }

        return readable;
    }

    public async Task<RelationshipResult<RelationshipView>> ReadAsync(ClaimsPrincipal user, Guid relationshipId, CancellationToken cancellationToken)
    {
        var relationship = await db.MentorshipRelationships.AsNoTracking().SingleOrDefaultAsync(row => row.Id == relationshipId, cancellationToken);
        if (relationship is null)
        {
            return RelationshipResult<RelationshipView>.Refused(RelationshipRefusal.NotFound);
        }

        return await authorization.IsPermittedAsync(user, AuthorisationMatrix.Relationship.Read, ProtectedRecord.Of(relationship))
            ? RelationshipResult<RelationshipView>.Of(await ViewAsync(relationshipId, cancellationToken))
            : RelationshipResult<RelationshipView>.Refused(RelationshipRefusal.Forbidden);
    }

    /// <summary>
    /// A Talent Management administrator's edit of the relationship's attributes on the version they read (FR-047 to
    /// FR-050). An edit that changes nothing is refused, and nothing is written.
    /// </summary>
    public async Task<RelationshipResult<RelationshipView>> EditAsync(
        ClaimsPrincipal user, Guid relationshipId, long? version, RelationshipEdit edit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var relationship = await db.MentorshipRelationships.AsNoTracking().SingleOrDefaultAsync(row => row.Id == relationshipId, cancellationToken);
        if (relationship is null)
        {
            return RelationshipResult<RelationshipView>.Refused(RelationshipRefusal.NotFound);
        }

        if (!await authorization.IsPermittedAsync(user, AuthorisationMatrix.Relationship.UpdateAttributes, ProtectedRecord.Of(relationship))
            || await callers.ResolveAsync(user, cancellationToken) is not { } actor)
        {
            return RelationshipResult<RelationshipView>.Refused(RelationshipRefusal.Forbidden);
        }

        // Each change is attributed to the administrator's employee record (FR-051).
        if (actor.EmployeeId is null)
        {
            return RelationshipResult<RelationshipView>.Refused(RelationshipRefusal.NoEmployeeRecord);
        }

        if (version is not { } decidedOn)
        {
            return RelationshipResult<RelationshipView>.Refused(RelationshipRefusal.VersionRequired);
        }

        var refusal = await CommitAsync(actor, relationshipId, decidedOn, edit, cancellationToken);
        return refusal == RelationshipRefusal.None
            ? RelationshipResult<RelationshipView>.Of(await ViewAsync(relationshipId, cancellationToken))
            : RelationshipResult<RelationshipView>.Refused(refusal);
    }

    /// <summary>
    /// The command protocol of ADR-005: one transaction whose first statement claims the version, then the edit on the row
    /// as that claim leaves it, one audit entry per changed attribute, committed together or not at all.
    /// </summary>
    private async Task<RelationshipRefusal> CommitAsync(Caller actor, Guid id, long decidedOn, RelationshipEdit edit, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await RelationshipVersion.ClaimAsync(db, id, decidedOn, cancellationToken))
        {
            return RelationshipRefusal.Stale;
        }

        // A refusal leaves the transaction uncommitted, so the claimed version rolls back with it.
        var current = await db.MentorshipRelationships.AsNoTracking().SingleAsync(row => row.Id == id, cancellationToken);
        var changes = edit.ChangesTo(current);
        if (changes.Count == 0)
        {
            return RelationshipRefusal.Unchanged;
        }

        var changed = changes.Select(change => change.Attribute).ToHashSet();
        await db.MentorshipRelationships.Where(row => row.Id == id).ExecuteUpdateAsync(set =>
        {
            if (changed.Contains(RelationshipAttribute.State))
            {
                set.SetProperty(row => row.State, edit.State);
            }

            if (changed.Contains(RelationshipAttribute.Status))
            {
                set.SetProperty(row => row.Status, edit.Status);
            }

            if (changed.Contains(RelationshipAttribute.IsActive))
            {
                set.SetProperty(row => row.IsActive, edit.IsActive);
            }
        }, cancellationToken);

        foreach (var change in changes)
        {
            audit.Append(actor, new AuditEvent(AuditAction.AttributeChanged, AuditRecordType.Relationship, id)
            {
                PriorValues = new AuditValues().Set(change.Field, change.Prior),
                NewValues = new AuditValues().Set(change.Field, change.New),
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return RelationshipRefusal.None;
    }

    private async Task<RelationshipView> ViewAsync(Guid id, CancellationToken cancellationToken) =>
        (await WithNamesAsync(db.MentorshipRelationships.Where(row => row.Id == id), cancellationToken)).Single().View;

    /// <summary>The relationships <paramref name="relationships"/> selects, oldest first, each with its participants' display names.</summary>
    private async Task<IReadOnlyList<(MentorshipRelationship Relationship, RelationshipView View)>> WithNamesAsync(
        IQueryable<MentorshipRelationship> relationships, CancellationToken cancellationToken)
    {
        var rows = await relationships.AsNoTracking()
            .Join(db.Employees, relationship => relationship.ProtegeEmployeeId, protege => protege.Id,
                (relationship, protege) => new { relationship, ProtegeName = protege.DisplayName })
            .Join(db.Employees, row => row.relationship.MentorEmployeeId, mentor => mentor.Id,
                (row, mentor) => new { row.relationship, row.ProtegeName, MentorName = mentor.DisplayName })
            .OrderBy(row => row.relationship.CreatedAt).ThenBy(row => row.relationship.Id)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(row => (row.relationship, ViewOf(row.relationship, row.ProtegeName, row.MentorName)))];
    }

    private static RelationshipView ViewOf(MentorshipRelationship relationship, string protegeName, string mentorName) => new(
        relationship.Id,
        relationship.Provenance,
        relationship.CandidateId,
        new RelationshipParticipantView(relationship.ProtegeEmployeeId, protegeName),
        new RelationshipParticipantView(relationship.MentorEmployeeId, mentorName),
        relationship.State,
        relationship.Status,
        relationship.IsActive,
        relationship.DurationCategory,
        relationship.StartDate,
        relationship.EndDate,
        relationship is { Provenance: Provenance.Legacy, SourceFile: { } file, SourceRow: { } row } ? new LegacySourceView(file, row) : null,
        relationship.CreatedAt,
        relationship.UpdatedAt,
        relationship.Version);
}

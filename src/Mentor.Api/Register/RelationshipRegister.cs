using System.Data;
using System.Globalization;
using System.Security.Claims;
using Mentor.Api.Audit;
using Mentor.Api.Goals;
using Mentor.Api.Identity;
using Mentor.Core.Audit;
using Mentor.Core.Goals;
using Mentor.Core.Relationships;
using Mentor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Mentor.Api.Register;

/// <summary>The export file: its bytes and the name it is offered under.</summary>
public sealed record RegisterExport(byte[] Content, string FileName);

/// <summary>Why an export was refused.</summary>
public enum RegisterRefusal
{
    None,

    /// <summary>The export is attributed to the caller's employee record in the trail, and they have none yet.</summary>
    NoEmployeeRecord,
}

/// <summary>
/// The relationship register and its reports (COMP-REGISTER-REPORTING, ADR-011). Both the register and its CSV export are
/// computed on demand by one query over the relationship, employee and legacy review exception tables, read as one
/// read-only snapshot. Nothing is stored, synchronised or imported: the tracker file remains provenance, never a second
/// source (FR-061). The register lists every relationship of either provenance and every held source row that has not
/// since been entered. Each export is audited as REGISTER_EXPORTED, attributed to the administrator who asked, before the
/// file is returned, so no file leaves without its entry. The administrator's goals and progress view reads every
/// relationship's structured goals in one snapshot as well. register.read and register.export belong to Talent
/// Management alone (ADR-003), so the endpoint check is the whole check, as it is for the audit trail.
/// </summary>
public sealed class RelationshipRegister(MentorDbContext db, CallerResolver callers, AuditTrail audit)
{
    /// <summary>The database refuses any write inside a view's transaction; a view only ever reads.</summary>
    private const string ReadOnlyTransaction = "SET TRANSACTION READ ONLY";

    public async Task<IReadOnlyList<RegisterEntryView>> ReadAsync(CancellationToken cancellationToken)
    {
        await using var snapshot = await BeginSnapshotAsync(cancellationToken);
        var entries = await EntriesAsync(cancellationToken);
        await snapshot.CommitAsync(cancellationToken);
        return entries;
    }

    /// <summary>
    /// The CSV export of the register as it reads now. It is audited as REGISTER_EXPORTED with its format and row counts,
    /// committed before the file is returned. The entry names no record and holds no row content.
    /// </summary>
    public async Task<(RegisterExport? Export, RegisterRefusal Refusal)> ExportAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (await callers.ResolveAsync(user, cancellationToken) is not { EmployeeId: not null } actor)
        {
            return (null, RegisterRefusal.NoEmployeeRecord);
        }

        IReadOnlyList<RegisterEntryView> entries;
        await using (var snapshot = await BeginSnapshotAsync(cancellationToken))
        {
            entries = await EntriesAsync(cancellationToken);
            await snapshot.CommitAsync(cancellationToken);
        }

        var content = RegisterCsv.Write(entries);
        var entry = audit.Append(actor, new AuditEvent(AuditAction.RegisterExported, AuditRecordType.Register, null)
        {
            NewValues = new AuditValues()
                .Set("format", "CSV")
                .Set("relationshipRows", entries.Count(row => row.Kind == RegisterEntryKind.Relationship))
                .Set("heldSourceRows", entries.Count(row => row.Kind == RegisterEntryKind.HeldSourceRow)),
        });
        await db.SaveChangesAsync(cancellationToken);

        var exportedAt = entry.OccurredAt.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        return (new RegisterExport(content, $"mentorship-register-{exportedAt}.csv"), RegisterRefusal.None);
    }

    /// <summary>
    /// Every relationship with its structured goals and progress, in register order (FR-040, FR-066). Talent Management
    /// reads here what each relationship's goal route shows it, completion status included, for all relationships at once.
    /// </summary>
    public async Task<IReadOnlyList<RelationshipGoalsView>> GoalsAsync(CancellationToken cancellationToken)
    {
        await using var snapshot = await BeginSnapshotAsync(cancellationToken);
        var relationships = await RelationshipsAsync(cancellationToken);
        var goals = await GoalViewReader.ReadAsync(db, _ => true, cancellationToken);
        await snapshot.CommitAsync(cancellationToken);

        var goalsOf = goals.ToLookup(goal => goal.RelationshipId);
        var ordered = RegisterEntries.Ordered(relationships.Select(row => (row.Entry, row.Relationship.CreatedAt)));
        var relationshipOf = relationships.ToDictionary(row => row.Relationship.Id, row => row.Relationship);
        return [.. ordered.Select(entry =>
        {
            var relationship = relationshipOf[entry.RelationshipId!.Value];
            var ofRelationship = goalsOf[relationship.Id].ToList();
            return new RelationshipGoalsView(
                relationship.Id,
                relationship.Provenance,
                entry.Mentor!,
                entry.Protege!,
                relationship.State,
                relationship.Status,
                relationship.IsActive,
                SummaryOf(ofRelationship),
                ofRelationship);
        })];
    }

    internal static GoalSummaryView SummaryOf(IReadOnlyCollection<GoalView> goals) => new(
        goals.Count,
        goals.Count(goal => goal.CompletionStatus == GoalCompletionStatus.Complete),
        goals.Count(goal => goal.CompletionStatus == GoalCompletionStatus.Incomplete),
        goals.Count(goal => goal.PendingCompletionRequestId is not null));

    /// <summary>One read-only snapshot (REPEATABLE READ), so that the relationships, the people and the held rows describe one moment.</summary>
    private async Task<IDbContextTransaction> BeginSnapshotAsync(CancellationToken cancellationToken)
    {
        var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await db.Database.ExecuteSqlRawAsync(ReadOnlyTransaction, cancellationToken);
        return snapshot;
    }

    private async Task<IReadOnlyList<RegisterEntryView>> EntriesAsync(CancellationToken cancellationToken)
    {
        var relationships = await RelationshipsAsync(cancellationToken);

        // A held row stays listed until its source row is entered as a relationship; that relationship's row then stands for it.
        var held = await db.LegacyReviewExceptions.AsNoTracking()
            .Where(exception => exception.RelationshipId == null
                && !db.MentorshipRelationships.Any(relationship => relationship.SourceFile == exception.SourceFile && relationship.SourceRow == exception.SourceRow))
            .ToListAsync(cancellationToken);

        return RegisterEntries.Ordered(
            relationships.Select(row => (row.Entry, row.Relationship.CreatedAt))
                .Concat(held.Select(exception => (RegisterEntries.Held(exception), exception.CreatedAt))));
    }

    private async Task<List<(MentorshipRelationship Relationship, RegisterEntryView Entry)>> RelationshipsAsync(CancellationToken cancellationToken)
    {
        var rows = await (
                from relationship in db.MentorshipRelationships.AsNoTracking()
                join mentor in db.Employees on relationship.MentorEmployeeId equals mentor.Id
                join protege in db.Employees on relationship.ProtegeEmployeeId equals protege.Id
                select new { relationship, MentorName = mentor.DisplayName, MentorLevel = mentor.LevelTitle, ProtegeName = protege.DisplayName, ProtegeLevel = protege.LevelTitle })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(row => (row.relationship, RegisterEntries.Of(
            row.relationship,
            new RegisterPersonView(row.relationship.MentorEmployeeId, row.MentorName, row.MentorLevel),
            new RegisterPersonView(row.relationship.ProtegeEmployeeId, row.ProtegeName, row.ProtegeLevel))))];
    }
}

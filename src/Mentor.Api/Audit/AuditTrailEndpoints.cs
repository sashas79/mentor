using System.Data;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Text.Json;
using Mentor.Api.Authorisation;
using Mentor.Core.Agreements;
using Mentor.Core.Audit;
using Mentor.Core.Formation;
using Mentor.Core.Requests;
using Mentor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Mentor.Api.Audit;

/// <summary>Who took the action: the employee the trail attributes it to, their name as it now reads, and how they were authenticated.</summary>
public sealed record AuditActorView(Guid EmployeeId, string DisplayName, IdentityScheme IdentityScheme);

/// <summary>
/// One entry of the trail as an administrator reads it (CONTRACT-AUDIT-ENTRY): the structured fields of the entry and
/// nothing else. <see cref="Provenance"/> tells an actual application event from a claim taken from the legacy source,
/// which names its source file and row.
/// </summary>
public sealed record AuditEntryView(
    Guid Id,
    DateTimeOffset OccurredAt,
    AuditActorView Actor,
    AuditAction Action,
    AuditRecordType RecordType,
    Guid? RecordId,
    JsonElement? PriorValues,
    JsonElement? NewValues,
    Guid? CandidateId,
    Guid? RevisionId,
    string? ContentHash,
    string? Outcome,
    IReadOnlyList<Guid>? InvalidatedAcceptanceIds,
    IReadOnlyList<Guid>? InvalidatedDecisionIds,
    AuditProvenance Provenance,
    string? SourceFile,
    int? SourceRow);

/// <summary>A revision of the candidate's draft by its identity: number, parent and content hash, never the content itself.</summary>
public sealed record RevisionEvidenceView(
    Guid Id,
    int RevisionNumber,
    Guid? ParentRevisionId,
    string ContentHash,
    Guid CreatedByEmployeeId,
    DateTimeOffset CreatedAt,
    bool IsCurrent);

/// <summary>An acceptance exactly as recorded, with the standing derived for it now (ADR-004).</summary>
public sealed record AcceptanceEvidenceView(
    Guid Id,
    Guid RevisionId,
    int RevisionNumber,
    string ContentHash,
    Guid ParticipantEmployeeId,
    DateTimeOffset AcceptedAt,
    Applicability Applicability);

/// <summary>A formation decision exactly as recorded, with the standing derived for it now; a superseded approval is never effective.</summary>
public sealed record DecisionEvidenceView(
    Guid Id,
    Guid RevisionId,
    int RevisionNumber,
    string ContentHash,
    FormationOutcome Outcome,
    Guid AdministratorEmployeeId,
    DateTimeOffset DecidedAt,
    Applicability Applicability);

/// <summary>
/// Everything an administrator needs to verify a candidate's current and superseded acceptances and decisions
/// (FR-028, FR-069): the revisions, every acceptance and decision with its applicability, and every entry of the trail
/// that names the candidate, as its record or as its decision context.
/// </summary>
public sealed record CandidateEvidenceView(
    Guid CandidateId,
    CandidateStatus Status,
    Guid ProtegeEmployeeId,
    Guid MentorEmployeeId,
    Guid? CurrentRevisionId,
    IReadOnlyList<RevisionEvidenceView> Revisions,
    IReadOnlyList<AcceptanceEvidenceView> Acceptances,
    IReadOnlyList<DecisionEvidenceView> Decisions,
    IReadOnlyList<AuditEntryView> Entries);

/// <summary>
/// The administrator's read-only view of the trail (ADR-007), filtered by record. audit-trail.read is Talent
/// Management's alone (ADR-003), so the endpoint check is the whole check; the candidate view still takes the record
/// check every action on an existing record takes. The candidate view is read in one read-only snapshot: the head its
/// applicability is derived from and every list it holds describe the same moment.
/// </summary>
internal static class AuditTrailEndpoints
{
    public const string RecordTypeRoute = "/api/audit-trail/records/{recordType}";
    public const string RecordRoute = "/api/audit-trail/records/{recordType}/{recordId:guid}";
    public const string CandidateRoute = "/api/audit-trail/candidates/{candidateId:guid}";

    /// <summary>The database refuses any write inside the view's transaction; the view only ever reads.</summary>
    private const string ReadOnlyTransaction = "SET TRANSACTION READ ONLY";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(RecordTypeRoute, async Task<Results<Ok<IReadOnlyList<AuditEntryView>>, BadRequest>> (
                AuditRecordType recordType, MentorDbContext db, CancellationToken cancellationToken) =>
            {
                if (!Enum.IsDefined(recordType))
                {
                    return TypedResults.BadRequest();
                }

                return TypedResults.Ok(await EntriesAsync(db, entry => entry.RecordType == recordType, cancellationToken));
            })
            .WithName("GetAuditEntriesOfRecordType")
            .RequirePermission(AuthorisationMatrix.AuditTrail.Read);

        endpoints.MapGet(RecordRoute, async Task<Results<Ok<IReadOnlyList<AuditEntryView>>, BadRequest>> (
                AuditRecordType recordType, Guid recordId, MentorDbContext db, CancellationToken cancellationToken) =>
            {
                if (!Enum.IsDefined(recordType))
                {
                    return TypedResults.BadRequest();
                }

                return TypedResults.Ok(await EntriesAsync(db, entry => entry.RecordType == recordType && entry.RecordId == recordId, cancellationToken));
            })
            .WithName("GetAuditEntriesOfRecord")
            .RequirePermission(AuthorisationMatrix.AuditTrail.Read);

        endpoints.MapGet(CandidateRoute, async Task<Results<Ok<CandidateEvidenceView>, NotFound, ForbidHttpResult>> (
                Guid candidateId, ClaimsPrincipal user, IAuthorizationService authorization, MentorDbContext db, CancellationToken cancellationToken) =>
            {
                // The head and every list come from one snapshot (REPEATABLE READ; ADR-013 fixes READ COMMITTED for
                // commands, and this is no command). A replacement that commits while the view is read, a new revision
                // made current and then evidence recorded on it, is seen whole or not at all: the applicability derived
                // from the head is the applicability of the evidence listed, and no acceptance or decision names a
                // revision the view lacks.
                await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
                await db.Database.ExecuteSqlRawAsync(ReadOnlyTransaction, cancellationToken);
                var candidate = await db.MentorSelections.AsNoTracking().SingleOrDefaultAsync(selection => selection.Id == candidateId, cancellationToken);
                if (candidate is null)
                {
                    return TypedResults.NotFound();
                }

                if (!await authorization.IsPermittedAsync(user, AuthorisationMatrix.AuditTrail.Read, ProtectedRecord.Of(candidate)))
                {
                    return TypedResults.Forbid();
                }

                var evidence = await CandidateEvidenceAsync(db, candidate, cancellationToken);
                await snapshot.CommitAsync(cancellationToken);
                return TypedResults.Ok(evidence);
            })
            .WithName("GetCandidateEvidence")
            .RequirePermission(AuthorisationMatrix.AuditTrail.Read);
    }

    /// <summary>
    /// The view of <paramref name="candidate"/>, the head as it was read in the caller's snapshot. Each acceptance and
    /// decision brings the number of the revision it names with it, so nothing is looked up in another list.
    /// </summary>
    private static async Task<CandidateEvidenceView> CandidateEvidenceAsync(MentorDbContext db, MentorSelection candidate, CancellationToken cancellationToken)
    {
        var revisions = await db.AgreementRevisions.AsNoTracking()
            .Where(revision => revision.CandidateId == candidate.Id)
            .OrderBy(revision => revision.RevisionNumber)
            .ToListAsync(cancellationToken);
        var acceptances = await db.AgreementAcceptances.AsNoTracking()
            .Join(db.AgreementRevisions, acceptance => acceptance.RevisionId, revision => revision.Id,
                (acceptance, revision) => new { acceptance, revision.CandidateId, revision.RevisionNumber })
            .Where(row => row.CandidateId == candidate.Id)
            .OrderBy(row => row.acceptance.AcceptedAt).ThenBy(row => row.acceptance.Id)
            .Select(row => new { row.acceptance, row.RevisionNumber })
            .ToListAsync(cancellationToken);
        var decisions = await db.FormationDecisions.AsNoTracking()
            .Where(decision => decision.CandidateId == candidate.Id)
            .Join(db.AgreementRevisions, decision => decision.RevisionId, revision => revision.Id,
                (decision, revision) => new { decision, revision.RevisionNumber })
            .OrderBy(row => row.decision.DecidedAt).ThenBy(row => row.decision.Id)
            .ToListAsync(cancellationToken);
        var entries = await EntriesAsync(db,
            entry => entry.CandidateId == candidate.Id || (entry.RecordType == AuditRecordType.Candidate && entry.RecordId == candidate.Id),
            cancellationToken);

        return new CandidateEvidenceView(
            candidate.Id,
            candidate.Status,
            candidate.ProtegeEmployeeId,
            candidate.MentorEmployeeId,
            candidate.CurrentRevisionId,
            [.. revisions.Select(revision => new RevisionEvidenceView(
                revision.Id, revision.RevisionNumber, revision.ParentRevisionId, revision.ContentHash, revision.CreatedByEmployeeId, revision.CreatedAt,
                revision.Id == candidate.CurrentRevisionId))],
            [.. acceptances.Select(row => new AcceptanceEvidenceView(
                row.acceptance.Id, row.acceptance.RevisionId, row.RevisionNumber, row.acceptance.ContentHash, row.acceptance.ParticipantEmployeeId, row.acceptance.AcceptedAt,
                ApplicabilityRule.Of(candidate.Status, candidate.CurrentRevisionId, row.acceptance.RevisionId)))],
            [.. decisions.Select(row => new DecisionEvidenceView(
                row.decision.Id, row.decision.RevisionId, row.RevisionNumber, row.decision.ContentHash, row.decision.Outcome, row.decision.AdministratorEmployeeId, row.decision.DecidedAt,
                ApplicabilityRule.Of(candidate.Status, candidate.CurrentRevisionId, row.decision.RevisionId)))],
            entries);
    }

    /// <summary>The entries <paramref name="about"/> selects, oldest first, each with its actor's name.</summary>
    private static async Task<IReadOnlyList<AuditEntryView>> EntriesAsync(MentorDbContext db, Expression<Func<AuditEntry, bool>> about, CancellationToken cancellationToken)
    {
        var rows = await db.AuditEntries.AsNoTracking()
            .Where(about)
            .Join(db.Employees, entry => entry.ActorEmployeeId, actor => actor.Id, (entry, actor) => new { entry, actor.DisplayName })
            .OrderBy(row => row.entry.OccurredAt).ThenBy(row => row.entry.Id)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(row => View(row.entry, row.DisplayName))];
    }

    private static AuditEntryView View(AuditEntry entry, string actorDisplayName) => new(
        entry.Id,
        entry.OccurredAt,
        new AuditActorView(entry.ActorEmployeeId, actorDisplayName, entry.IdentityScheme),
        entry.Action,
        entry.RecordType,
        entry.RecordId,
        Json(entry.PriorValues),
        Json(entry.NewValues),
        entry.CandidateId,
        entry.RevisionId,
        entry.ContentHash,
        entry.Outcome,
        entry.InvalidatedAcceptanceIds,
        entry.InvalidatedDecisionIds,
        entry.Provenance,
        entry.SourceFile,
        entry.SourceRow);

    private static JsonElement? Json(string? values) => values is null ? null : JsonSerializer.Deserialize<JsonElement>(values);
}

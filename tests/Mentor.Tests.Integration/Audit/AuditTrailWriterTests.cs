using System.Text.Json;
using Mentor.Api.Audit;
using Mentor.Core.Agreements;
using Mentor.Core.Audit;
using Mentor.Core.Relationships;
using Mentor.Tests.Integration.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Mentor.Tests.Integration.Audit;

// A business-critical action and its audit entry commit or roll back together, attributed to the true actor at the
// server's time (ADR-007; AC-DR-014-01, AC-FR-051-01, AC-FR-065-01, AC-NFR-004-01).
public sealed class AuditTrailWriterTests(PostgresServer postgres)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(IdentityScheme.Entra)]
    [InlineData(IdentityScheme.Simulated)]
    public async Task An_entry_commits_in_the_transaction_of_its_action_at_the_servers_time_of_that_transaction(IdentityScheme scheme)
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var mentorship = await new Seed(db).FormedMentorshipAsync();
        await using var context = db.Context(Login.App);
        var trail = new AuditTrail(context);

        AuditEntry entry;
        MentorshipRelationship relationship;
        await using (var command = await context.Database.BeginTransactionAsync(Cancellation))
        {
            relationship = await context.MentorshipRelationships.SingleAsync(row => row.Id == mentorship.Relationship, Cancellation);
            var prior = relationship.State;
            relationship.State = RelationshipState.OnHold;
            relationship.Version++;
            entry = trail.Append(Actors.Administrator(mentorship.Administrator, scheme), new AuditEvent(AuditAction.AttributeChanged, AuditRecordType.Relationship, relationship.Id)
            {
                PriorValues = new AuditValues().Set("state", prior),
                NewValues = new AuditValues().Set("state", relationship.State),
            });
            await context.SaveChangesAsync(Cancellation);
            await command.CommitAsync(Cancellation);
        }

        // One transaction has one time: the entry is dated exactly when the row changed.
        Assert.Equal(relationship.UpdatedAt, entry.OccurredAt);
        Assert.True(await db.ScalarAsync<bool>(Login.App, """
            SELECT entry.occurred_at = relationship.updated_at
            FROM mentor.audit_entry AS entry, mentor.mentorship_relationship AS relationship
            WHERE entry.id = $1 AND relationship.id = $2
            """, entry.Id, relationship.Id));

        await using var reader = db.Context(Login.App);
        var stored = await reader.AuditEntries.AsNoTracking().SingleAsync(Cancellation);
        Assert.Equal((mentorship.Administrator, scheme, AuditProvenance.Application), (stored.ActorEmployeeId, stored.IdentityScheme, stored.Provenance));
        Assert.Equal((AuditAction.AttributeChanged, AuditRecordType.Relationship, (Guid?)relationship.Id), (stored.Action, stored.RecordType, stored.RecordId));
        Assert.Equal("IN_PROGRESS", Field(stored.PriorValues, "state"));
        Assert.Equal("ON_HOLD", Field(stored.NewValues, "state"));
        Assert.Equal("ON_HOLD", await db.ScalarAsync<string>(Login.App, "SELECT state FROM mentor.mentorship_relationship WHERE id = $1", relationship.Id));
    }

    [Fact]
    public async Task An_action_the_database_refuses_leaves_no_entry_and_an_entry_the_database_refuses_leaves_no_action()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var mentorship = await new Seed(db).FormedMentorshipAsync();
        var administrator = Actors.Administrator(mentorship.Administrator);

        // The entry is saved first, then the action is refused: the transaction rolls back and takes the entry with it.
        await using (var context = db.Context(Login.App))
        await using (var command = await context.Database.BeginTransactionAsync(Cancellation))
        {
            new AuditTrail(context).Append(administrator, new AuditEvent(AuditAction.AttributeChanged, AuditRecordType.Relationship, mentorship.Relationship)
            {
                PriorValues = new AuditValues().Set("state", RelationshipState.InProgress),
                NewValues = new AuditValues().Set("state", "BOGUS"),
            });
            await context.SaveChangesAsync(Cancellation);
            await Rejected.WithAsync(PostgresErrorCodes.CheckViolation,
                () => context.Database.ExecuteSqlAsync($"UPDATE mentor.mentorship_relationship SET state = 'BOGUS' WHERE id = {mentorship.Relationship}", Cancellation),
                "ck_mentorship_relationship_state");
            await command.RollbackAsync(Cancellation);
        }

        // The action is tracked first, then the entry is refused (a hash that is not the revision's): one save, nothing persists.
        await using (var context = db.Context(Login.App))
        {
            var relationship = await context.MentorshipRelationships.SingleAsync(row => row.Id == mentorship.Relationship, Cancellation);
            relationship.State = RelationshipState.Complete;
            new AuditTrail(context).Append(administrator, new AuditEvent(AuditAction.AttributeChanged, AuditRecordType.Relationship, relationship.Id)
            {
                CandidateId = mentorship.Candidate,
                Revision = new RevisionReference(mentorship.Revision.Id, new string('0', 64)),
            });
            await Rejected.WithAsync(PostgresErrorCodes.ForeignKeyViolation, () => context.SaveChangesAsync(Cancellation), "fk_audit_entry_revision_content");
        }

        Assert.Equal(0L, await db.ScalarAsync<long>(Login.App, "SELECT count(*) FROM mentor.audit_entry"));
        Assert.Equal("IN_PROGRESS", await db.ScalarAsync<string>(Login.App, "SELECT state FROM mentor.mentorship_relationship WHERE id = $1", mentorship.Relationship));
    }

    // AC-NFR-004-01: the time of an entry is the server's, whatever a statement supplies, for application events and
    // legacy claims alike; nothing can record an invented event time.
    [Fact]
    public async Task An_entrys_time_is_the_servers_whatever_is_supplied()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var seed = new Seed(db);
        var administrator = await seed.EmployeeAsync("Alice");
        await seed.AuthoriseLegacyEntryAsync(Login.Alice, administrator);
        var invented = new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var application = await db.ScalarAsync<DateTime>(Login.App, """
            INSERT INTO mentor.audit_entry (id, actor_employee_id, identity_scheme, action, record_type, record_id, provenance, occurred_at)
            VALUES (gen_random_uuid(), $1, 'ENTRA', 'ATTRIBUTE_CHANGED', 'RELATIONSHIP', gen_random_uuid(), 'APPLICATION', $2)
            RETURNING occurred_at
            """, administrator, invented);
        var legacy = await db.ScalarAsync<DateTime>(Login.Alice, """
            INSERT INTO mentor.audit_entry (id, actor_employee_id, identity_scheme, action, record_type, record_id, provenance, source_file, source_row, occurred_at)
            VALUES (gen_random_uuid(), $1, 'DATABASE', 'LEGACY_ENTRY', 'RELATIONSHIP', gen_random_uuid(), 'LEGACY_SOURCE', $2, 8, $3)
            RETURNING occurred_at
            """, administrator, Seed.SourceFile, invented);

        Assert.True(application > invented.UtcDateTime.AddYears(20));
        Assert.True(legacy > invented.UtcDateTime.AddYears(20));
    }

    // AC-FR-069-01: a replacement names exactly the acceptances and decisions bound to the revision it replaces, which
    // stay on record unchanged.
    [Fact]
    public async Task A_replacement_names_the_acceptances_and_decisions_of_the_revision_it_replaces()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var seed = new Seed(db);
        var protege = await seed.EmployeeAsync("Protégé");
        var mentor = await seed.EmployeeAsync("Mentor");
        var administrator = await seed.EmployeeAsync("Talent Management");
        var candidate = await seed.CandidateAsync(await seed.RequestAsync(protege), protege, mentor);
        var first = await seed.RevisionAsync(candidate, protege);
        var protegeAccepted = await seed.AcceptanceAsync(first, protege);
        var mentorAccepted = await seed.AcceptanceAsync(first, mentor);
        var rejected = await seed.FormationDecisionAsync(candidate, first, administrator, "REJECTED");
        // Another candidate's evidence is nobody else's business.
        var otherProtege = await seed.EmployeeAsync("Other protégé");
        var other = await seed.CandidateAsync(await seed.RequestAsync(otherProtege), otherProtege, mentor);
        await seed.AcceptanceAsync(await seed.RevisionAsync(other, otherProtege), otherProtege);
        await using var context = db.Context(Login.App);
        var trail = new AuditTrail(context);

        var invalidated = await trail.InapplicableEvidenceAsync(first.Id, Cancellation);
        var second = await seed.RevisionAsync(candidate, mentor, number: 2, parent: first.Id);
        var entry = trail.Append(Actors.Employee(mentor), new AuditEvent(AuditAction.RevisionCreated, AuditRecordType.AgreementRevision, second.Id)
        {
            CandidateId = candidate,
            Revision = new RevisionReference(second.Id, second.ContentHash),
            Outcome = AuditOutcome.ResetToStep1,
            Invalidated = invalidated,
            PriorValues = new AuditValues().Set("currentRevisionId", first.Id),
            NewValues = new AuditValues().Set("currentRevisionId", second.Id),
        });
        await context.SaveChangesAsync(Cancellation);

        Assert.Equal([protegeAccepted, mentorAccepted], invalidated.AcceptanceIds);
        Assert.Equal([rejected], invalidated.DecisionIds);
        await using var reader = db.Context(Login.App);
        var stored = await reader.AuditEntries.AsNoTracking().SingleAsync(row => row.Id == entry.Id, Cancellation);
        Assert.Equal(new[] { protegeAccepted, mentorAccepted }, stored.InvalidatedAcceptanceIds);
        Assert.Equal(new[] { rejected }, stored.InvalidatedDecisionIds);
        Assert.Equal((second.Id, second.ContentHash, "RESET_TO_STEP_1"), (stored.RevisionId!.Value, stored.ContentHash, stored.Outcome));
        Assert.Equal(2L, await db.ScalarAsync<long>(Login.App, "SELECT count(*) FROM mentor.agreement_acceptance WHERE revision_id = $1", first.Id));
        Assert.Empty((await trail.InapplicableEvidenceAsync(null, Cancellation)).AcceptanceIds);
    }

    private static string? Field(string? values, string field) =>
        JsonSerializer.Deserialize<JsonElement>(values!).GetProperty(field).GetString();
}

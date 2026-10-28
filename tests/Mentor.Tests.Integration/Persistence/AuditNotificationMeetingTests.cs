using Npgsql;

namespace Mentor.Tests.Integration.Persistence;

// The audit trail, notification events and meetings (ADR-007, ADR-009, ADR-010; DR-013, DR-014).
public sealed class AuditNotificationMeetingTests(PostgresServer postgres)
{
    private const string AuditEntry = """
        INSERT INTO mentor.audit_entry (id, actor_employee_id, identity_scheme, action, record_type, record_id, candidate_id,
            revision_id, content_hash, outcome, provenance)
        VALUES (gen_random_uuid(), $1, $2, 'ACCEPTANCE_RECORDED', 'AGREEMENT_ACCEPTANCE', gen_random_uuid(), $3, $4, $5, NULL, 'APPLICATION')
        """;

    [Theory]
    [InlineData("ENTRA")]
    [InlineData("SIMULATED")]
    public async Task An_application_event_records_how_its_actor_was_authenticated(string scheme)
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var mentorship = await new Seed(db).FormedMentorshipAsync();

        await db.ExecuteAsync(Login.App, AuditEntry, mentorship.Protege, scheme, mentorship.Candidate, mentorship.Revision.Id, mentorship.Revision.ContentHash);
        await Rejected.WithAsync(PostgresErrorCodes.CheckViolation, () => db.ExecuteAsync(Login.App, AuditEntry,
            mentorship.Protege, "DATABASE", mentorship.Candidate, mentorship.Revision.Id, mentorship.Revision.ContentHash),
            "ck_audit_entry_origin");
    }

    [Fact]
    public async Task An_audit_entrys_decision_context_names_a_revision_together_with_that_revisions_hash()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var mentorship = await new Seed(db).FormedMentorshipAsync();

        await Rejected.WithAsync(PostgresErrorCodes.CheckViolation, () => db.ExecuteAsync(Login.App, AuditEntry,
            mentorship.Protege, "ENTRA", mentorship.Candidate, mentorship.Revision.Id, null), "ck_audit_entry_decision_context");
        await Rejected.WithAsync(PostgresErrorCodes.ForeignKeyViolation, () => db.ExecuteAsync(Login.App, AuditEntry,
            mentorship.Protege, "ENTRA", mentorship.Candidate, mentorship.Revision.Id, new string('0', 64)), "fk_audit_entry_revision_content");
    }

    [Theory]
    [InlineData("SIMULATED", true)]
    [InlineData("NONE", false)]
    public async Task A_simulated_delivery_happens_when_the_event_is_recorded_and_no_other_delivery_is_claimed(string delivery, bool delivered)
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var mentorship = await new Seed(db).FormedMentorshipAsync();

        var deliveredAtCreation = await db.ScalarAsync<bool?>(Login.App, """
            INSERT INTO mentor.notification_event (id, event_type, subject_type, subject_id, recipient_employee_id, recipient_role,
                required_action, delivery, delivered_at)
            VALUES (gen_random_uuid(), 'INVITATION_SENT', 'CANDIDATE', $1, $2, 'MENTOR', 'RESPOND_TO_INVITATION', $3, now() - interval '1 day')
            RETURNING delivered_at = created_at
            """, mentorship.Candidate, mentorship.Mentor, delivery);

        Assert.Equal(delivered ? true : null, deliveredAtCreation);
    }

    [Theory]
    [InlineData("INTRODUCTORY", false, true)]
    [InlineData("RECURRING_SESSION", true, false)]
    [InlineData("RECURRING_SESSION", true, true)]
    public async Task An_introduction_belongs_to_a_candidate_and_a_recurring_session_to_a_relationship(string kind, bool candidate, bool relationship)
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var mentorship = await new Seed(db).FormedMentorshipAsync();

        await Rejected.WithAsync(PostgresErrorCodes.CheckViolation, () => db.ExecuteAsync(Login.App, """
            INSERT INTO mentor.meeting (id, kind, candidate_id, relationship_id, arranged_outside_application, created_by_employee_id)
            VALUES (gen_random_uuid(), $1, $2, $3, true, $4)
            """, kind, candidate ? mentorship.Candidate : null, relationship ? mentorship.Relationship : null, mentorship.Protege),
            "ck_meeting_owner");
    }
}

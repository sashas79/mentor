using Npgsql;

namespace Mentor.Tests.Integration.Persistence;

// Controlled manual entry of historical records under mentor_legacy_entry (ADR-008; BR-016, BR-022, DR-016, FR-062).
public sealed class LegacyEntryTests(PostgresServer postgres)
{
    // AC-BR-022-01, AC-DR-005-01: the entry is attributed to the administrator who made it, keeps its source and leaves
    // unresolved values unresolved instead of inventing them.
    [Fact]
    public async Task An_authorised_administrator_enters_a_historical_relationship_attributed_to_themselves()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var (seed, alice, _, protege, mentor) = await PeopleAsync(db);
        await seed.AuthoriseLegacyEntryAsync(Login.Alice, alice);

        var relationship = await seed.LegacyRelationshipAsync(Login.Alice, alice, protege, mentor, sourceRow: 8);
        await seed.LegacyReviewExceptionAsync(Login.Alice, alice, 8, "UNRESOLVED_VALUE", relationship, "STATE");
        await db.ExecuteAsync(Login.Alice, """
            INSERT INTO mentor.audit_entry (id, actor_employee_id, identity_scheme, action, record_type, record_id, provenance, source_file, source_row)
            VALUES (gen_random_uuid(), $1, 'DATABASE', 'LEGACY_ENTRY', 'RELATIONSHIP', $2, 'LEGACY_SOURCE', $3, 8)
            """, alice, relationship, Seed.SourceFile);

        Assert.Equal(alice, await db.ScalarAsync<Guid>(Login.App,
            "SELECT entered_by_employee_id FROM mentor.mentorship_relationship WHERE id = $1 AND state IS NULL AND status IS NULL AND is_active IS NULL",
            relationship));
    }

    [Fact]
    public async Task An_entry_attributed_to_anyone_else_is_refused()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var (seed, alice, bob, protege, mentor) = await PeopleAsync(db);
        await seed.AuthoriseLegacyEntryAsync(Login.Alice, alice);

        await Rejected.WithAsync(PostgresErrorCodes.InsufficientPrivilege,
            () => seed.LegacyRelationshipAsync(Login.Alice, bob, protege, mentor, sourceRow: 8));
        await Rejected.WithAsync(PostgresErrorCodes.InsufficientPrivilege,
            () => seed.LegacyReviewExceptionAsync(Login.Alice, bob, 9, "NO_MENTOR"));
    }

    [Fact]
    public async Task An_account_without_an_authorisation_enters_nothing()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var (seed, _, bob, protege, mentor) = await PeopleAsync(db);

        await Rejected.WithAsync(PostgresErrorCodes.InsufficientPrivilege,
            () => seed.LegacyRelationshipAsync(Login.Bob, bob, protege, mentor, sourceRow: 8));
    }

    [Fact]
    public async Task An_account_authorises_only_itself()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var (_, _, bob, _, _) = await PeopleAsync(db);

        await Rejected.WithAsync(PostgresErrorCodes.InsufficientPrivilege, () => db.ExecuteAsync(Login.Alice,
            "INSERT INTO mentor.legacy_entry_authorisation (id, employee_id, database_account) VALUES (gen_random_uuid(), $1, $2)",
            bob, PostgresServer.AccountOf(Login.Bob)));
    }

    // AC-BR-022-02: nobody but a Talent Management administrator's legacy entry account enters historical relationships,
    // the application included.
    [Fact]
    public async Task The_application_cannot_enter_historical_relationships()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var (seed, alice, _, protege, mentor) = await PeopleAsync(db);

        await Rejected.WithAsync(PostgresErrorCodes.InsufficientPrivilege,
            () => seed.LegacyRelationshipAsync(Login.App, alice, protege, mentor, sourceRow: 8));
        await Rejected.WithAsync(PostgresErrorCodes.InsufficientPrivilege, () => db.ExecuteAsync(Login.App, """
            INSERT INTO mentor.audit_entry (id, actor_employee_id, identity_scheme, action, record_type, provenance, source_file, source_row)
            VALUES (gen_random_uuid(), $1, 'DATABASE', 'LEGACY_ENTRY', 'RELATIONSHIP', 'LEGACY_SOURCE', 'tracker.xlsx', 8)
            """, alice));
    }

    [Fact]
    public async Task Legacy_entry_writes_no_application_record()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var (seed, alice, _, protege, mentor) = await PeopleAsync(db);
        await seed.AuthoriseLegacyEntryAsync(Login.Alice, alice);

        await Rejected.WithAsync(PostgresErrorCodes.InsufficientPrivilege, () => db.ExecuteAsync(Login.Alice, """
            INSERT INTO mentor.mentorship_relationship (id, provenance, protege_employee_id, mentor_employee_id, state, status, is_active)
            VALUES (gen_random_uuid(), 'DIGITAL', $1, $2, 'IN_PROGRESS', 'BLANK', 'N')
            """, protege, mentor));
        await Rejected.WithAsync(PostgresErrorCodes.InsufficientPrivilege, () => db.ExecuteAsync(Login.Alice, """
            INSERT INTO mentor.audit_entry (id, actor_employee_id, identity_scheme, action, record_type, provenance)
            VALUES (gen_random_uuid(), $1, 'ENTRA', 'ATTRIBUTE_CHANGED', 'RELATIONSHIP', 'APPLICATION')
            """, alice));
    }

    // AC-DR-016-01, AC-NFR-003-01: a source row naming someone who is not in the directory is kept as a review exception
    // with its source, outside the relationship table, and nobody is invented for it.
    [Fact]
    public async Task An_unresolved_person_is_kept_as_a_review_exception_without_a_relationship_or_an_employee()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var (seed, alice, _, _, _) = await PeopleAsync(db);
        await seed.AuthoriseLegacyEntryAsync(Login.Alice, alice);
        var employees = await db.ScalarAsync<long>(Login.App, "SELECT count(*) FROM mentor.employee");

        await seed.LegacyReviewExceptionAsync(Login.Alice, alice, 12, "UNRESOLVED_PERSON");

        await Rejected.WithAsync(PostgresErrorCodes.CheckViolation,
            () => seed.LegacyReviewExceptionAsync(Login.Alice, alice, 13, "UNRESOLVED_PERSON", relationship: Guid.NewGuid()),
            "ck_legacy_review_exception_subject");
        Assert.Equal(employees, await db.ScalarAsync<long>(Login.App, "SELECT count(*) FROM mentor.employee"));
        Assert.Equal(0, await db.ScalarAsync<long>(Login.App, "SELECT count(*) FROM mentor.mentorship_relationship"));
    }

    [Fact]
    public async Task A_source_row_is_entered_once()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var (seed, alice, _, protege, mentor) = await PeopleAsync(db);
        await seed.AuthoriseLegacyEntryAsync(Login.Alice, alice);
        await seed.LegacyRelationshipAsync(Login.Alice, alice, protege, mentor, sourceRow: 8);

        await Rejected.WithAsync(PostgresErrorCodes.UniqueViolation,
            () => seed.LegacyRelationshipAsync(Login.Alice, alice, protege, mentor, sourceRow: 8),
            "ix_mentorship_relationship_source_file_source_row");
    }

    // AC-DR-014-01: a legacy-source claim is distinguishable from an application event and names its source row.
    [Fact]
    public async Task A_legacy_audit_entry_names_its_source_and_its_database_account_authentication()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var (seed, alice, _, _, _) = await PeopleAsync(db);
        await seed.AuthoriseLegacyEntryAsync(Login.Alice, alice);

        await Rejected.WithAsync(PostgresErrorCodes.CheckViolation, () => db.ExecuteAsync(Login.Alice, """
            INSERT INTO mentor.audit_entry (id, actor_employee_id, identity_scheme, action, record_type, provenance)
            VALUES (gen_random_uuid(), $1, 'DATABASE', 'LEGACY_ENTRY', 'RELATIONSHIP', 'LEGACY_SOURCE')
            """, alice), "ck_audit_entry_origin");
        await Rejected.WithAsync(PostgresErrorCodes.CheckViolation, () => db.ExecuteAsync(Login.Alice, """
            INSERT INTO mentor.audit_entry (id, actor_employee_id, identity_scheme, action, record_type, provenance, source_file, source_row)
            VALUES (gen_random_uuid(), $1, 'ENTRA', 'LEGACY_ENTRY', 'RELATIONSHIP', 'LEGACY_SOURCE', 'tracker.xlsx', 8)
            """, alice), "ck_audit_entry_origin");
    }

    private static async Task<(Seed Seed, Guid Alice, Guid Bob, Guid Protege, Guid Mentor)> PeopleAsync(TestDatabase db)
    {
        var seed = new Seed(db);
        return (seed, await seed.EmployeeAsync("Alice"), await seed.EmployeeAsync("Bob"), await seed.EmployeeAsync("Protégé"), await seed.EmployeeAsync("Mentor"));
    }
}

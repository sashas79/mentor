using Npgsql;

namespace Mentor.Tests.Integration.Persistence;

// The database rules run as the account whose statement fires them. They hold whatever that account's session puts on
// its search path or creates in its own temporary schema, because every function of the schema resolves the names in
// its body in the system catalog first (ADR-008, ADR-013).
public sealed class SearchPathTests(PostgresServer postgres)
{
    private const string LegacyRelationship = """
        INSERT INTO mentor.mentorship_relationship (id, provenance, protege_employee_id, mentor_employee_id, source_file, source_row,
            source_mentor_text, source_protege_text, source_active_text, source_duration_text, source_start_text, source_end_text,
            entered_by_employee_id)
        VALUES (gen_random_uuid(), 'LEGACY', $1, $2, $3, 8, 'M. Mentor', 'P. Protégé', 'Successfully complete', 'ongoing', '10/1/2026',
            'ongoing', $4)
        """;

    private const string LegacyReviewException = """
        INSERT INTO mentor.legacy_review_exception (id, source_file, source_row, reason, source_mentor_text, source_active_text, entered_by_employee_id)
        VALUES (gen_random_uuid(), $1, 9, 'NO_MENTOR', 'J. Unknown', 'Successfully complete', $2)
        """;

    private const string LegacyAuditEntry = """
        INSERT INTO mentor.audit_entry (id, actor_employee_id, identity_scheme, action, record_type, provenance, source_file, source_row)
        VALUES (gen_random_uuid(), $1, 'DATABASE', 'LEGACY_ENTRY', 'RELATIONSHIP', 'LEGACY_SOURCE', $2, 8)
        """;

    [Fact]
    public async Task Every_function_of_the_schema_fixes_its_search_path()
    {
        await using var db = await postgres.CreateDatabaseAsync();

        var unfixed = await db.ScalarAsync<string[]>(Login.Owner, """
            SELECT coalesce(array_agg(routine.oid::regprocedure::text ORDER BY routine.oid::regprocedure::text), '{}')
            FROM pg_proc AS routine
            JOIN pg_namespace AS namespace ON namespace.oid = routine.pronamespace
            WHERE namespace.nspname = 'mentor' AND 'search_path=pg_catalog, pg_temp' <> ALL (coalesce(routine.proconfig, '{}'))
            """);

        Assert.Empty(unfixed);
    }

    // AC-BR-022-01, AC-NFR-004-01: an account needs no privilege to create a type in its own temporary schema, which is
    // searched for a type name before the system catalog unless the path says otherwise. A uuid of one character there
    // compares two identifiers by their first character, and the application's identifiers begin alike: they are
    // time-ordered.
    [Fact]
    public async Task A_legacy_entry_is_attributed_to_its_administrator_whatever_type_the_session_defines()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var seed = new Seed(db);
        var alice = await AdministratorAsync(db, "01a1ca25-f35c-7000-8000-00000000000a");
        var bob = await AdministratorAsync(db, "01a1ca25-f35c-7000-8000-00000000000b");
        var (protege, mentor) = (await seed.EmployeeAsync("Protégé"), await seed.EmployeeAsync("Mentor"));
        await seed.AuthoriseLegacyEntryAsync(Login.Alice, alice);
        await using var session = await Session.OpenAsync(db, Login.Alice);

        await session.ExecuteAsync("CREATE DOMAIN pg_temp.uuid AS \"char\"");

        await Rejected.WithAsync(PostgresErrorCodes.InsufficientPrivilege,
            () => session.ExecuteAsync(LegacyRelationship, protege, mentor, Seed.SourceFile, bob));
        await Rejected.WithAsync(PostgresErrorCodes.InsufficientPrivilege,
            () => session.ExecuteAsync(LegacyReviewException, Seed.SourceFile, bob));
        await Rejected.WithAsync(PostgresErrorCodes.InsufficientPrivilege,
            () => session.ExecuteAsync(LegacyAuditEntry, bob, Seed.SourceFile));
        await session.ExecuteAsync(LegacyRelationship, protege, mentor, Seed.SourceFile, alice);
        Assert.Equal(alice, await db.ScalarAsync<Guid>(Login.Owner, "SELECT entered_by_employee_id FROM mentor.mentorship_relationship"));
    }

    // No account is given a schema to create functions in. The rules do not depend on that: a function or an operator
    // ahead of the system catalog's on the session's search path is never the one a rule calls.
    [Fact]
    public async Task Legacy_entry_writes_no_application_record_whatever_function_is_on_the_search_path()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var administrator = await new Seed(db).EmployeeAsync("Talent Management");
        await using var session = await ShadowingSessionAsync(db, Login.Alice, "shadow",
            "CREATE FUNCTION shadow.pg_has_role(name, text, text) RETURNS boolean LANGUAGE sql AS 'SELECT false'");

        await Rejected.WithAsync(PostgresErrorCodes.InsufficientPrivilege, () => session.ExecuteAsync("""
            INSERT INTO mentor.audit_entry (id, actor_employee_id, identity_scheme, action, record_type, provenance)
            VALUES (gen_random_uuid(), $1, 'ENTRA', 'ATTRIBUTE_CHANGED', 'RELATIONSHIP', 'APPLICATION')
            """, administrator));
    }

    // AC-NFR-004-01: an insert and an update both record the server's time, not the time of a now() of the session's own.
    [Fact]
    public async Task Recorded_times_are_the_database_server_time_whatever_function_is_on_the_search_path()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var invented = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await using var session = await ShadowingSessionAsync(db, Login.App, "shadow, pg_catalog",
            "CREATE FUNCTION shadow.now() RETURNS timestamptz LANGUAGE sql AS $$ SELECT TIMESTAMPTZ '2001-01-01 00:00:00+00' $$");

        var created = await session.ScalarAsync<DateTime>(
            "INSERT INTO mentor.employee (id, entra_object_id, display_name) VALUES (gen_random_uuid(), gen_random_uuid(), 'Employee') RETURNING created_at");
        var updated = await session.ScalarAsync<DateTime>("UPDATE mentor.employee SET display_name = 'Renamed' RETURNING updated_at");

        Assert.True(created > invented.AddYears(20));
        Assert.True(updated > invented.AddYears(20));
    }

    // AC-DR-011-01: the rule compares the goal's status with the system catalog's equality, not the session's.
    [Fact]
    public async Task A_goal_is_complete_only_with_approval_whatever_operator_is_on_the_search_path()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var mentorship = await new Seed(db).FormedMentorshipAsync();
        await using var session = await ShadowingSessionAsync(db, Login.App, "shadow, pg_catalog",
            "CREATE FUNCTION shadow.never(text, text) RETURNS boolean LANGUAGE sql AS 'SELECT false'",
            "CREATE OPERATOR shadow.= (LEFTARG = text, RIGHTARG = text, FUNCTION = shadow.never)");

        await Rejected.WithAsync(PostgresErrorCodes.CheckViolation,
            () => session.ExecuteAsync("UPDATE mentor.development_goal SET completion_status = 'COMPLETE' WHERE id = $1", mentorship.Goal));
    }

    /// <summary>An employee with the identifier given, shaped as the application's are: UUID version 7, a time prefix first.</summary>
    private static Task<Guid> AdministratorAsync(TestDatabase db, string id) =>
        db.ScalarAsync<Guid>(Login.Owner,
            "INSERT INTO mentor.employee (id, entra_object_id, display_name) VALUES ($1, gen_random_uuid(), 'Talent Management') RETURNING id",
            Guid.Parse(id));

    /// <summary>
    /// A session of <paramref name="login"/> that has created its own objects in a schema it may create in, and then put
    /// that schema on its search path.
    /// </summary>
    private static async Task<Session> ShadowingSessionAsync(TestDatabase db, Login login, string searchPath, params string[] objects)
    {
        await db.ExecuteAsync(Login.Owner, "CREATE SCHEMA shadow; GRANT ALL ON SCHEMA shadow TO PUBLIC");
        var session = await Session.OpenAsync(db, login);
        foreach (var definition in objects)
        {
            await session.ExecuteAsync(definition);
        }

        await session.ExecuteAsync($"SET search_path = {searchPath}");
        return session;
    }

    /// <summary>One session of an account: what it sets, and what it creates in its temporary schema, lasts until it closes.</summary>
    private sealed class Session(NpgsqlConnection connection) : IAsyncDisposable
    {
        public static async Task<Session> OpenAsync(TestDatabase db, Login login)
        {
            var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(db.ConnectionString(login)) { Pooling = false }.ConnectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            return new Session(connection);
        }

        public async Task ExecuteAsync(string sql, params object?[] parameters)
        {
            await using var command = Command(sql, parameters);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        public async Task<T> ScalarAsync<T>(string sql, params object?[] parameters)
        {
            await using var command = Command(sql, parameters);
            return (T)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
        }

        public ValueTask DisposeAsync() => connection.DisposeAsync();

        private NpgsqlCommand Command(string sql, object?[] parameters)
        {
            var command = new NpgsqlCommand(sql, connection);
            foreach (var parameter in parameters)
            {
                command.Parameters.Add(new NpgsqlParameter { Value = parameter ?? DBNull.Value });
            }

            return command;
        }
    }
}

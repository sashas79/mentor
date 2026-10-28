using Npgsql;

namespace Mentor.Tests.Integration.Persistence;

// The database rules that bind every role, the schema owner included, so an application connected with the wrong
// account still cannot rewrite history (ADR-013).
public sealed class ImmutabilityTests(PostgresServer postgres)
{
    [Theory]
    [MemberData(nameof(Tables.EvidenceTables), MemberType = typeof(Tables))]
    public async Task Evidence_cannot_be_changed_or_removed_even_by_the_schema_owner(string table)
    {
        await using var db = await postgres.CreateDatabaseAsync();
        await new Seed(db).EveryTableAsync();

        await Rejected.WithAsync(PostgresErrorCodes.RestrictViolation, () => db.ExecuteAsync(Login.Owner, $"UPDATE mentor.{table} SET id = id"));
        await Rejected.WithAsync(PostgresErrorCodes.RestrictViolation, () => db.ExecuteAsync(Login.Owner, $"DELETE FROM mentor.{table}"));
    }

    [Theory]
    [MemberData(nameof(Tables.OperationalTables), MemberType = typeof(Tables))]
    public async Task Operational_rows_cannot_be_deleted_even_by_the_schema_owner(string table)
    {
        await using var db = await postgres.CreateDatabaseAsync();
        await new Seed(db).EveryTableAsync();

        await Rejected.WithAsync(PostgresErrorCodes.RestrictViolation, () => db.ExecuteAsync(Login.Owner, $"DELETE FROM mentor.{table}"));
    }

    [Theory]
    [InlineData("employee", "entra_object_id", "gen_random_uuid()")]
    [InlineData("employee", "created_at", "now() - interval '1 day'")]
    [InlineData("mentor_profile", "employee_id", "gen_random_uuid()")]
    [InlineData("mentorship_request", "protege_employee_id", "gen_random_uuid()")]
    [InlineData("mentor_selection", "mentor_employee_id", "gen_random_uuid()")]
    [InlineData("mentorship_relationship", "provenance", "'LEGACY'")]
    [InlineData("mentorship_relationship", "start_date", "DATE '2026-01-10'")]
    [InlineData("development_goal", "agreed_description", "'Lead every design review'")]
    [InlineData("meeting", "frequency_weeks", "9")]
    [InlineData("notification_event", "event_type", "'INVITATION_SENT'")]
    public async Task Immutable_columns_cannot_change_even_for_the_schema_owner(string table, string column, string value)
    {
        await using var db = await postgres.CreateDatabaseAsync();
        await new Seed(db).EveryTableAsync();

        await Rejected.WithAsync(PostgresErrorCodes.RestrictViolation, () => db.ExecuteAsync(Login.Owner, $"UPDATE mentor.{table} SET {column} = {value}"));
    }

    [Fact]
    public async Task Recorded_times_are_the_database_server_time_whatever_a_statement_supplies()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var invented = new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var recorded = await db.ScalarAsync<DateTime>(Login.Owner, """
            INSERT INTO mentor.employee (id, entra_object_id, display_name, created_at, updated_at)
            VALUES (gen_random_uuid(), gen_random_uuid(), 'Employee', $1, $1)
            RETURNING created_at
            """, invented);

        Assert.True(recorded > invented.UtcDateTime.AddYears(20));
    }

    [Fact]
    public async Task The_update_time_moves_with_every_update()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var employee = await new Seed(db).EmployeeAsync();
        var before = await db.ScalarAsync<DateTime>(Login.Owner, "SELECT updated_at FROM mentor.employee WHERE id = $1", employee);

        var after = await db.ScalarAsync<DateTime>(Login.App,
            "UPDATE mentor.employee SET display_name = 'Renamed' WHERE id = $1 RETURNING updated_at", employee);

        Assert.True(after > before);
    }
}

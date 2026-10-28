using Npgsql;

namespace Mentor.Tests.Integration.Persistence;

// The privileges of ADR-013: mentor_app never updates evidence, never deletes, and updates only the mutable columns of
// operational rows; mentor_legacy_entry inserts legacy rows only.
public sealed class PrivilegeTests(PostgresServer postgres)
{
    [Theory]
    [MemberData(nameof(Tables.EvidenceTables), MemberType = typeof(Tables))]
    public async Task The_application_can_neither_update_nor_delete_evidence(string table)
    {
        await using var db = await postgres.CreateDatabaseAsync();

        await Rejected.WithAsync(PostgresErrorCodes.InsufficientPrivilege, () => db.ExecuteAsync(Login.App, $"UPDATE mentor.{table} SET id = id"));
        await Rejected.WithAsync(PostgresErrorCodes.InsufficientPrivilege, () => db.ExecuteAsync(Login.App, $"DELETE FROM mentor.{table}"));
    }

    [Theory]
    [MemberData(nameof(Tables.OperationalTables), MemberType = typeof(Tables))]
    public async Task The_application_never_deletes_an_operational_row(string table)
    {
        await using var db = await postgres.CreateDatabaseAsync();

        await Rejected.WithAsync(PostgresErrorCodes.InsufficientPrivilege, () => db.ExecuteAsync(Login.App, $"DELETE FROM mentor.{table}"));
    }

    [Theory]
    [InlineData("employee", "display_name")]
    [InlineData("employee", "level_title")]
    [InlineData("mentor_profile", "biography")]
    [InlineData("mentor_profile", "approval_status")]
    [InlineData("mentorship_request", "motivation")]
    [InlineData("mentorship_request", "disposition")]
    [InlineData("mentor_selection", "status")]
    [InlineData("mentor_selection", "current_revision_id")]
    [InlineData("mentor_selection", "admin_remarks")]
    [InlineData("mentor_selection", "version")]
    [InlineData("mentorship_relationship", "state")]
    [InlineData("mentorship_relationship", "status")]
    [InlineData("mentorship_relationship", "is_active")]
    [InlineData("mentorship_relationship", "version")]
    [InlineData("development_goal", "completion_status")]
    [InlineData("meeting", "scheduled_at")]
    [InlineData("notification_event", "read_at")]
    public async Task The_application_updates_the_mutable_columns_of_operational_rows(string table, string column)
    {
        await using var db = await postgres.CreateDatabaseAsync();
        await new Seed(db).EveryTableAsync();

        var updated = await db.ExecuteAsync(Login.App, $"UPDATE mentor.{table} SET {column} = {column}");

        Assert.True(updated > 0);
    }

    [Theory]
    [InlineData("employee", "entra_object_id")]
    [InlineData("mentor_profile", "employee_id")]
    [InlineData("mentorship_request", "protege_employee_id")]
    [InlineData("mentor_selection", "request_id")]
    [InlineData("mentor_selection", "protege_employee_id")]
    [InlineData("mentor_selection", "mentor_employee_id")]
    [InlineData("mentorship_relationship", "provenance")]
    [InlineData("mentorship_relationship", "candidate_id")]
    [InlineData("mentorship_relationship", "mentor_employee_id")]
    [InlineData("mentorship_relationship", "start_date")]
    [InlineData("development_goal", "agreed_description")]
    [InlineData("meeting", "frequency_weeks")]
    [InlineData("notification_event", "recipient_employee_id")]
    public async Task The_application_cannot_update_an_immutable_column(string table, string column)
    {
        await using var db = await postgres.CreateDatabaseAsync();

        await Rejected.WithAsync(PostgresErrorCodes.InsufficientPrivilege, () => db.ExecuteAsync(Login.App, $"UPDATE mentor.{table} SET {column} = {column}"));
    }

    [Fact]
    public async Task The_application_cannot_write_the_legacy_tables()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var employee = await new Seed(db).EmployeeAsync();

        await Rejected.WithAsync(PostgresErrorCodes.InsufficientPrivilege, () => db.ExecuteAsync(Login.App,
            "INSERT INTO mentor.legacy_entry_authorisation (id, employee_id, database_account) VALUES (gen_random_uuid(), $1, session_user)", employee));
        await Rejected.WithAsync(PostgresErrorCodes.InsufficientPrivilege, () => db.ExecuteAsync(Login.App, """
            INSERT INTO mentor.legacy_review_exception (id, source_file, source_row, reason, entered_by_employee_id)
            VALUES (gen_random_uuid(), 'tracker.xlsx', 9, 'NO_MENTOR', $1)
            """, employee));
    }

    // AC-DR-016-02: legacy entry can never create an employee, let alone one for an unresolved person.
    [Fact]
    public async Task Legacy_entry_creates_no_employee()
    {
        await using var db = await postgres.CreateDatabaseAsync();

        await Rejected.WithAsync(PostgresErrorCodes.InsufficientPrivilege, () => db.ExecuteAsync(Login.Alice,
            "INSERT INTO mentor.employee (id, entra_object_id, display_name) VALUES (gen_random_uuid(), gen_random_uuid(), 'J. Unknown')"));
    }

    [Theory]
    [InlineData("UPDATE mentor.mentorship_relationship SET state = state")]
    [InlineData("DELETE FROM mentor.legacy_review_exception")]
    [InlineData("INSERT INTO mentor.meeting (id, kind) VALUES (gen_random_uuid(), 'INTRODUCTORY')")]
    [InlineData("SELECT count(*) FROM mentor.agreement_revision")]
    public async Task Legacy_entry_reaches_nothing_beyond_its_own_rows(string statement)
    {
        await using var db = await postgres.CreateDatabaseAsync();

        await Rejected.WithAsync(PostgresErrorCodes.InsufficientPrivilege, () => db.ExecuteAsync(Login.Alice, statement));
    }
}

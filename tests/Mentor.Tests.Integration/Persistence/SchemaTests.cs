using Microsoft.EntityFrameworkCore;

namespace Mentor.Tests.Integration.Persistence;

public sealed class SchemaTests(PostgresServer postgres)
{
    [Fact]
    public async Task The_migrations_build_exactly_the_mapped_model()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        await using var context = db.Context(Login.Owner);

        Assert.Empty(await context.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Every_table_is_insert_only_evidence_or_an_operational_table_that_never_loses_rows()
    {
        await using var db = await postgres.CreateDatabaseAsync();

        var tables = await db.ScalarAsync<string[]>(Login.Owner,
            "SELECT array_agg(tablename::text ORDER BY tablename) FROM pg_tables WHERE schemaname = 'mentor' AND tablename <> 'ef_migrations_history'");
        var evidence = await TablesWithTrigger(db, "evidence_is_insert_only");
        var operational = await TablesWithTrigger(db, "rows_are_never_deleted");

        Assert.Equal(Tables.Evidence.Order(), evidence);
        Assert.Equal(Tables.Operational.Order(), operational);
        Assert.Equal(tables, evidence.Concat(operational).Order());
    }

    // No private meeting discussion can be captured because nothing can hold it (NFR-002, ADR-010).
    [Fact]
    public async Task No_column_can_hold_meeting_discussion()
    {
        await using var db = await postgres.CreateDatabaseAsync();

        var meetingColumns = await db.ScalarAsync<string[]>(Login.Owner,
            "SELECT array_agg(column_name::text ORDER BY column_name) FROM information_schema.columns WHERE table_schema = 'mentor' AND table_name = 'meeting'");
        var discussionColumns = await db.ScalarAsync<long>(Login.Owner, """
            SELECT count(*) FROM information_schema.columns
            WHERE table_schema = 'mentor' AND column_name ~ '(note|discussion|summary|transcript|minutes_of)'
            """);

        Assert.Equal(
            ["arranged_outside_application", "candidate_id", "created_at", "created_by_employee_id", "duration_minutes",
                "frequency_weeks", "id", "kind", "relationship_id", "scheduled_at", "updated_at"],
            meetingColumns);
        Assert.Equal(0, discussionColumns);
    }

    private static Task<string[]> TablesWithTrigger(TestDatabase db, string trigger) =>
        db.ScalarAsync<string[]>(Login.Owner, """
            SELECT array_agg(c.relname::text ORDER BY c.relname)
            FROM pg_trigger t
            JOIN pg_class c ON c.oid = t.tgrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'mentor' AND t.tgname = $1
            """, trigger);
}

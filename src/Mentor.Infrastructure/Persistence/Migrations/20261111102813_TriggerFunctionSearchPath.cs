using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mentor.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The trigger functions of the initial schema resolve the names in their bodies in the system catalog, whatever the
    /// session that fires them has done (ADR-008, ADR-013). They run as the account whose statement fires them, and until
    /// now they took that session's search path. A type the account created in its own temporary schema, or a function or
    /// operator in a schema it put first, then stood in for the system catalog's: an authorised legacy entry account could
    /// attribute an entry to another employee. Each function now fixes the path that the legacy entry procedure fixes: the
    /// system catalog first, the temporary schema last. The bodies are unchanged, and so is the model.
    /// </summary>
    public partial class TriggerFunctionSearchPath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql("""
                ALTER FUNCTION mentor.reject_change() SET search_path = pg_catalog, pg_temp;
                ALTER FUNCTION mentor.stamp_insert_time() SET search_path = pg_catalog, pg_temp;
                ALTER FUNCTION mentor.stamp_notification_times() SET search_path = pg_catalog, pg_temp;
                ALTER FUNCTION mentor.guard_update() SET search_path = pg_catalog, pg_temp;
                ALTER FUNCTION mentor.require_completion_approval() SET search_path = pg_catalog, pg_temp;
                ALTER FUNCTION mentor.enforce_legacy_entry() SET search_path = pg_catalog, pg_temp;
                """);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql("""
                ALTER FUNCTION mentor.reject_change() RESET search_path;
                ALTER FUNCTION mentor.stamp_insert_time() RESET search_path;
                ALTER FUNCTION mentor.stamp_notification_times() RESET search_path;
                ALTER FUNCTION mentor.guard_update() RESET search_path;
                ALTER FUNCTION mentor.require_completion_approval() RESET search_path;
                ALTER FUNCTION mentor.enforce_legacy_entry() RESET search_path;
                """);
    }
}

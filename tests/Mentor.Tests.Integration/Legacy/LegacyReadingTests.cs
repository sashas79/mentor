using Mentor.Tests.Integration.Persistence;

namespace Mentor.Tests.Integration.Legacy;

// How a tracker cell is read during legacy entry (FR-062, ADR-006, ADR-008; the legacy tracker mapping and the
// product_domain decisions of 27 Oct 2026 recorded in the specification): only as a recorded decision allows; any other
// cell is unknown, never a guess. The readings run as the legacy-entry account that enters the rows.
public sealed class LegacyReadingTests(PostgresServer server)
{
    // AC-FR-062-01, AC-FR-062-02, AC-E2E-07: a date is read only from day/month/year with a four-digit year, naming a real
    // day; "ongoing", another order or separator, a two-digit year and an impossible day stay unknown. Spacing around it,
    // a line break copied with the cell included, is ignored, as it is for every cell.
    [Theory]
    [InlineData("10/1/2026", "2026-01-10")]
    [InlineData("01/10/2026", "2026-10-01")]
    [InlineData("31/12/2025", "2025-12-31")]
    [InlineData(" 9/3/2027 ", "2027-03-09")]
    [InlineData("10/1/2026\r\n", "2026-01-10")]
    [InlineData("29/02/2028", "2028-02-29")]
    [InlineData("29/02/2026", null)]
    [InlineData("31/04/2026", null)]
    [InlineData("0/1/2026", null)]
    [InlineData("10/13/2026", null)]
    [InlineData("ongoing", null)]
    [InlineData("Ongoing", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("2026-01-10", null)]
    [InlineData("10/1/26", null)]
    [InlineData("10.1.2026", null)]
    [InlineData("10-1-2026", null)]
    [InlineData("Jan 2026", null)]
    [InlineData("10/1/2026 approx", null)]
    public async Task A_date_is_read_only_from_day_month_year(string? cell, string? expected)
    {
        await using var db = await server.CreateDatabaseAsync();

        Assert.Equal(expected, await db.ScalarAsync<string?>(Login.Alice, "SELECT mentor.legacy_source_date($1)::text", cell));
    }

    // BR-010, AC-E2E-07: a duration names one of the three categories exactly, in any letter case; anything else, "ongoing"
    // included, leaves the category unknown.
    [Theory]
    [InlineData("6 months", "SIX_MONTHS")]
    [InlineData("12 months", "TWELVE_MONTHS")]
    [InlineData("12+ months", "TWELVE_PLUS_MONTHS")]
    [InlineData(" 6  Months ", "SIX_MONTHS")]
    [InlineData("6 months\r\n", "SIX_MONTHS")]
    [InlineData("\t12+ months", "TWELVE_PLUS_MONTHS")]
    [InlineData("12\tmonths", "TWELVE_MONTHS")]
    [InlineData("ongoing", null)]
    [InlineData("6", null)]
    [InlineData("6 month", null)]
    [InlineData("six months", null)]
    [InlineData("12+", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public async Task A_duration_is_read_only_when_it_names_a_category(string? cell, string? expected)
    {
        await using var db = await server.CreateDatabaseAsync();

        Assert.Equal(expected, await db.ScalarAsync<string?>(Login.Alice, "SELECT mentor.legacy_duration_category($1)", cell));
    }

    // AC-FR-044-01, AC-FR-046-01, AC-BR-008-01, AC-E2E-07: an Active cell gives only what a recorded decision reads from it,
    // and never IsActive; "Yes", "Active", an empty cell and every other value resolve nothing.
    [Theory]
    [InlineData("Successfully complete", "COMPLETE", null)]
    [InlineData("  SUCCESSFULLY   complete ", "COMPLETE", null)]
    [InlineData("Successfully complete\r\n", "COMPLETE", null)]
    [InlineData("\tput on hold\n", "ON_HOLD", null)]
    [InlineData("No -\tneeds reassigning", null, "NEEDS_REASSIGNING")]
    [InlineData("put on hold", "ON_HOLD", null)]
    [InlineData("Put on hold", "ON_HOLD", null)]
    [InlineData("No - needs reassigning", null, "NEEDS_REASSIGNING")]
    [InlineData("Yes", null, null)]
    [InlineData("Y", null, null)]
    [InlineData("No", null, null)]
    [InlineData("Active", null, null)]
    [InlineData("Complete", null, null)]
    [InlineData("Successfully completed", null, null)]
    [InlineData("", null, null)]
    [InlineData(null, null, null)]
    public async Task An_active_cell_gives_only_what_a_recorded_decision_reads_from_it(string? cell, string? state, string? status)
    {
        await using var db = await server.CreateDatabaseAsync();

        var reading = await db.ScalarAsync<string>(Login.Alice,
            "SELECT concat_ws('|', coalesce(state, '-'), coalesce(status, '-'), coalesce(is_active, '-')) FROM mentor.legacy_active_reading($1)", cell);

        Assert.Equal($"{state ?? "-"}|{status ?? "-"}|-", reading);
    }
}

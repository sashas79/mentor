using System.Net;
using System.Text.Json;
using Mentor.Api.Hosting;
using Mentor.Tests.Integration.Legacy;
using Mentor.Tests.Integration.Persistence;
using Mentor.Tests.Integration.Relationships;
using Mentor.Tests.Integration.Support;
using static Mentor.Tests.Integration.Legacy.LegacyEntryScript;
using static Mentor.Tests.Integration.Register.RegisterApi;

namespace Mentor.Tests.Integration.Register;

// The Talent Management relationship register and its CSV export (COMP-REGISTER-REPORTING, ADR-011,
// CONTRACT-REGISTER-EXPORT): computed on demand from the authoritative store, with every relationship of either provenance
// and every held tracker row. An unknown stays unknown beside its retained source text, and historical provenance and
// unresolved mappings are distinguishable from digitally formed data (FR-060, FR-061, FR-062).
public sealed class RelationshipRegisterTests(PostgresServer server)
{
    private const string Level = "Principal Engineer";

    // AC-FR-060-01, AC-FR-061-01, AC-FR-062-01, AC-FR-062-02: the register and the export show a digital relationship,
    // a legacy relationship entered through the real entry script, and two held tracker rows. The legacy row's "10/1/2026" is
    // the date the entry decisions read and "ongoing" stays text with no end date. Its unresolved Status and IsActive are
    // empty and flagged as a review exception, while the digital row's BLANK Status is empty with no flag. Held rows show
    // their source text and reason only.
    [Fact]
    public async Task The_register_and_its_export_show_every_relationship_and_held_row_with_unknowns_left_unknown()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var (mentor, protege) = (api.Employee(Profile.Mentor), api.Employee(Profile.Employee));
        await api.Db.ExecuteAsync(Login.Owner, "UPDATE mentor.employee SET level_title = $1 WHERE id = $2", Level, mentor);
        var digital = await RelationshipApi.FormedAsync(api);
        await AuthoriseAsync(api.Db, Login.Alice, api.Callers.ObjectId(Profile.Administrator));
        var answers = await RunAsync(api.Db, Login.Alice,
            new TrackerRow(8, api.Callers.ObjectId(Profile.Employee), api.Callers.ObjectId(Profile.Mentor), Protege: "Zoë Mokoena"),
            new TrackerRow(9, api.Callers.ObjectId(Profile.Employee), null, Mentor: null, Active: "Yes", Duration: "ongoing", Start: "2025", End: null),
            new TrackerRow(12, Guid.NewGuid(), api.Callers.ObjectId(Profile.Mentor), Protege: "S. Dlamini", Active: "=1+1", Duration: "12+ months",
                Start: "1 March 2025", End: ""));
        Assert.Equal(["RELATIONSHIP", "NO_MENTOR", "UNRESOLVED_PERSON"], answers.Select(answer => answer.Outcome));
        var (legacy, noMentor, unresolvedPerson) = (answers[0].Record, answers[1].Record, answers[2].Record);

        var register = await ReadAsync(api);

        // Historical rows in tracker order, then the digital relationship.
        Assert.Equal(
            [("Relationship", legacy, (Guid?)null), ("HeldSourceRow", (Guid?)null, noMentor), ("HeldSourceRow", null, unresolvedPerson), ("Relationship", digital.Id(), null)],
            register.Select(entry => (entry.Text("kind"), entry.OptionalGuid("relationshipId"), entry.OptionalGuid("reviewExceptionId"))));
        Assert.All(register, entry => Assert.Equal(
            ["durationCategory", "endDate", "isActive", "kind", "mentor", "protege", "provenance", "relationshipId", "reviewException", "reviewExceptionId", "source", "startDate", "state", "status"],
            entry.PropertyNames()));

        var entered = register[0];
        Assert.Equal(("Legacy", mentor, Level, protege), (entered.Text("provenance"), entered.GetProperty("mentor").GetProperty("employeeId").GetGuid(),
            entered.GetProperty("mentor").Text("level"), entered.GetProperty("protege").GetProperty("employeeId").GetGuid()));
        Assert.Equal(("Complete", null, null, "2026-01-10", null, "SixMonths"),
            (Value(entered, "state"), Value(entered, "status"), Value(entered, "isActive"), Value(entered, "startDate"), Value(entered, "endDate"), Value(entered, "durationCategory")));
        var source = entered.GetProperty("source");
        Assert.Equal((Seed.SourceFile, 8, "J. van der Merwe", "Zoë Mokoena", "Successfully complete", "6 months", "10/1/2026", "ongoing"),
            (source.Text("file"), source.GetProperty("row").GetInt32(), source.Text("mentorText"), source.Text("protegeText"), source.Text("activeText"),
                source.Text("durationText"), source.Text("startText"), source.Text("endText")));
        Assert.Equal(("UnresolvedValue", "Status,IsActive"), ReviewOf(entered));

        var held = register[1];
        Assert.Equal(("Legacy", null, null, null, null, null), (held.Text("provenance"), Value(held, "mentor"), Value(held, "protege"), Value(held, "state"),
            Value(held, "startDate"), Value(held, "durationCategory")));
        Assert.Equal((9, null, "Yes", "2025"), (held.GetProperty("source").GetProperty("row").GetInt32(), Value(held.GetProperty("source"), "mentorText"),
            held.GetProperty("source").Text("activeText"), held.GetProperty("source").Text("startText")));
        Assert.Equal(("NoMentor", ""), ReviewOf(held));
        Assert.Equal(("UnresolvedPerson", ""), ReviewOf(register[2]));
        // The source text is kept verbatim; only the export keeps a spreadsheet from evaluating it.
        Assert.Equal(("=1+1", ""), (register[2].GetProperty("source").Text("activeText"), register[2].GetProperty("source").Text("endText")));

        var formed = register[3];
        Assert.Equal(("Digital", null, null), (formed.Text("provenance"), Value(formed, "source"), Value(formed, "reviewException")));
        Assert.Equal(("InProgress", "Blank", "N", "2026-12-01", "2027-11-30", "TwelveMonths"),
            (Value(formed, "state"), Value(formed, "status"), Value(formed, "isActive"), Value(formed, "startDate"), Value(formed, "endDate"), Value(formed, "durationCategory")));

        // The export has the same rows as the register.
        var (mentorName, protegeName) = (entered.GetProperty("mentor").Text("displayName"), entered.GetProperty("protege").Text("displayName"));
        var records = Records(await ExportAsync(api));
        Assert.Equal(
            [
                ["Mentor", "Mentor level", "Protégé", "Protégé level", "State", "Status", "IsActive", "Start date", "End date", "Duration", "Provenance",
                    "Source reference", "Source Active text", "Source Duration text", "Source date text", "Review exception"],
                [mentorName, Level, protegeName, "", "Complete", "", "", "2026-01-10", "", "6 months", "Legacy",
                    $"{Seed.SourceFile}, row 8", "Successfully complete", "6 months", "Start: 10/1/2026; End: ongoing", "Unresolved value: Status, IsActive"],
                ["", "", "T. Mokoena", "", "", "", "", "", "", "", "Legacy",
                    $"{Seed.SourceFile}, row 9", "Yes", "ongoing", "Start: 2025", "No mentor"],
                ["J. van der Merwe", "", "S. Dlamini", "", "", "", "", "", "", "", "Legacy",
                    $"{Seed.SourceFile}, row 12", "'=1+1", "12+ months", "Start: 1 March 2025", "Unresolved person"],
                [mentorName, Level, protegeName, "", "In Progress", "", "N", "2026-12-01", "2027-11-30", "12 months", "Digital",
                    "", "", "", "", ""],
            ],
            records);
    }

    // AC-FR-061-01: nothing is stored or synchronised; the next read reflects the store as it now is. An administrator's edit
    // of a legacy row is in the next register, and a value now set is no longer flagged. The exceptions recorded at entry
    // stay as entered.
    [Fact]
    public async Task The_register_is_reconstructed_from_the_store_each_time_and_a_value_since_set_is_no_longer_flagged()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        await AuthoriseAsync(api.Db, Login.Alice, api.Callers.ObjectId(Profile.Administrator));
        var (_, record) = Assert.Single(await RunAsync(api.Db, Login.Alice, new TrackerRow(8, api.Callers.ObjectId(Profile.Employee), api.Callers.ObjectId(Profile.Mentor))));
        var exceptions = await CountAsync(api.Db, "legacy_review_exception");
        Assert.Equal(("UnresolvedValue", "Status,IsActive"), ReviewOf(Assert.Single(await ReadAsync(api))));

        await RelationshipApi.EditAsync(api, await RelationshipApi.ReadAsync(api, record, Profile.Administrator), new { status = "NeedsReassigning" });
        Assert.Equal(("UnresolvedValue", "IsActive"), ReviewOf(Assert.Single(await ReadAsync(api))));
        Assert.Equal("Needs reassigning", Records(await ExportAsync(api))[1][5]);

        await RelationshipApi.EditAsync(api, await RelationshipApi.ReadAsync(api, record, Profile.Administrator), new { isActive = "N" });
        var resolved = Assert.Single(await ReadAsync(api));
        Assert.Equal((JsonValueKind.Null, "N"), (resolved.GetProperty("reviewException").ValueKind, resolved.Text("isActive")));
        var row = Records(await ExportAsync(api))[1];
        Assert.Equal(("Needs reassigning", "N", "Legacy", ""), (row[5], row[6], row[10], row[15]));
        Assert.Equal(exceptions, await CountAsync(api.Db, "legacy_review_exception"));
        Assert.Equal(0L, await api.Db.ScalarAsync<long>(Login.Owner,
            "SELECT count(*) FROM pg_tables WHERE schemaname = 'mentor' AND (tablename LIKE '%register%' OR tablename LIKE '%export%')"));
    }

    // A tracker row held for an unresolved person and entered once that person is resolved is listed once, as its relationship.
    [Fact]
    public async Task A_held_row_entered_later_is_listed_once_as_its_relationship()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        await AuthoriseAsync(api.Db, Login.Alice, api.Callers.ObjectId(Profile.Administrator));
        var unknown = Guid.NewGuid();
        Assert.Equal("UNRESOLVED_PERSON", Assert.Single(await RunAsync(api.Db, Login.Alice, new TrackerRow(12, unknown, api.Callers.ObjectId(Profile.Mentor)))).Outcome);
        Assert.Equal("HeldSourceRow", Assert.Single(await ReadAsync(api)).Text("kind"));

        var (_, resolved) = await PersonAsync(api.Db, "T. Mokoena");
        var (_, relationship) = Assert.Single(await RunAsync(api.Db, Login.Alice, new TrackerRow(12, resolved, api.Callers.ObjectId(Profile.Mentor))));

        var entry = Assert.Single(await ReadAsync(api));
        Assert.Equal(("Relationship", relationship, 12), (entry.Text("kind"), entry.OptionalGuid("relationshipId"), entry.GetProperty("source").GetProperty("row").GetInt32()));
        Assert.Equal(2, Records(await ExportAsync(api)).Count);
        Assert.Equal(1L, await api.Db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.legacy_review_exception WHERE reason = 'UNRESOLVED_PERSON'"));
    }

    // ADR-011: each export is audited, attributed to the administrator who asked, before the file is returned. The file is a
    // UTF-8 CSV download that no cache keeps. Reading the register is not an export and writes nothing.
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task Each_export_is_audited_as_the_administrator_and_offered_as_a_csv_download(IdentityMode mode)
    {
        await using var api = await WorkflowApi.StartAsync(server, mode);
        await CallerMentorship.FormedAsync(api.Db, api.Callers);
        await CallerMentorship.LegacyAsync(api.Db, api.Callers);
        await new Seed(api.Db).LegacyReviewExceptionAsync(Login.Alice, api.Employee(Profile.Administrator), sourceRow: 9, "NO_MENTOR");
        await ReadAsync(api);
        await GoalsAsync(api);
        Assert.Equal(0L, await ExportEntryCountAsync(api.Db));

        using var response = await api.SendAsync(HttpMethod.Get, Export, Profile.Administrator);
        using var again = await api.SendAsync(HttpMethod.Get, Export, Profile.Administrator);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (response.StatusCode, again.StatusCode));
        Assert.Equal("text/csv; charset=utf-8", response.Content.Headers.ContentType!.ToString());
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Matches("^mentorship-register-[0-9]{8}T[0-9]{6}Z\\.csv$", response.Content.Headers.ContentDisposition.FileNameStar ?? response.Content.Headers.ContentDisposition.FileName);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Equal(4, Records(await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).Count);

        var entries = await ExportEntriesAsync(api);
        Assert.Equal(2, entries.Count);
        Assert.All(entries, entry =>
        {
            Assert.Equal(("RegisterExported", "Register", null, "Application"), (entry.Text("action"), entry.Text("recordType"), Value(entry, "recordId"), entry.Text("provenance")));
            Assert.Equal((api.Employee(Profile.Administrator), mode.ToString()),
                (entry.GetProperty("actor").GetProperty("employeeId").GetGuid(), entry.GetProperty("actor").Text("identityScheme")));
            var values = entry.GetProperty("newValues");
            Assert.Equal(["format", "heldSourceRows", "relationshipRows"], values.PropertyNames());
            Assert.Equal(("CSV", 2, 1), (values.Text("format"), values.GetProperty("relationshipRows").GetInt32(), values.GetProperty("heldSourceRows").GetInt32()));
        });
    }

    // AC-FR-060-02, AC-NFR-001-01: no one but Talent Management reaches the register, its export or the goals view through
    // the API, under Entra and under the labelled simulation alike. A refused export is not recorded as an export.
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task Only_talent_management_reaches_the_register_its_export_and_the_goals_view(IdentityMode mode)
    {
        await using var api = await WorkflowApi.StartAsync(server, mode);
        await CallerMentorship.FormedAsync(api.Db, api.Callers);

        var mismatches = new List<string>();
        foreach (var route in Routes)
        {
            foreach (var profile in Enum.GetValues<Profile>().Cast<Profile?>().Append(null))
            {
                var expected = profile switch
                {
                    null => HttpStatusCode.Unauthorized,
                    Profile.Administrator => HttpStatusCode.OK,
                    _ => HttpStatusCode.Forbidden,
                };
                using var response = await api.SendAsync(HttpMethod.Get, route, profile);
                var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
                if (response.StatusCode != expected || (expected != HttpStatusCode.OK && body.Contains("Protégé", StringComparison.Ordinal)))
                {
                    mismatches.Add($"{profile?.ToString() ?? "anonymous"} GET {route}: {response.StatusCode}, expected {expected}");
                }
            }
        }

        Assert.Empty(mismatches);
        Assert.Equal(1L, await ExportEntryCountAsync(api.Db));
    }

    /// <summary>The row's review exception as its reason and its unresolved attributes, comma-separated in order.</summary>
    private static (string Reason, string Attributes) ReviewOf(JsonElement entry)
    {
        var review = entry.GetProperty("reviewException");
        return (review.Text("reason"), string.Join(',', review.Items("unresolvedAttributes").Select(attribute => attribute.GetString())));
    }
}

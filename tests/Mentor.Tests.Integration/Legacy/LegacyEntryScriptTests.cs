using System.Text.Json;
using Mentor.Core.Employees;
using Mentor.Infrastructure.Persistence;
using Mentor.Tests.Integration.Persistence;
using Mentor.Tests.Integration.Relationships;
using Mentor.Tests.Integration.Support;
using Npgsql;
using static Mentor.Tests.Integration.Legacy.LegacyEntryScript;

namespace Mentor.Tests.Integration.Legacy;

// Controlled manual entry of historical tracker rows through reviewed scripts made from the template in
// infra/legacy-entry/ (COMP-LEGACY-RECORDS, ADR-008, CONTRACT-LEGACY-ENTRY). A Talent Management administrator's own
// legacy-entry account enters each source row once. A row becomes a LEGACY relationship carrying its source and nothing
// invented, or a review exception. Every row is attributed to that administrator and audited as a legacy-source claim
// (FR-062 to FR-064, BR-016, BR-022, DR-001, DR-014, DR-016, NFR-003, NFR-004).
public sealed class LegacyEntryScriptTests(PostgresServer server)
{
    // AC-E2E-07, AC-FR-062-01, AC-FR-062-02, AC-FR-063-02, AC-FR-063-04, AC-FR-064-01, AC-BR-022-01, AC-DR-014-01,
    // AC-NFR-004-01: a validated row is entered with its real source and the entering administrator at the actual time.
    // "10/1/2026" is 10 January 2026, "ongoing" stays text with no date, and Active gives State only. IsActive and Status stay
    // unresolved, each a review exception. No candidate, agreement, acceptance, approval, goal or employee is made, and the
    // row is reported as legacy through the API and the trail. An administrator resolves an unknown later in the application,
    // and the exception stays as entered.
    [Fact]
    public async Task A_validated_row_is_entered_as_a_legacy_relationship_with_its_source_and_nothing_invented()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var (administrator, protege, mentor) = (api.Employee(Profile.Administrator), api.Employee(Profile.Employee), api.Employee(Profile.Mentor));
        await AuthoriseAsync(api.Db, Login.Alice, api.Callers.ObjectId(Profile.Administrator));
        var employees = await CountAsync(api.Db, "employee");
        var before = await api.Db.ScalarAsync<DateTime>(Login.Owner, "SELECT clock_timestamp()");

        await RunAsync(api.Db, Login.Alice, new TrackerRow(8, api.Callers.ObjectId(Profile.Employee), api.Callers.ObjectId(Profile.Mentor)));

        var after = await api.Db.ScalarAsync<DateTime>(Login.Owner, "SELECT clock_timestamp()");
        var entered = Assert.Single(await RowsAsync(api.Db, "SELECT * FROM mentor.mentorship_relationship"));
        var id = entered.Id();
        Assert.Equal(("LEGACY", null, protege, mentor, administrator),
            (Value(entered, "provenance"), Value(entered, "candidate_id"), entered.GetProperty("protege_employee_id").GetGuid(),
                entered.GetProperty("mentor_employee_id").GetGuid(), entered.GetProperty("entered_by_employee_id").GetGuid()));
        Assert.Equal(("COMPLETE", null, null, "SIX_MONTHS", "2026-01-10", null),
            (Value(entered, "state"), Value(entered, "status"), Value(entered, "is_active"), Value(entered, "duration_category"),
                Value(entered, "start_date"), Value(entered, "end_date")));
        Assert.Equal((Seed.SourceFile, "8", "T. Mokoena", "J. van der Merwe", "Successfully complete", "6 months", "10/1/2026", "ongoing"),
            (Value(entered, "source_file"), Value(entered, "source_row"), Value(entered, "source_protege_text"), Value(entered, "source_mentor_text"),
                Value(entered, "source_active_text"), Value(entered, "source_duration_text"), Value(entered, "source_start_text"), Value(entered, "source_end_text")));
        var enteredAt = entered.GetProperty("created_at").GetDateTimeOffset();
        Assert.InRange(enteredAt.UtcDateTime, before, after);
        Assert.Equal((0L, employees), (await DigitalEvidenceAsync(api.Db), await CountAsync(api.Db, "employee")));

        var exceptions = await RowsAsync(api.Db, "SELECT * FROM mentor.legacy_review_exception ORDER BY attribute");
        Assert.Equal([("IS_ACTIVE", "UNRESOLVED_VALUE"), ("STATUS", "UNRESOLVED_VALUE")], exceptions.Select(exception => (Value(exception, "attribute"), Value(exception, "reason"))));
        Assert.All(exceptions, exception => Assert.Equal((id, administrator, "8", "Successfully complete"),
            (exception.GetProperty("relationship_id").GetGuid(), exception.GetProperty("entered_by_employee_id").GetGuid(), Value(exception, "source_row"),
                Value(exception, "source_active_text"))));

        // Reported through the API as legacy, with its source and its unknowns as unknown.
        var view = await RelationshipApi.ReadAsync(api, id, Profile.Administrator);
        Assert.Equal(("Legacy", null, Seed.SourceFile, 8),
            (view.Text("provenance"), Value(view, "candidateId"), view.GetProperty("source").Text("file"), view.GetProperty("source").GetProperty("row").GetInt32()));
        Assert.Equal(("Complete", null, null, "SixMonths", "2026-01-10", null),
            (Value(view, "state"), Value(view, "status"), Value(view, "isActive"), Value(view, "durationCategory"), Value(view, "startDate"), Value(view, "endDate")));
        Assert.Equal(id, (await RelationshipApi.ReadAsync(api, id, Profile.Employee)).Id());

        // The trail: a legacy-source claim by the administrator's database account, at the time of entry, naming its source row.
        var entry = Assert.Single(await RelationshipApi.HistoryAsync(api, id));
        Assert.Equal(("LegacyEntry", "LegacySource", "Database", administrator, Seed.SourceFile, 8, enteredAt),
            (entry.Text("action"), entry.Text("provenance"), entry.GetProperty("actor").Text("identityScheme"), entry.GetProperty("actor").GetProperty("employeeId").GetGuid(),
                entry.Text("sourceFile"), entry.GetProperty("sourceRow").GetInt32(), entry.OptionalTime("occurredAt")!.Value));
        var values = entry.GetProperty("newValues");
        Assert.Equal(("COMPLETE", null, null, "SIX_MONTHS", "2026-01-10", null),
            (Value(values, "state"), Value(values, "status"), Value(values, "isActive"), Value(values, "durationCategory"), Value(values, "startDate"), Value(values, "endDate")));
        foreach (var exception in exceptions)
        {
            var held = Assert.Single((await api.JsonAsync(HttpMethod.Get, $"/api/audit-trail/records/LegacyReviewException/{exception.Id()}", Profile.Administrator))
                .EnumerateArray());
            Assert.Equal(("LegacyEntry", "LegacySource", administrator, "UNRESOLVED_VALUE", id, Value(exception, "attribute")),
                (held.Text("action"), held.Text("provenance"), held.GetProperty("actor").GetProperty("employeeId").GetGuid(), Value(held.GetProperty("newValues"), "reason"),
                    held.GetProperty("newValues").GetProperty("relationshipId").GetGuid(), Value(held.GetProperty("newValues"), "attribute")));
        }

        var resolved = await RelationshipApi.EditAsync(api, view, new { isActive = "N" });

        Assert.Equal("N", resolved.Text("isActive"));
        Assert.Equal(exceptions.Select(exception => exception.GetRawText()),
            (await RowsAsync(api.Db, "SELECT * FROM mentor.legacy_review_exception ORDER BY attribute")).Select(exception => exception.GetRawText()));
    }

    // AC-DR-016-01, AC-DR-016-02, AC-FR-063-03, AC-NFR-003-01, AC-DR-001-01: a row whose person is not resolved to an
    // employee is held whole as a review exception, with its source and every cell as written. That covers a directory
    // identity with no employee record, a person named but not resolved, and nobody resolved at all. Nobody is invented,
    // and no relationship or digital evidence is written.
    [Fact]
    public async Task A_row_whose_person_is_not_resolved_to_an_employee_is_held_as_a_review_exception_and_nobody_is_invented()
    {
        await using var db = await server.CreateDatabaseAsync();
        var (administrator, administratorObjectId) = await PersonAsync(db, "Administrator");
        var (_, protege) = await PersonAsync(db, "Protégé");
        var (_, mentor) = await PersonAsync(db, "Mentor");
        await AuthoriseAsync(db, Login.Alice, administratorObjectId);
        var employees = await CountAsync(db, "employee");

        await RunAsync(db, Login.Alice,
            new TrackerRow(20, Guid.NewGuid(), mentor, Protege: "A. Departed"),
            new TrackerRow(21, protege, null, Mentor: "B. Unresolved"),
            new TrackerRow(22, null, null, Protege: "C. Unresolved", Mentor: "D. Unresolved"));

        var held = await RowsAsync(db, "SELECT * FROM mentor.legacy_review_exception ORDER BY source_row");
        Assert.Equal([("20", "UNRESOLVED_PERSON"), ("21", "UNRESOLVED_PERSON"), ("22", "UNRESOLVED_PERSON")],
            held.Select(exception => (Value(exception, "source_row"), Value(exception, "reason"))));
        Assert.Equal([("A. Departed", "J. van der Merwe"), ("T. Mokoena", "B. Unresolved"), ("C. Unresolved", "D. Unresolved")],
            held.Select(exception => (Value(exception, "source_protege_text"), Value(exception, "source_mentor_text"))));
        Assert.All(held, exception => Assert.Equal((null, null, administrator, Seed.SourceFile, "Successfully complete", "6 months", "10/1/2026", "ongoing"),
            (Value(exception, "relationship_id"), Value(exception, "attribute"), exception.GetProperty("entered_by_employee_id").GetGuid(), Value(exception, "source_file"),
                Value(exception, "source_active_text"), Value(exception, "source_duration_text"), Value(exception, "source_start_text"), Value(exception, "source_end_text"))));
        Assert.Equal((0L, 0L, employees), (await CountAsync(db, "mentorship_relationship"), await DigitalEvidenceAsync(db), await CountAsync(db, "employee")));

        var audited = await RowsAsync(db, "SELECT * FROM mentor.audit_entry ORDER BY source_row");
        Assert.Equal(held.Select(exception => (exception.Id(), Value(exception, "source_row"))), audited.Select(entry => (entry.GetProperty("record_id").GetGuid(), Value(entry, "source_row"))));
        Assert.All(audited, entry => Assert.Equal(("LEGACY_ENTRY", "LEGACY_REVIEW_EXCEPTION", "LEGACY_SOURCE", "DATABASE", administrator, "UNRESOLVED_PERSON"),
            (Value(entry, "action"), Value(entry, "record_type"), Value(entry, "provenance"), Value(entry, "identity_scheme"), entry.GetProperty("actor_employee_id").GetGuid(),
                Value(entry.GetProperty("new_values"), "reason"))));
    }

    // Specification (legacy tracker rows 9 and 18), AC-FR-063-03, AC-FR-063-04: a row with no mentor is held as a review
    // exception; no mentor, agreement, acceptance or relationship is made up for it.
    [Fact]
    public async Task A_row_without_a_mentor_is_held_as_a_review_exception()
    {
        await using var db = await server.CreateDatabaseAsync();
        var (_, administratorObjectId) = await PersonAsync(db, "Administrator");
        var (_, protege) = await PersonAsync(db, "Protégé");
        await AuthoriseAsync(db, Login.Alice, administratorObjectId);

        await RunAsync(db, Login.Alice, new TrackerRow(9, protege, null, Mentor: null), new TrackerRow(18, protege, null, Mentor: "  ", Active: "Yes"));

        Assert.Equal([("9", "NO_MENTOR", null, "Successfully complete"), ("18", "NO_MENTOR", "  ", "Yes")],
            (await RowsAsync(db, "SELECT * FROM mentor.legacy_review_exception ORDER BY source_row"))
            .Select(exception => (Value(exception, "source_row"), Value(exception, "reason"), Value(exception, "source_mentor_text"), Value(exception, "source_active_text"))));
        Assert.Equal((0L, 0L), (await CountAsync(db, "mentorship_relationship"), await DigitalEvidenceAsync(db)));
    }

    // AC-BR-022-01, AC-BR-022-02, AC-FR-063-02: only a legacy-entry account authorised for its Talent Management
    // administrator enters rows, each attributed to that administrator. An unauthorised account is refused, and so is the
    // application's account, which can neither run the procedure nor authorise itself. A refused script enters nothing.
    [Fact]
    public async Task Only_an_authorised_account_enters_rows_and_each_is_attributed_to_its_administrator()
    {
        await using var db = await server.CreateDatabaseAsync();
        var (alice, aliceObjectId) = await PersonAsync(db, "Alice");
        var (bob, bobObjectId) = await PersonAsync(db, "Bob");
        var (_, protege) = await PersonAsync(db, "Protégé");
        var (_, mentor) = await PersonAsync(db, "Mentor");

        const string procedure = "mentor.enter_legacy_row(text, integer, uuid, uuid, text, text, text, text, text, text)";
        Assert.Equal((true, false), (
            await db.ScalarAsync<bool>(Login.Owner, $"SELECT has_function_privilege('mentor_legacy_entry', '{procedure}', 'EXECUTE')"),
            await db.ScalarAsync<bool>(Login.Owner, $"SELECT has_function_privilege('mentor_app', '{procedure}', 'EXECUTE')")));
        await Rejected.WithAsync(PostgresErrorCodes.InsufficientPrivilege, () => RunAsync(db, Login.Bob, new TrackerRow(8, protege, mentor)));
        await Rejected.WithAsync(PostgresErrorCodes.InsufficientPrivilege, () => RunAsync(db, Login.App, new TrackerRow(8, protege, mentor)));
        await Rejected.WithAsync(PostgresErrorCodes.InsufficientPrivilege, () => AuthoriseAsync(db, Login.App, aliceObjectId));
        Assert.Equal((0L, 0L, 0L, 0L),
            (await CountAsync(db, "mentorship_relationship"), await CountAsync(db, "legacy_review_exception"), await CountAsync(db, "audit_entry"),
                await CountAsync(db, "legacy_entry_authorisation")));

        await AuthoriseAsync(db, Login.Alice, aliceObjectId);
        await AuthoriseAsync(db, Login.Bob, bobObjectId);
        await RunAsync(db, Login.Alice, new TrackerRow(8, protege, mentor));
        await RunAsync(db, Login.Bob, new TrackerRow(9, protege, null, Mentor: null));

        Assert.Equal(alice, Assert.Single(await RowsAsync(db, "SELECT * FROM mentor.mentorship_relationship")).GetProperty("entered_by_employee_id").GetGuid());
        Assert.Equal([("8", alice), ("8", alice), ("9", bob)],
            (await RowsAsync(db, "SELECT * FROM mentor.legacy_review_exception ORDER BY source_row"))
            .Select(exception => (Value(exception, "source_row"), exception.GetProperty("entered_by_employee_id").GetGuid())));
        Assert.Equal([("8", alice), ("8", alice), ("8", alice), ("9", bob)],
            (await RowsAsync(db, "SELECT * FROM mentor.audit_entry ORDER BY source_row"))
            .Select(entry => (Value(entry, "source_row"), entry.GetProperty("actor_employee_id").GetGuid())));
    }

    // AC-FR-062-01, AC-FR-062-02, AC-FR-063-02: each cell reaches its own column as the readings read it. Duration, Start
    // and End are each read from their own cell, State or Status from Active, and every other value stays unresolved with
    // its review exception.
    [Fact]
    public async Task Each_cell_reaches_its_own_column_as_the_readings_read_it()
    {
        await using var db = await server.CreateDatabaseAsync();
        var (_, administratorObjectId) = await PersonAsync(db, "Administrator");
        var (_, protege) = await PersonAsync(db, "Protégé");
        var (_, mentor) = await PersonAsync(db, "Mentor");
        await AuthoriseAsync(db, Login.Alice, administratorObjectId);

        await RunAsync(db, Login.Alice,
            new TrackerRow(30, protege, mentor, Active: "put on hold", Duration: "12+ months", Start: "Jan 2026", End: "31/12/2026"),
            new TrackerRow(31, protege, mentor, Active: "No - needs reassigning\r\n", Duration: "ongoing", Start: "01/02/2025", End: ""));

        var entered = await RowsAsync(db, "SELECT * FROM mentor.mentorship_relationship ORDER BY source_row");
        Assert.Equal([("ON_HOLD", null, null, "TWELVE_PLUS_MONTHS", null, "2026-12-31"), (null, "NEEDS_REASSIGNING", null, null, "2025-02-01", null)],
            entered.Select(row => (Value(row, "state"), Value(row, "status"), Value(row, "is_active"), Value(row, "duration_category"),
                Value(row, "start_date"), Value(row, "end_date"))));
        Assert.Equal([("12+ months", "Jan 2026", "31/12/2026"), ("ongoing", "01/02/2025", "")],
            entered.Select(row => (Value(row, "source_duration_text"), Value(row, "source_start_text"), Value(row, "source_end_text"))));
        Assert.Equal([("30", "IS_ACTIVE"), ("30", "STATUS"), ("31", "IS_ACTIVE"), ("31", "STATE")],
            (await RowsAsync(db, "SELECT * FROM mentor.legacy_review_exception ORDER BY source_row, attribute"))
            .Select(exception => (Value(exception, "source_row"), Value(exception, "attribute"))));
    }

    // Specification (legacy tracker rows 9 and 18), ADR-008, AC-FR-063-03, AC-NFR-003-01: a row is NO_MENTOR by its Mentor
    // cell alone, including a cell holding only a copied line break. An identity for a person whose cell is empty is refused,
    // and the whole script with it, so no mentor is ever invented for such a row. A row held as NO_MENTOR is never entered
    // later.
    [Fact]
    public async Task No_identity_is_given_to_a_person_the_row_does_not_name_and_a_row_without_a_mentor_stays_held()
    {
        await using var db = await server.CreateDatabaseAsync();
        var (_, administratorObjectId) = await PersonAsync(db, "Administrator");
        var (_, protege) = await PersonAsync(db, "Protégé");
        var (_, mentor) = await PersonAsync(db, "Mentor");
        await AuthoriseAsync(db, Login.Alice, administratorObjectId);

        await Rejected.WithAsync(PostgresErrorCodes.InvalidParameterValue,
            () => RunAsync(db, Login.Alice, new TrackerRow(8, protege, mentor), new TrackerRow(9, protege, mentor, Mentor: null)));
        await Rejected.WithAsync(PostgresErrorCodes.InvalidParameterValue, () => RunAsync(db, Login.Alice, new TrackerRow(10, protege, mentor, Protege: " ")));
        Assert.Equal((0L, 0L), (await CountAsync(db, "mentorship_relationship"), await CountAsync(db, "legacy_review_exception")));

        await RunAsync(db, Login.Alice, new TrackerRow(9, protege, null, Mentor: "\r\n"));
        var held = Assert.Single(await RowsAsync(db, "SELECT * FROM mentor.legacy_review_exception"));
        Assert.Equal(("NO_MENTOR", "\r\n"), (Value(held, "reason"), Value(held, "source_mentor_text")));

        await Rejected.WithAsync(PostgresErrorCodes.UniqueViolation,
            () => RunAsync(db, Login.Alice, new TrackerRow(9, protege, mentor, Mentor: "J. van der Merwe")));
        Assert.Equal((0L, 1L), (await CountAsync(db, "mentorship_relationship"), await CountAsync(db, "legacy_review_exception")));
    }

    // ADR-008: a source row is entered once, and a script enters all of its rows or none. Scripts holding the same row at
    // once hold it once, and a source file is named exactly. A row held because its person had no employee record is
    // entered later, once they have one, and its review exception stays as evidence.
    [Fact]
    public async Task A_source_row_is_entered_once_and_a_script_enters_all_of_its_rows_or_none()
    {
        await using var db = await server.CreateDatabaseAsync();
        var (_, administratorObjectId) = await PersonAsync(db, "Administrator");
        var (_, protege) = await PersonAsync(db, "Protégé");
        var (_, mentor) = await PersonAsync(db, "Mentor");
        await AuthoriseAsync(db, Login.Alice, administratorObjectId);
        await RunAsync(db, Login.Alice, new TrackerRow(8, protege, mentor), new TrackerRow(9, protege, null, Mentor: null));
        var entries = await CountAsync(db, "audit_entry");

        await Rejected.WithAsync(PostgresErrorCodes.UniqueViolation,
            () => RunAsync(db, Login.Alice, new TrackerRow(10, protege, mentor), new TrackerRow(8, protege, mentor)));
        await Rejected.WithAsync(PostgresErrorCodes.UniqueViolation, () => RunAsync(db, Login.Alice, new TrackerRow(9, protege, null, Mentor: null)));
        await Rejected.WithAsync(PostgresErrorCodes.InvalidParameterValue,
            () => RunAsync(db, Login.Alice, new TrackerRow(8, protege, mentor, SourceFile: $"{Seed.SourceFile} ")));
        var attempts = await Task.WhenAll(Enumerable.Range(0, 2)
            .Select(_ => Record.ExceptionAsync(() => RunAsync(db, Login.Alice, new TrackerRow(40, protege, null, Mentor: null))).AsTask()));
        Assert.Single(attempts, attempt => attempt is null);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(Assert.Single(attempts, attempt => attempt is not null)).SqlState);
        Assert.Equal(1L, await db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.legacy_review_exception WHERE source_row = 40"));
        entries++;

        Assert.Equal(["8"], (await RowsAsync(db, "SELECT source_row FROM mentor.mentorship_relationship")).Select(row => Value(row, "source_row")));
        Assert.Equal((1L, entries), (await db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.legacy_review_exception WHERE source_row = 9"),
            await CountAsync(db, "audit_entry")));

        var newcomer = Guid.NewGuid();
        await RunAsync(db, Login.Alice, new TrackerRow(11, newcomer, mentor, Protege: "E. Newcomer"));
        var held = Assert.Single(await RowsAsync(db, "SELECT * FROM mentor.legacy_review_exception WHERE source_row = 11"));
        Assert.Equal("UNRESOLVED_PERSON", Value(held, "reason"));
        await db.ExecuteAsync(Login.Owner, "INSERT INTO mentor.employee (id, entra_object_id, display_name) VALUES (gen_random_uuid(), $1, 'E. Newcomer')", newcomer);

        await RunAsync(db, Login.Alice, new TrackerRow(11, newcomer, mentor, Protege: "E. Newcomer"));

        Assert.Equal(["8", "11"], (await RowsAsync(db, "SELECT source_row FROM mentor.mentorship_relationship ORDER BY source_row")).Select(row => Value(row, "source_row")));
        Assert.Equal(held.GetRawText(),
            Assert.Single(await RowsAsync(db, "SELECT * FROM mentor.legacy_review_exception WHERE source_row = 11 AND relationship_id IS NULL")).GetRawText());
    }

    // AC-FR-063-01, AC-FR-063-04, AC-NFR-004-01: entry needs no importer, screen or API. The procedure takes only the cells and
    // the resolved people, so no State, Status, IsActive, date, entrant or time can be supplied. No application assembly
    // reads a spreadsheet.
    [Fact]
    public async Task Entry_takes_only_the_cells_and_the_resolved_people_and_needs_no_importer()
    {
        await using var db = await server.CreateDatabaseAsync();

        Assert.Equal(
            "source_file text, source_row integer, protege_object_id uuid, mentor_object_id uuid, source_protege_text text, source_mentor_text text, "
                + "source_active_text text, source_duration_text text, source_start_text text, source_end_text text",
            await db.ScalarAsync<string>(Login.Owner, "SELECT pg_get_function_identity_arguments('mentor.enter_legacy_row'::regproc)"));
        string[] spreadsheets = ["ClosedXML", "EPPlus", "NPOI", "ExcelDataReader", "DocumentFormat.OpenXml", "Aspose.Cells", "GemBox.Spreadsheet", "Syncfusion.XlsIO"];
        var referenced = new[] { typeof(Program).Assembly, typeof(Employee).Assembly, typeof(MentorDbContext).Assembly }
            .SelectMany(assembly => assembly.GetReferencedAssemblies())
            .Select(assembly => assembly.Name!);
        Assert.DoesNotContain(referenced, name => spreadsheets.Any(library => name.Contains(library, StringComparison.OrdinalIgnoreCase)));
    }

    private static string? Value(JsonElement row, string column) =>
        row.GetProperty(column) is { ValueKind: not JsonValueKind.Null } value ? value.ToString() : null;
}

using System.Globalization;
using System.Text;
using System.Text.Json;
using Mentor.Tests.Integration.Persistence;
using Npgsql;

namespace Mentor.Tests.Integration.Legacy;

/// <summary>
/// One tracker row as a reviewer transcribes it: the cells exactly as they read, and the Entra object identifiers the
/// people were resolved to, or none.
/// </summary>
internal sealed record TrackerRow(
    int Row,
    Guid? ProtegeObjectId,
    Guid? MentorObjectId,
    string? Protege = "T. Mokoena",
    string? Mentor = "J. van der Merwe",
    string? Active = "Successfully complete",
    string? Duration = "6 months",
    string? Start = "10/1/2026",
    string? End = "ongoing",
    string SourceFile = Seed.SourceFile);

/// <summary>
/// Entry scripts made from the real template in infra/legacy-entry/, filled in as its README says, and run as a database
/// account the way psql runs them: the whole script at once, one transaction.
/// </summary>
internal static class LegacyEntryScript
{
    private const string RowBegin = "-- row:begin";
    private const string RowEnd = "-- row:end";

    public static string Folder { get; } = Path.Combine(RepositoryRoot(), "infra", "legacy-entry");

    /// <summary>The template with its header filled in and one call per row.</summary>
    public static string Render(params TrackerRow[] rows)
    {
        var template = File.ReadAllText(Path.Combine(Folder, "entry-script.template.sql"));
        var begin = template.IndexOf(RowBegin, StringComparison.Ordinal);
        var end = template.IndexOf(RowEnd, StringComparison.Ordinal) + RowEnd.Length;
        Assert.True(begin >= 0 && end > begin, "The template marks the call to repeat for each row.");
        var header = template[..begin]
            .Replace("{{script_version}}", "Rows under test, v1", StringComparison.Ordinal)
            .Replace("{{batch_source}}", $"{Seed.SourceFile}, rows under test", StringComparison.Ordinal)
            .Replace("{{prepared_by}}", "Test administrator", StringComparison.Ordinal)
            .Replace("{{reviewed_by}}", "Second test administrator", StringComparison.Ordinal);
        var script = header + string.Join('\n', rows.Select(row => Fill(template[begin..end], row))) + template[end..];
        Assert.DoesNotContain("{{", script, StringComparison.Ordinal);
        return script;
    }

    /// <summary>authorise-account.sql filled in with the administrator's Entra object identifier.</summary>
    public static string Authorisation(Guid administratorObjectId)
    {
        var script = File.ReadAllText(Path.Combine(Folder, "authorise-account.sql"))
            .Replace("{{administrator_object_id}}", Literal(administratorObjectId), StringComparison.Ordinal);
        Assert.DoesNotContain("{{", script, StringComparison.Ordinal);
        return script;
    }

    /// <summary>
    /// Runs the rendered script as <c>psql -X --set ON_ERROR_STOP=1 --file</c> does: statement by statement on one
    /// connection of its own, stopping at the first refusal. The connection then closes, which rolls back a transaction
    /// the script left open. So whatever the template itself commits, and only that, persists.
    /// </summary>
    public static async Task RunAsync(TestDatabase db, Login login, params TrackerRow[] rows)
    {
        var unpooled = new NpgsqlConnectionStringBuilder(db.ConnectionString(login)) { Pooling = false };
        await using var connection = new NpgsqlConnection(unpooled.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        foreach (var statement in Statements(Render(rows)))
        {
            await using var command = new NpgsqlCommand(statement, connection);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
    }

    public static Task<int> AuthoriseAsync(TestDatabase db, Login login, Guid administratorObjectId) =>
        db.ExecuteAsync(login, Authorisation(administratorObjectId));

    /// <summary>An employee as first sign-in records one, with the Entra object identifier a reviewer resolves them to.</summary>
    public static async Task<(Guid Employee, Guid ObjectId)> PersonAsync(TestDatabase db, string name)
    {
        var objectId = Guid.NewGuid();
        var employee = await db.ScalarAsync<Guid>(Login.Owner,
            "INSERT INTO mentor.employee (id, entra_object_id, display_name) VALUES (gen_random_uuid(), $1, $2) RETURNING id", objectId, name);
        return (employee, objectId);
    }

    /// <summary>The rows <paramref name="query"/> selects, each as the JSON of all its columns.</summary>
    public static async Task<List<JsonElement>> RowsAsync(TestDatabase db, string query, params object?[] parameters)
    {
        var json = await db.ScalarAsync<string?>(Login.Owner, $"SELECT coalesce(json_agg(row_to_json(selected)), '[]')::text FROM ({query}) AS selected", parameters);
        return [.. JsonSerializer.Deserialize<JsonElement>(json!).EnumerateArray()];
    }

    /// <summary>How many rows of the tables formation writes exist: none may exist for a legacy entry.</summary>
    public static Task<long> DigitalEvidenceAsync(TestDatabase db) =>
        db.ScalarAsync<long>(Login.Owner, """
            SELECT (SELECT count(*) FROM mentor.mentor_selection) + (SELECT count(*) FROM mentor.agreement_revision)
                 + (SELECT count(*) FROM mentor.agreement_acceptance) + (SELECT count(*) FROM mentor.formation_decision)
                 + (SELECT count(*) FROM mentor.formed_agreement) + (SELECT count(*) FROM mentor.development_goal)
            """);

    public static Task<long> CountAsync(TestDatabase db, string table) =>
        db.ScalarAsync<long>(Login.Owner, $"SELECT count(*) FROM mentor.{table}");

    private static string Fill(string block, TrackerRow row) => block
        .Replace("{{source_file}}", Literal(row.SourceFile), StringComparison.Ordinal)
        .Replace("{{source_row}}", row.Row.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("{{protege_object_id}}", Literal(row.ProtegeObjectId), StringComparison.Ordinal)
        .Replace("{{mentor_object_id}}", Literal(row.MentorObjectId), StringComparison.Ordinal)
        .Replace("{{source_protege_text}}", Literal(row.Protege), StringComparison.Ordinal)
        .Replace("{{source_mentor_text}}", Literal(row.Mentor), StringComparison.Ordinal)
        .Replace("{{source_active_text}}", Literal(row.Active), StringComparison.Ordinal)
        .Replace("{{source_duration_text}}", Literal(row.Duration), StringComparison.Ordinal)
        .Replace("{{source_start_text}}", Literal(row.Start), StringComparison.Ordinal)
        .Replace("{{source_end_text}}", Literal(row.End), StringComparison.Ordinal);

    /// <summary>
    /// The script's statements in order, split as psql splits a file: at each semicolon outside quoted text and outside
    /// "--" comments. The template uses no other quoting.
    /// </summary>
    private static List<string> Statements(string script)
    {
        var statements = new List<string>();
        var statement = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < script.Length; index++)
        {
            var character = script[index];
            if (!quoted && character == '-' && index + 1 < script.Length && script[index + 1] == '-')
            {
                var lineEnd = script.IndexOf('\n', index);
                index = lineEnd < 0 ? script.Length : lineEnd;
                statement.Append('\n');
                continue;
            }

            if (character == '\'')
            {
                quoted = !quoted;
            }

            if (character == ';' && !quoted)
            {
                Add();
                continue;
            }

            statement.Append(character);
        }

        Add();
        return statements;

        void Add()
        {
            if (statement.ToString().Trim() is { Length: > 0 } text)
            {
                statements.Add(text);
            }

            statement.Clear();
        }
    }

    private static string Literal(string? text) => text is null ? "NULL" : $"'{text.Replace("'", "''", StringComparison.Ordinal)}'";

    private static string Literal(Guid? id) => id is { } value ? $"'{value}'" : "NULL";

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "Mentor.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("No repository root above the test assembly.");
    }
}

using System.Net;
using System.Text;
using System.Text.Json;
using Mentor.Api.Register;
using Mentor.Tests.Integration.Persistence;
using Mentor.Tests.Integration.Support;

namespace Mentor.Tests.Integration.Register;

/// <summary>The register's routes, the calls the register tests make of them and a reader for the CSV export.</summary>
internal static class RegisterApi
{
    public const string Register = RegisterEndpoints.RegisterRoute;
    public const string Export = RegisterEndpoints.ExportRoute;
    public const string Goals = RegisterEndpoints.GoalsRoute;

    public static IReadOnlyList<string> Routes { get; } = [Register, Export, Goals];

    /// <summary>The register as Talent Management reads it, in register order.</summary>
    public static async Task<List<JsonElement>> ReadAsync(WorkflowApi api) =>
        [.. (await api.JsonAsync(HttpMethod.Get, Register, Profile.Administrator)).EnumerateArray()];

    /// <summary>The goals and progress view as Talent Management reads it, in register order.</summary>
    public static async Task<List<JsonElement>> GoalsAsync(WorkflowApi api) =>
        [.. (await api.JsonAsync(HttpMethod.Get, Goals, Profile.Administrator)).EnumerateArray()];

    /// <summary>Talent Management's export, which must succeed: the file's bytes, byte-order mark included.</summary>
    public static async Task<byte[]> ExportAsync(WorkflowApi api)
    {
        using var response = await api.SendAsync(HttpMethod.Get, Export, Profile.Administrator);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>The export's records, header first, each as its fields, read as RFC 4180 reads them after the byte-order mark.</summary>
    public static List<string[]> Records(byte[] file)
    {
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, file[..3]);
        var text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(file[3..]);
        Assert.EndsWith("\r\n", text, StringComparison.Ordinal);
        var records = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (quoted)
            {
                if (character == '"' && index + 1 < text.Length && text[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else if (character == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(character);
                }
            }
            else if (character == '"')
            {
                quoted = true;
            }
            else if (character == ',')
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
            {
                fields.Add(field.ToString());
                field.Clear();
                records.Add([.. fields]);
                fields.Clear();
                index++;
            }
            else
            {
                field.Append(character);
            }
        }

        Assert.False(quoted, "Every quoted field is closed.");
        return records;
    }

    /// <summary>The register's export entries as Talent Management reads them in the trail, oldest first.</summary>
    public static async Task<List<JsonElement>> ExportEntriesAsync(WorkflowApi api) =>
        [.. (await api.JsonAsync(HttpMethod.Get, "/api/audit-trail/records/Register", Profile.Administrator)).EnumerateArray()];

    public static Task<long> ExportEntryCountAsync(TestDatabase db) =>
        db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.audit_entry WHERE action = 'REGISTER_EXPORTED'");

    public static string? Value(JsonElement element, string property) =>
        element.GetProperty(property).ValueKind == JsonValueKind.Null ? null : element.GetProperty(property).ToString();
}

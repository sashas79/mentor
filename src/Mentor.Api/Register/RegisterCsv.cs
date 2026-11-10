using System.Globalization;
using System.Text;
using Mentor.Core.Legacy;
using Mentor.Core.Relationships;

namespace Mentor.Api.Register;

/// <summary>
/// The spreadsheet-compatible export of the register (CONTRACT-REGISTER-EXPORT, ADR-011): UTF-8 with a byte-order
/// mark, a header row, one CRLF-terminated record per register row, and fields quoted as RFC 4180 requires. These are the
/// columns, in order:
/// <list type="number">
/// <item>Mentor, Mentor level, Protégé, Protégé level;</item>
/// <item>State, Status, IsActive;</item>
/// <item>Start date, End date (ISO 8601) and Duration;</item>
/// <item>Provenance and Source reference;</item>
/// <item>Source Active text, Source Duration text, Source date text;</item>
/// <item>Review exception.</item>
/// </list>
/// Values are business values as the BRS names them. An unknown value is an empty cell, and so is a BLANK Status. The
/// Provenance and Review exception columns, with the source text beside them, tell an unknown from a blank. A held
/// source row shows the names its source gives in the Mentor and Protégé columns and leaves every resolved column empty.
/// No goal, progress, agreement content or meeting content is written.
/// </summary>
internal static class RegisterCsv
{
    public const string ContentType = "text/csv; charset=utf-8";

    public static readonly IReadOnlyList<string> Columns =
    [
        "Mentor", "Mentor level", "Protégé", "Protégé level", "State", "Status", "IsActive", "Start date", "End date", "Duration",
        "Provenance", "Source reference", "Source Active text", "Source Duration text", "Source date text", "Review exception",
    ];

    private static readonly UTF8Encoding Utf8WithMark = new(encoderShouldEmitUTF8Identifier: true);

    /// <summary>The export file's bytes, byte-order mark first.</summary>
    public static byte[] Write(IReadOnlyList<RegisterEntryView> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var csv = new StringBuilder();
        AppendRecord(csv, Columns);
        foreach (var entry in entries)
        {
            AppendRecord(csv, Fields(entry));
        }

        return [.. Utf8WithMark.GetPreamble(), .. Utf8WithMark.GetBytes(csv.ToString())];
    }

    /// <summary>The cells of one row, in column order, before quoting.</summary>
    public static IReadOnlyList<string> Fields(RegisterEntryView entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var source = entry.Source;
        return
        [
            entry.Mentor?.DisplayName ?? (entry.Kind == RegisterEntryKind.HeldSourceRow ? source?.MentorText : null) ?? string.Empty,
            entry.Mentor?.Level ?? string.Empty,
            entry.Protege?.DisplayName ?? (entry.Kind == RegisterEntryKind.HeldSourceRow ? source?.ProtegeText : null) ?? string.Empty,
            entry.Protege?.Level ?? string.Empty,
            State(entry.State),
            Status(entry.Status),
            entry.IsActive?.ToString() ?? string.Empty,
            Date(entry.StartDate),
            Date(entry.EndDate),
            Duration(entry.DurationCategory),
            entry.Provenance.ToString(),
            source is null ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"{source.File}, row {source.Row}"),
            source?.ActiveText ?? string.Empty,
            source?.DurationText ?? string.Empty,
            source is null ? string.Empty : DateText(source.StartText, source.EndText),
            Review(entry.ReviewException),
        ];
    }

    /// <summary>
    /// The field as written. A field that would start a spreadsheet formula (=, +, -, @, a tab or a carriage return) is
    /// prefixed with an apostrophe so that it opens as text and is never evaluated: a name or a source cell is someone's
    /// text, not a formula. A field containing a comma, a quote, a carriage return or a line feed is quoted, with each
    /// quote doubled.
    /// </summary>
    public static string Escape(string field)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (field.Length > 0 && field[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            field = "'" + field;
        }

        return field.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? "\"" + field.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : field;
    }

    private static void AppendRecord(StringBuilder csv, IReadOnlyList<string> fields)
    {
        csv.AppendJoin(',', fields.Select(Escape));
        csv.Append("\r\n");
    }

    private static string State(RelationshipState? state) => state switch
    {
        RelationshipState.InProgress => "In Progress",
        RelationshipState.OnHold => "On hold",
        RelationshipState.Complete => "Complete",
        null => string.Empty,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Not a State of FR-044."),
    };

    // Blank is a business value that reads as an empty cell, exactly as the tracker shows it (FR-045).
    private static string Status(RelationshipStatus? status) => status switch
    {
        RelationshipStatus.NeedsReassigning => "Needs reassigning",
        RelationshipStatus.Blank or null => string.Empty,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Not a Status of FR-045."),
    };

    private static string Duration(DurationCategory? duration) => duration switch
    {
        DurationCategory.SixMonths => "6 months",
        DurationCategory.TwelveMonths => "12 months",
        DurationCategory.TwelvePlusMonths => "12+ months",
        null => string.Empty,
        _ => throw new ArgumentOutOfRangeException(nameof(duration), duration, "Not a duration category of BR-010."),
    };

    private static string Date(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;

    // The Start and End cells exactly as entered, each named, so that neither is mistaken for the other.
    private static string DateText(string? start, string? end)
    {
        var parts = new List<string>(2);
        if (!string.IsNullOrEmpty(start))
        {
            parts.Add("Start: " + start);
        }

        if (!string.IsNullOrEmpty(end))
        {
            parts.Add("End: " + end);
        }

        return string.Join("; ", parts);
    }

    private static string Review(RegisterReviewView? review) => review switch
    {
        null => string.Empty,
        { Reason: LegacyReviewReason.UnresolvedPerson } => "Unresolved person",
        { Reason: LegacyReviewReason.NoMentor } => "No mentor",
        { Reason: LegacyReviewReason.UnresolvedValue } => "Unresolved value: " + string.Join(", ", review.UnresolvedAttributes),
        _ => throw new ArgumentOutOfRangeException(nameof(review), review.Reason, "Not a review reason of ADR-008."),
    };
}

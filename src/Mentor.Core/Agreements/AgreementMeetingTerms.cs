using System.Text.Json;

namespace Mentor.Core.Agreements;

/// <summary>
/// The meeting terms of an agreement content document (CONTRACT-AGREEMENT-CONTENT, FR-055): the planned interval
/// between meetings in weeks and the approximate minutes per meeting, held under the terms section as
/// <see cref="MeetingIntervalWeeksProperty"/> and <see cref="ApproximateMinutesPerMeetingProperty"/>. They are copied
/// onto every planned session of the formed relationship (ADR-010), so the schedule shows what both participants
/// accepted. A document without them, or one whose values are not positive whole numbers, has none.
/// </summary>
public sealed record AgreementMeetingTerms(int? FrequencyWeeks, int? DurationMinutes)
{
    public const string TermsProperty = "terms";
    public const string MeetingIntervalWeeksProperty = "meetingIntervalWeeks";
    public const string ApproximateMinutesPerMeetingProperty = "approximateMinutesPerMeeting";

    public static readonly AgreementMeetingTerms None = new(null, null);

    /// <summary>Reads the terms of the canonical JSON content of a revision or formed agreement.</summary>
    public static AgreementMeetingTerms Of(string content)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(content);
        }
        catch (JsonException)
        {
            return None;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty(TermsProperty, out var terms)
                || terms.ValueKind != JsonValueKind.Object)
            {
                return None;
            }

            return new AgreementMeetingTerms(PositiveInteger(terms, MeetingIntervalWeeksProperty), PositiveInteger(terms, ApproximateMinutesPerMeetingProperty));
        }
    }

    private static int? PositiveInteger(JsonElement terms, string property) =>
        terms.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number > 0
            ? number
            : null;
}

using System.Text.Json;
using System.Text.Json.Serialization;
using Mentor.Api.Agreements;
using Mentor.Core.Agreements;
using Mentor.Core.Relationships;

namespace Mentor.Tests.Unit.Agreements;

// ADR-004, FR-068, BR-012, BR-018: a revision body is the entered values alone; the parties and the policy are the
// server's, and no body has a place for a signature, a scanned document or meeting notes. An acceptance names the hash
// alone and an annex change its basis and value alone. A body naming anything else, at any level, is malformed.
public sealed class DraftBodiesTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private const string Complete = """
        {
          "terms": {
            "durationCategory": "TwelvePlusMonths", "startDate": "2026-12-01", "endDate": null, "meetingIntervalWeeks": 2,
            "approximateMinutesPerMeeting": 45, "betweenMeetingContact": { "interval": 3, "unit": "Days" },
            "missedMeetingArrangements": "Propose a new date", "reviewDate": "2027-03-01"
          },
          "goals": ["Lead a design review"],
          "barriers": ["Delivery deadlines"],
          "contingencies": ["Move the meeting"],
          "responsibilities": { "mentor": ["Share experience"], "protege": ["Prepare an agenda"] }
        }
        """;

    [Fact]
    public void A_revision_body_names_the_entered_values()
    {
        var form = JsonSerializer.Deserialize<AgreementDraftForm>(Complete, Web)!;

        Assert.Equal(
            new AgreementTermsForm(DurationCategory.TwelvePlusMonths, new DateOnly(2026, 12, 1), null, 2, 45, new ContactLimitForm(3, ContactUnit.Days),
                "Propose a new date", new DateOnly(2027, 3, 1)),
            form.Terms);
        Assert.Equal(["Lead a design review"], form.Goals!);
        Assert.Equal(["Share experience"], form.Responsibilities!.Mentor!);
        Assert.Equal(["Prepare an agenda"], form.Responsibilities!.Protege!);
    }

    [Theory]
    [InlineData("\"goals\":", "\"parties\":{\"protege\":\"P\"},\"goals\":")]
    [InlineData("\"goals\":", "\"policy\":[],\"goals\":")]
    [InlineData("\"goals\":", "\"signature\":\"data:image/png;base64,AAAA\",\"goals\":")]
    [InlineData("\"goals\":", "\"notes\":\"What we discussed\",\"goals\":")]
    [InlineData("\"goals\":", "\"contentHash\":\"abc\",\"goals\":")]
    [InlineData("\"goals\":", "\"revisionNumber\":1,\"goals\":")]
    [InlineData("\"durationCategory\":", "\"meetingNotes\":\"What we discussed\",\"durationCategory\":")]
    [InlineData("\"durationCategory\":", "\"state\":\"InProgress\",\"durationCategory\":")]
    [InlineData("\"interval\":", "\"perMonth\":2,\"interval\":")]
    [InlineData("\"mentor\":", "\"administrator\":[\"Approve\"],\"mentor\":")]
    [InlineData("\"TwelvePlusMonths\"", "\"TWELVE_PLUS_MONTHS\"")]
    public void A_revision_body_that_names_anything_else_is_malformed(string member, string replacement)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AgreementDraftForm>(Complete.Replace(member, replacement, StringComparison.Ordinal), Web));
    }

    [Fact]
    public void An_acceptance_names_the_content_hash_alone()
    {
        Assert.Equal(new AcceptanceRequest("ab12"), JsonSerializer.Deserialize<AcceptanceRequest>("""{"contentHash":"ab12"}""", Web));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AcceptanceRequest>("""{"contentHash":"ab12","participantEmployeeId":"8f14e45f-ceea-467a-9575-7b9b1a7e0c11"}""", Web));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AcceptanceRequest>("""{"contentHash":"ab12","signature":"x"}""", Web));
    }

    [Fact]
    public void An_annex_change_names_its_basis_and_value_alone()
    {
        Assert.Equal(new AnnexChangeRequest(PermittedChangeBasis.AdministrativeReference, "TM-2026-014"),
            JsonSerializer.Deserialize<AnnexChangeRequest>("""{"basis":"AdministrativeReference","value":"TM-2026-014"}""", Web));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AnnexChangeRequest>("""{"basis":"AdministrativeRemark","value":"x","goals":["Another goal"]}""", Web));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AnnexChangeRequest>("""{"basis":"AdministrativeRemark","value":"x","field":"AdminReference"}""", Web));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AnnexChangeRequest>("""{"basis":"Typographical","value":"x"}""", Web));
    }
}

using System.Text.Json;
using System.Text.RegularExpressions;
using Mentor.Api.Hosting;
using Mentor.Api.Meetings;
using Mentor.Infrastructure.Persistence;

namespace Mentor.Tests.Unit.Meetings;

// ADR-010: the API has no field for anything said in a meeting, and no calendar dependency (FR-058, FR-059, NFR-002).
public sealed partial class MeetingRequestTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void A_meeting_request_carries_only_its_schedule()
    {
        var request = JsonSerializer.Deserialize<MeetingRequest>("""{"scheduledAt":"2026-11-03T09:00:00+00:00","arrangedOutsideApplication":true}""", Web);

        Assert.Equal(new MeetingRequest(new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero), ArrangedOutsideApplication: true), request);
        Assert.Equal(new MeetingRequest(null, ArrangedOutsideApplication: false), JsonSerializer.Deserialize<MeetingRequest>("{}", Web));
    }

    // AC-FR-059-01, AC-NFR-002-01: a body that carries discussion content is refused rather than silently dropped.
    [Theory]
    [InlineData("""{"notes":"We discussed the review"}""")]
    [InlineData("""{"scheduledAt":null,"summary":"Went well"}""")]
    [InlineData("""{"arrangedOutsideApplication":true,"discussion":{"topic":"career"}}""")]
    [InlineData("""{"Notes":"case does not matter"}""")]
    public void A_meeting_request_carrying_anything_else_is_refused(string body)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<MeetingRequest>(body, Web));
    }

    // AC-FR-058-01, AC-NFR-010-01: nothing the backend loads talks to a calendar.
    [Fact]
    public void The_backend_depends_on_no_calendar_service()
    {
        var referenced = new[] { typeof(ApiHost).Assembly, typeof(MentorDbContext).Assembly }
            .SelectMany(assembly => assembly.GetReferencedAssemblies())
            .Select(assembly => assembly.Name!)
            .Distinct()
            .ToList();

        Assert.NotEmpty(referenced);
        Assert.DoesNotContain(referenced, name => CalendarService().IsMatch(name));
    }

    [GeneratedRegex("graph|calendar|exchange|outlook|google|ical", RegexOptions.IgnoreCase)]
    private static partial Regex CalendarService();
}

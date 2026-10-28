using System.Net;
using System.Text;
using Mentor.Api.Hosting;
using Mentor.Tests.Integration.Persistence;
using Mentor.Tests.Integration.Support;

namespace Mentor.Tests.Integration.Meetings;

// AC-NFR-002-01, AC-FR-059-01, ADR-010: the meeting endpoints take no discussion content, and what a client sends
// anyway reaches neither a record, nor a response, nor a log, even with every log category at Trace.
public sealed class MeetingRedactionTests(PostgresServer server)
{
    [Fact]
    public async Task A_meeting_request_carrying_discussion_notes_is_refused_and_the_notes_reach_neither_a_log_nor_a_response()
    {
        await using var db = await server.CreateDatabaseAsync();
        var callers = await TestCallers.RecordAsync(db, IdentityMode.Entra);
        var mentorship = await CallerMentorship.FormedAsync(db, callers);
        await using var probe = await ProbeHost.StartAsync(
            IdentityMode.Entra,
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Mentor"] = db.ConnectionString(Login.App),
                ["Notifications:Delivery"] = "Simulated",
                ["Logging:LogLevel:Default"] = "Trace",
                ["Logging:LogLevel:Microsoft"] = "Trace",
                ["Logging:LogLevel:Microsoft.AspNetCore"] = "Trace",
            },
            testIdentity: false);
        var sessions = $"/api/relationships/{mentorship.Relationship}/meetings";

        var refused = await probe.SendAsync(WithNotes(callers.Request(HttpMethod.Post, $"{sessions}?notes={ProbeHost.Note}", Profile.Employee)));
        var recorded = await probe.SendAsync(WithBody(callers.Request(HttpMethod.Post, sessions, Profile.Mentor), """{"scheduledAt":"2026-11-03T09:00:00+00:00"}"""));
        var meeting = (await recorded.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Split("\"id\":\"")[1][..36];
        var refusedChange = await probe.SendAsync(WithNotes(callers.Request(HttpMethod.Put, $"/api/meetings/{meeting}", Profile.Employee)));

        Assert.Equal(HttpStatusCode.Created, recorded.StatusCode);
        foreach (var refusal in new[] { refused, refusedChange })
        {
            Assert.Equal(HttpStatusCode.BadRequest, refusal.StatusCode);
            Assert.DoesNotContain(ProbeHost.Note, await refusal.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        Assert.Equal(1, await db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.meeting"));
        Assert.True(await db.ScalarAsync<bool>(Login.Owner, "SELECT arranged_outside_application = false AND scheduled_at IS NOT NULL FROM mentor.meeting"));
        Assert.NotEmpty(probe.Logs.Entries);
        Assert.DoesNotContain(probe.Logs.Entries, entry => entry.Mentions(ProbeHost.Note));
        var requests = probe.Logs.From<RequestLoggingMiddleware>().Select(entry => entry.Message).ToList();
        Assert.Contains(requests, line => line.StartsWith("POST /api/relationships/{relationshipId:guid}/meetings answered 400", StringComparison.Ordinal));
        Assert.Contains(requests, line => line.StartsWith("POST /api/relationships/{relationshipId:guid}/meetings answered 201", StringComparison.Ordinal));
        Assert.Contains(requests, line => line.StartsWith("PUT /api/meetings/{meetingId:guid} answered 400", StringComparison.Ordinal));
    }

    private static HttpRequestMessage WithNotes(HttpRequestMessage request) =>
        WithBody(request, $$"""{"scheduledAt":"2026-11-03T09:00:00+00:00","arrangedOutsideApplication":false,"notes":"{{ProbeHost.Note}}"}""");

    private static HttpRequestMessage WithBody(HttpRequestMessage request, string json)
    {
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return request;
    }
}

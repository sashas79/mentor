using System.Net;
using System.Text;
using System.Text.Json;
using Mentor.Api.Hosting;
using Mentor.Tests.Integration.Persistence;
using Mentor.Tests.Integration.Support;

namespace Mentor.Tests.Integration.Goals;

// AC-E2E-08, AC-FR-066-01, AC-FR-040-01, AC-NFR-002-01, FR-059: Talent Management reads structured goals and progress,
// the completion status among them, and no private discussion through any route. No entry or decision can carry meeting
// notes or a reason. What a client sends anyway reaches neither a record nor a response nor a log, and the progress update
// itself reaches neither the trail nor a log, even with every log category at Trace.
public sealed class GoalConfidentialityTests(PostgresServer server)
{
    /// <summary>Stands for the protégé's progress update: kept and shown to the permitted readers, never written to the trail or a log.</summary>
    private const string Update = "progress-update-3d81c7";

    [Fact]
    public async Task Talent_management_reads_structured_goals_and_progress_and_no_discussion_reaches_a_record_a_response_or_a_log()
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
        var goal = mentorship.Goal;

        using var withNotes = await probe.SendAsync(Post(callers, GoalApi.Progress(goal), Profile.Employee,
            $$"""{"progressUpdate":"{{Update}}","notes":"{{ProbeHost.Note}}"}"""));
        using var requested = await probe.SendAsync(Post(callers, GoalApi.CompletionRequests(goal), Profile.Employee, $$"""{"progressUpdate":"{{Update}}"}"""));
        var request = (await JsonAsync(requested)).GetProperty("pendingCompletionRequestId").GetGuid();
        using var withReason = await probe.SendAsync(Post(callers, GoalApi.Decision(goal, request), Profile.Mentor,
            $$"""{"decision":"Rejected","reason":"{{ProbeHost.Note}}"}"""));
        using var rejected = await probe.SendAsync(Post(callers, GoalApi.Decision(goal, request), Profile.Mentor, """{"decision":"Rejected"}"""));
        using var read = await probe.SendAsync(callers.Request(HttpMethod.Get, GoalApi.OfRelationship(mentorship.Relationship), Profile.Administrator));

        Assert.Equal(
            [HttpStatusCode.BadRequest, HttpStatusCode.OK, HttpStatusCode.BadRequest, HttpStatusCode.OK, HttpStatusCode.OK],
            new[] { withNotes, requested, withReason, rejected, read }.Select(response => response.StatusCode));
        foreach (var response in new[] { withNotes, requested, withReason, rejected, read })
        {
            Assert.DoesNotContain(ProbeHost.Note, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        }

        // Talent Management reads the goal, its completion status and the structured progress, and nothing more.
        var administratorView = Assert.Single((await JsonAsync(read)).EnumerateArray());
        Assert.Equal(
            ["agreedDescription", "completionStatus", "createdAt", "formedAgreementId", "id", "pendingCompletionRequestId", "position", "progress", "relationshipId", "updatedAt"],
            administratorView.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        var entry = Assert.Single(administratorView.Items("progress"));
        Assert.Equal(
            ["completion", "completionRequested", "decision", "id", "progressUpdate", "protegeEmployeeId", "submittedAt"],
            entry.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(("Incomplete", Update, "Rejected"), (administratorView.Text("completionStatus"), entry.Text("progressUpdate"), entry.Text("completion")));

        // Only the request was recorded; neither the notes nor the update is in the trail, and the notes are nowhere.
        Assert.Equal((1L, 1L), (await db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.goal_progress"),
            await db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.goal_approval")));
        Assert.Equal(2L, await db.ScalarAsync<long>(Login.Owner,
            "SELECT count(*) FROM mentor.audit_entry WHERE action IN ('GOAL_PROGRESS_SUBMITTED', 'GOAL_COMPLETION_DECIDED')"));
        foreach (var text in new[] { ProbeHost.Note, Update })
        {
            Assert.Equal(0L, await db.ScalarAsync<long>(Login.Owner, """
                SELECT count(*) FROM mentor.audit_entry
                WHERE strpos(coalesce(prior_values::text, '') || coalesce(new_values::text, '') || coalesce(outcome, ''), $1) > 0
                """, text));
        }

        Assert.Equal(0L, await db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.goal_progress WHERE strpos(progress_update, $1) > 0", ProbeHost.Note));

        // Nothing typed reaches a log; each request is logged by its route template and status alone.
        Assert.NotEmpty(probe.Logs.Entries);
        Assert.DoesNotContain(probe.Logs.Entries, logged => logged.Mentions(ProbeHost.Note) || logged.Mentions(Update));
        var lines = probe.Logs.From<RequestLoggingMiddleware>().Select(logged => logged.Message).ToList();
        foreach (var expected in new[]
        {
            "POST /api/goals/{goalId:guid}/progress answered 400",
            "POST /api/goals/{goalId:guid}/completion-requests answered 200",
            "POST /api/goals/{goalId:guid}/completion-requests/{requestId:guid}/decision answered 400",
            "POST /api/goals/{goalId:guid}/completion-requests/{requestId:guid}/decision answered 200",
            "GET /api/relationships/{relationshipId:guid}/goals answered 200",
        })
        {
            Assert.Contains(lines, line => line.StartsWith(expected, StringComparison.Ordinal));
        }
    }

    private static HttpRequestMessage Post(TestCallers callers, string route, Profile caller, string json)
    {
        var request = callers.Request(HttpMethod.Post, route, caller);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return request;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
}

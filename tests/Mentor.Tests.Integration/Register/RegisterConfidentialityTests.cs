using System.Net;
using System.Text;
using System.Text.Json;
using Mentor.Api.Hosting;
using Mentor.Tests.Integration.Goals;
using Mentor.Tests.Integration.Persistence;
using Mentor.Tests.Integration.Support;

namespace Mentor.Tests.Integration.Register;

// AC-FR-040-01, AC-FR-066-01, AC-E2E-08, AC-NFR-002-01, AC-NFR-009-01: Talent Management sees every relationship's goals,
// their completion status and the structured progress, and the register and its export carry none of it. No goal wording,
// progress, agreement content or discussion appears in either. Nothing of a meeting's discussion is stored to be shown,
// and none of it reaches a response, the trail or a log, even with every log category at Trace.
public sealed class RegisterConfidentialityTests(PostgresServer server)
{
    /// <summary>Stands for the protégé's progress update: shown to Talent Management in the goals view, and nowhere in the register, its export, the trail or a log.</summary>
    private const string Update = "progress-update-9a4e27";

    [Fact]
    public async Task Talent_management_sees_structured_goals_and_progress_and_the_register_and_export_carry_none_of_it()
    {
        await using var db = await server.CreateDatabaseAsync();
        var callers = await TestCallers.RecordAsync(db, IdentityMode.Entra);
        var mentorship = await CallerMentorship.FormedAsync(db, callers);
        var legacy = await CallerMentorship.LegacyAsync(db, callers);
        var wording = await db.ScalarAsync<string>(Login.Owner, "SELECT agreed_description FROM mentor.development_goal WHERE id = $1", mentorship.Goal);
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

        using var withNotes = await probe.SendAsync(Post(callers, GoalApi.CompletionRequests(mentorship.Goal), Profile.Employee,
            $$"""{"progressUpdate":"{{Update}}","notes":"{{ProbeHost.Note}}"}"""));
        using var requested = await probe.SendAsync(Post(callers, GoalApi.CompletionRequests(mentorship.Goal), Profile.Employee, $$"""{"progressUpdate":"{{Update}}"}"""));
        var request = (await JsonAsync(requested)).GetProperty("pendingCompletionRequestId").GetGuid();
        using var pending = await probe.SendAsync(callers.Request(HttpMethod.Get, RegisterApi.Goals, Profile.Administrator));
        using var approved = await probe.SendAsync(Post(callers, GoalApi.Decision(mentorship.Goal, request), Profile.Mentor, """{"decision":"Approved"}"""));
        using var goals = await probe.SendAsync(callers.Request(HttpMethod.Get, RegisterApi.Goals, Profile.Administrator));
        using var register = await probe.SendAsync(callers.Request(HttpMethod.Get, RegisterApi.Register, Profile.Administrator));
        using var export = await probe.SendAsync(callers.Request(HttpMethod.Get, RegisterApi.Export, Profile.Administrator));

        Assert.Equal(
            [HttpStatusCode.BadRequest, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK],
            new[] { withNotes, requested, pending, approved, goals, register, export }.Select(response => response.StatusCode));

        // While the request awaits the mentor, the goal is incomplete and the request is counted as awaiting a decision.
        var awaiting = Assert.Single((await JsonAsync(pending)).EnumerateArray(), relationship => relationship.GetProperty("relationshipId").GetGuid() == mentorship.Relationship);
        Assert.Equal((1, 0, 1, 1), SummaryOf(awaiting));

        // Every relationship, the legacy one with no goals, and each goal with its completion status and structured progress.
        var view = (await JsonAsync(goals)).EnumerateArray().ToList();
        Assert.Equal([legacy, mentorship.Relationship], view.Select(relationship => relationship.GetProperty("relationshipId").GetGuid()));
        Assert.Equal(("Legacy", 0, (0, 0, 0, 0)), (view[0].Text("provenance"), view[0].Items("goals").Count, SummaryOf(view[0])));
        var formed = view[1];
        Assert.Equal(
            ["goals", "isActive", "mentor", "protege", "provenance", "relationshipId", "state", "status", "summary"],
            formed.PropertyNames());
        Assert.Equal((mentorship.Mentor, mentorship.Protege), (formed.GetProperty("mentor").GetProperty("employeeId").GetGuid(), formed.GetProperty("protege").GetProperty("employeeId").GetGuid()));
        Assert.Equal((1, 1, 0, 0), SummaryOf(formed));
        var goal = Assert.Single(formed.Items("goals"));
        Assert.Equal(
            ["agreedDescription", "completionStatus", "createdAt", "formedAgreementId", "id", "pendingCompletionRequestId", "position", "progress", "relationshipId", "updatedAt"],
            goal.PropertyNames());
        Assert.Equal((wording, "Complete"), (goal.Text("agreedDescription"), goal.Text("completionStatus")));
        var entry = Assert.Single(goal.Items("progress"));
        Assert.Equal(
            ["completion", "completionRequested", "decision", "id", "progressUpdate", "protegeEmployeeId", "submittedAt"],
            entry.PropertyNames());
        Assert.Equal((Update, true, "Approved"), (entry.Text("progressUpdate"), entry.GetProperty("completionRequested").GetBoolean(), entry.Text("completion")));

        // The register and the export name the relationships and nothing of their goals, progress or agreement.
        var agreement = await db.ScalarAsync<string>(Login.Owner, "SELECT content FROM mentor.formed_agreement WHERE id = $1", mentorship.FormedAgreement);
        var registerText = await register.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var exportText = Encoding.UTF8.GetString(await export.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, JsonSerializer.Deserialize<JsonElement>(registerText).GetArrayLength());
        foreach (var text in new[] { registerText, exportText })
        {
            foreach (var withheld in new[] { Update, wording, ProbeHost.Note, agreement, "progressUpdate", "agreedDescription", "completionStatus", "goals" })
            {
                Assert.DoesNotContain(withheld, text, StringComparison.Ordinal);
            }
        }

        // The export's entry in the trail holds counts only.
        Assert.Equal(0L, await db.ScalarAsync<long>(Login.Owner, """
            SELECT count(*) FROM mentor.audit_entry
            WHERE strpos(coalesce(prior_values::text, '') || coalesce(new_values::text, '') || coalesce(outcome, ''), $1) > 0
               OR strpos(coalesce(prior_values::text, '') || coalesce(new_values::text, '') || coalesce(outcome, ''), $2) > 0
            """, Update, ProbeHost.Note));

        // Nothing typed reaches a log; each request is logged by its route template and status alone.
        Assert.NotEmpty(probe.Logs.Entries);
        Assert.DoesNotContain(probe.Logs.Entries, logged => logged.Mentions(ProbeHost.Note) || logged.Mentions(Update) || logged.Mentions(wording));
        var lines = probe.Logs.From<RequestLoggingMiddleware>().Select(logged => logged.Message).ToList();
        foreach (var expected in new[] { "GET /api/register/goals answered 200", "GET /api/register answered 200", "GET /api/register/export answered 200" })
        {
            Assert.Contains(lines, line => line.StartsWith(expected, StringComparison.Ordinal));
        }
    }

    private static (int Total, int Complete, int Incomplete, int AwaitingDecision) SummaryOf(JsonElement relationship)
    {
        var summary = relationship.GetProperty("summary");
        return (summary.GetProperty("total").GetInt32(), summary.GetProperty("complete").GetInt32(), summary.GetProperty("incomplete").GetInt32(),
            summary.GetProperty("awaitingDecision").GetInt32());
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

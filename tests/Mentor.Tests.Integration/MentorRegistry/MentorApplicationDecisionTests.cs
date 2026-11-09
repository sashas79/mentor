using System.Net;
using Mentor.Api.Hosting;
using Mentor.Api.Identity;
using Mentor.Api.MentorRegistry;
using Mentor.Core.Notifications;
using Mentor.Tests.Integration.Identity;
using Mentor.Tests.Integration.Persistence;
using Mentor.Tests.Integration.Support;
using static Mentor.Tests.Integration.MentorRegistry.MentorRegistryApi;

namespace Mentor.Tests.Integration.MentorRegistry;

// A mentor application needs Talent Management's approval before the mentor is discoverable (FR-008, BR-001): the
// decision is the administrator's alone (FR-004), recorded on the profile, in the audit trail (FR-065, ADR-007) and in
// the applicant's inbox (FR-057, ADR-009), together. COMP-MENTOR-REGISTRY.
public sealed class MentorApplicationDecisionTests(PostgresServer server)
{
    // AC-FR-008-02, AC-FR-065-01 (its share), AC-FR-057-01
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task Approval_makes_the_mentor_discoverable_and_is_recorded_audited_and_notified_together(IdentityMode mode)
    {
        await using var api = await WorkflowApi.StartAsync(server, mode);
        var applicant = api.Employee(Profile.Applicant);
        var administrator = api.Employee(Profile.Administrator);
        var profile = await ProfileIdAsync(api.Db, applicant);
        Assert.DoesNotContain(profile, (await api.JsonAsync(HttpMethod.Get, MentorsRoute, Profile.Employee)).EnumerateArray().Select(JsonElementExtensions.Id));

        var approved = await api.JsonAsync(HttpMethod.Post, ApproveRoute(profile), Profile.Administrator);

        Assert.Equal(profile, approved.Id());
        Assert.Equal("Approved", approved.Text("approvalStatus"));
        Assert.Equal(administrator, approved.OptionalGuid("approvalDecidedByEmployeeId"));
        var decidedAt = approved.OptionalTime("approvalDecidedAt");
        Assert.NotNull(decidedAt);
        Assert.Contains(profile, (await api.JsonAsync(HttpMethod.Get, MentorsRoute, Profile.Employee)).EnumerateArray().Select(JsonElementExtensions.Id));
        Assert.Equal(["Employee", "Mentor"], (await api.JsonAsync(HttpMethod.Get, SessionEndpoint.Route, Profile.Applicant)).Strings("roles"));
        Assert.Equal(approved.GetRawText(), (await api.JsonAsync(HttpMethod.Get, OwnRoute, Profile.Applicant)).GetRawText());

        // The audit entry (ADR-007): true actor and scheme, action, record, prior and new values and outcome, in the
        // decision's own transaction, whose server time the profile, the entry and the event all carry.
        var entry = Assert.Single((await api.JsonAsync(HttpMethod.Get, AuditRoute(profile), Profile.Administrator)).EnumerateArray());
        Assert.Equal("MentorApplicationDecided", entry.Text("action"));
        Assert.Equal("MentorProfile", entry.Text("recordType"));
        Assert.Equal("APPROVED", entry.Text("outcome"));
        Assert.Equal("Application", entry.Text("provenance"));
        Assert.Equal(administrator, entry.GetProperty("actor").GetProperty("employeeId").GetGuid());
        Assert.Equal(mode.ToString(), entry.GetProperty("actor").Text("identityScheme"));
        Assert.Equal("PENDING", entry.GetProperty("priorValues").Text("approvalStatus"));
        Assert.Null(entry.GetProperty("priorValues").OptionalGuid("approvalDecidedByEmployeeId"));
        Assert.Null(entry.GetProperty("priorValues").OptionalTime("approvalDecidedAt"));
        Assert.Equal("APPROVED", entry.GetProperty("newValues").Text("approvalStatus"));
        Assert.Equal(administrator, entry.GetProperty("newValues").OptionalGuid("approvalDecidedByEmployeeId"));
        Assert.Equal(decidedAt, entry.GetProperty("newValues").OptionalTime("approvalDecidedAt"));
        Assert.Equal(decidedAt, entry.OptionalTime("occurredAt"));
        Assert.True(await api.Db.ScalarAsync<bool>(Login.Owner, """
            SELECT profile.approval_decided_at = profile.updated_at AND profile.updated_at = entry.occurred_at
            FROM mentor.mentor_profile AS profile
            JOIN mentor.audit_entry AS entry ON entry.record_id = profile.id
            WHERE profile.id = $1
            """, profile));

        // The applicant's notification (ADR-009): in their inbox and nobody else's, with nothing more to do.
        var notice = Assert.Single((await api.JsonAsync(HttpMethod.Get, Inbox, Profile.Applicant)).Items("events"));
        Assert.Equal("MentorApplicationDecided", notice.Text("eventType"));
        Assert.Equal(NotificationSubject.MentorProfile, notice.Text("subjectType"));
        Assert.Equal(profile, notice.GetProperty("subjectId").GetGuid());
        Assert.Equal("Applicant", notice.Text("recipientRole"));
        Assert.Equal("None", notice.Text("requiredAction"));
        Assert.Equal(decidedAt, notice.OptionalTime("createdAt"));
        Assert.Empty((await api.JsonAsync(HttpMethod.Get, Inbox, Profile.Administrator)).Items("events"));

        // A decision is taken once.
        foreach (var route in new[] { ApproveRoute(profile), DeclineRoute(profile) })
        {
            var again = await ProblemAssert.IsProblem(HttpStatusCode.Conflict, await api.SendAsync(HttpMethod.Post, route, Profile.Administrator));
            Assert.Equal(MentorRegistryEndpoints.AlreadyDecided, again.Text("title"));
        }

        Assert.Equal("APPROVED", await ApprovalStatusAsync(api.Db, profile));
        Assert.Equal(1L, await AuditEntriesAsync(api.Db, profile));
        Assert.Equal(1L, await NotificationsAsync(api.Db, applicant));
    }

    // AC-FR-008-01, AC-BR-001-01: a declined applicant is not discoverable, keeps their one application and is told.
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task A_declined_applicant_stays_undiscoverable_keeps_their_one_application_and_is_told(IdentityMode mode)
    {
        await using var api = await WorkflowApi.StartAsync(server, mode);
        var applicant = api.Employee(Profile.Applicant);
        var profile = await ProfileIdAsync(api.Db, applicant);

        var declined = await api.JsonAsync(HttpMethod.Post, DeclineRoute(profile), Profile.Administrator);

        Assert.Equal("Declined", declined.Text("approvalStatus"));
        Assert.Equal(api.Employee(Profile.Administrator), declined.OptionalGuid("approvalDecidedByEmployeeId"));
        Assert.NotNull(declined.OptionalTime("approvalDecidedAt"));
        Assert.DoesNotContain(profile, (await api.JsonAsync(HttpMethod.Get, MentorsRoute, Profile.Employee)).EnumerateArray().Select(JsonElementExtensions.Id));
        Assert.Equal(HttpStatusCode.Forbidden, await api.StatusAsync(HttpMethod.Get, ProfileRoute(profile), Profile.Employee));
        Assert.Equal(["Employee", "Applicant"], (await api.JsonAsync(HttpMethod.Get, SessionEndpoint.Route, Profile.Applicant)).Strings("roles"));
        Assert.Equal("Declined", (await api.JsonAsync(HttpMethod.Get, OwnRoute, Profile.Applicant)).Text("approvalStatus"));
        var entry = Assert.Single((await api.JsonAsync(HttpMethod.Get, AuditRoute(profile), Profile.Administrator)).EnumerateArray());
        Assert.Equal("DECLINED", entry.Text("outcome"));
        Assert.Equal("DECLINED", entry.GetProperty("newValues").Text("approvalStatus"));
        var notice = Assert.Single((await api.JsonAsync(HttpMethod.Get, Inbox, Profile.Applicant)).Items("events"));
        Assert.Equal("MentorApplicationDecided", notice.Text("eventType"));
        Assert.Equal("None", notice.Text("requiredAction"));

        // One application per employee: a declined applicant has theirs, and the decision stands.
        var again = await ProblemAssert.IsProblem(HttpStatusCode.Conflict, await api.SendAsync(HttpMethod.Post, ApplicationsRoute, Profile.Applicant, Details()));
        Assert.Equal(MentorRegistryEndpoints.AlreadyApplied, again.Text("title"));
        Assert.Equal(HttpStatusCode.Conflict, await api.StatusAsync(HttpMethod.Post, ApproveRoute(profile), Profile.Administrator));
        Assert.Equal("DECLINED", await ApprovalStatusAsync(api.Db, profile));
        Assert.Equal(1L, await AuditEntriesAsync(api.Db, profile));
    }

    // AC-FR-004-01, AC-FR-004-02: invoking the API directly, nobody but a Talent Management administrator approves a
    // mentor, the applicant themselves included, under either identity scheme.
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task Nobody_but_talent_management_approves_or_declines_an_application(IdentityMode mode)
    {
        await using var api = await WorkflowApi.StartAsync(server, mode);
        var applicant = api.Employee(Profile.Applicant);
        var profile = await ProfileIdAsync(api.Db, applicant);

        foreach (var caller in new[] { Profile.Employee, Profile.Applicant, Profile.Mentor, Profile.Newcomer })
        {
            Assert.Equal(HttpStatusCode.Forbidden, await api.StatusAsync(HttpMethod.Post, ApproveRoute(profile), caller));
            Assert.Equal(HttpStatusCode.Forbidden, await api.StatusAsync(HttpMethod.Post, DeclineRoute(profile), caller));
            Assert.Equal(HttpStatusCode.Forbidden, await api.StatusAsync(HttpMethod.Get, PendingRoute, caller));
        }

        await ProblemAssert.IsProblem(HttpStatusCode.Unauthorized, await api.SendAsync(HttpMethod.Post, ApproveRoute(profile), profile: null));
        Assert.Equal("PENDING", await ApprovalStatusAsync(api.Db, profile));
        Assert.Equal(0L, await AuditEntriesAsync(api.Db, profile));
        Assert.Equal(0L, await NotificationsAsync(api.Db, applicant));
        Assert.Equal(HttpStatusCode.NotFound, await api.StatusAsync(HttpMethod.Post, ApproveRoute(Guid.NewGuid()), Profile.Administrator));
        // The matrix refuses before any id is looked up: an outsider cannot tell whether an application exists.
        Assert.Equal(HttpStatusCode.Forbidden, await api.StatusAsync(HttpMethod.Post, ApproveRoute(Guid.NewGuid()), Profile.Mentor));
    }

    // ADR-003: Talent Management's pending work is a query over the applications, oldest first, and theirs alone.
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task Talent_management_lists_the_pending_applications_oldest_first(IdentityMode mode)
    {
        await using var api = await WorkflowApi.StartAsync(server, mode);
        var first = await ProfileIdAsync(api.Db, api.Employee(Profile.Applicant));
        var second = (await api.JsonAsync(HttpMethod.Post, ApplicationsRoute, Profile.Employee, Details(), HttpStatusCode.Created)).Id();

        var pending = (await api.JsonAsync(HttpMethod.Get, PendingRoute, Profile.Administrator)).EnumerateArray().ToList();

        Assert.Equal([first, second], pending.Select(JsonElementExtensions.Id));
        Assert.Equal(["Applicant", "Employee"], pending.Select(shown => shown.Text("displayName")));
        Assert.All(pending, shown => Assert.Equal("Pending", shown.Text("approvalStatus")));
        await api.JsonAsync(HttpMethod.Post, ApproveRoute(first), Profile.Administrator);
        Assert.Equal([second], (await api.JsonAsync(HttpMethod.Get, PendingRoute, Profile.Administrator)).EnumerateArray().Select(JsonElementExtensions.Id));
        await ProblemAssert.IsProblem(HttpStatusCode.Unauthorized, await api.SendAsync(HttpMethod.Get, PendingRoute, profile: null));
    }

    // Two administrators deciding at once: the database settles it. One decision is recorded, with one entry and one
    // notification; the other administrator learns the application was decided already.
    [Fact]
    public async Task Two_simultaneous_decisions_end_with_exactly_one_recorded()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var applicant = api.Employee(Profile.Applicant);
        var profile = await ProfileIdAsync(api.Db, applicant);

        var responses = await Task.WhenAll(
            api.SendAsync(HttpMethod.Post, ApproveRoute(profile), Profile.Administrator),
            api.SendAsync(HttpMethod.Post, DeclineRoute(profile), Profile.Administrator));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], responses.Select(response => response.StatusCode).Order());
        var recorded = responses[0].StatusCode == HttpStatusCode.OK ? "APPROVED" : "DECLINED";
        Assert.Equal(recorded, await ApprovalStatusAsync(api.Db, profile));
        Assert.Equal(1L, await AuditEntriesAsync(api.Db, profile));
        Assert.Equal(recorded, await api.Db.ScalarAsync<string>(Login.Owner, "SELECT outcome FROM mentor.audit_entry WHERE record_id = $1", profile));
        Assert.Equal(1L, await NotificationsAsync(api.Db, applicant));
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }
}

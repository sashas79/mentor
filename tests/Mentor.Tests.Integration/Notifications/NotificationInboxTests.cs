using System.Net;
using System.Text.Json;
using Mentor.Api.Hosting;
using Mentor.Api.Notifications;
using Mentor.Core.Formation;
using Mentor.Core.Goals;
using Mentor.Core.MentorRegistry;
using Mentor.Core.Notifications;
using Mentor.Core.Requests;
using Mentor.Infrastructure.Persistence;
using Mentor.Tests.Integration.Persistence;
using Mentor.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Mentor.Core.Notifications.WorkflowNotifications;

namespace Mentor.Tests.Integration.Notifications;

// The in-app inbox of simulated delivery (FR-057, ADR-009): events are demonstrable to their intended recipient and to
// nobody else, under either identity scheme.
public sealed class NotificationInboxTests(PostgresServer server)
{
    private const string Inbox = "/api/notifications";

    // AC-FR-057-02: delivery is explicitly simulated; the event is delivered the moment it is recorded.
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task The_inbox_holds_the_callers_own_events_only_newest_first_as_simulated_deliveries(IdentityMode mode)
    {
        await using var api = await WorkflowApi.StartAsync(server, mode);
        var first = await EventAsync(api.Db, api.Employee(Profile.Employee));
        var second = await EventAsync(api.Db, api.Employee(Profile.Employee), "MENTOR_RESPONDED", "PROTEGE", "RECORD_WILLINGNESS");
        var mentors = await EventAsync(api.Db, api.Employee(Profile.Mentor));
        var administrators = await EventAsync(api.Db, api.Employee(Profile.Administrator));

        var inbox = await api.JsonAsync(HttpMethod.Get, Inbox, Profile.Employee);

        Assert.Equal("Simulated", inbox.Text("delivery"));
        var events = inbox.Items("events");
        Assert.Equal([second, first], events.Select(JsonElementExtensions.Id));
        Assert.All(events, notification =>
        {
            Assert.Equal(api.Employee(Profile.Employee), notification.GetProperty("recipientEmployeeId").GetGuid());
            Assert.Equal("Simulated", notification.Text("delivery"));
            Assert.Equal(notification.OptionalTime("createdAt"), notification.OptionalTime("deliveredAt"));
            Assert.Null(notification.OptionalTime("readAt"));
        });
        Assert.Equal([mentors], (await api.JsonAsync(HttpMethod.Get, Inbox, Profile.Mentor)).Items("events").Select(JsonElementExtensions.Id));
        Assert.Equal([administrators], (await api.JsonAsync(HttpMethod.Get, Inbox, Profile.Administrator)).Items("events").Select(JsonElementExtensions.Id));
        Assert.Empty((await api.JsonAsync(HttpMethod.Get, Inbox, Profile.Newcomer)).Items("events"));
        await ProblemAssert.IsProblem(HttpStatusCode.Unauthorized, await api.SendAsync(HttpMethod.Get, Inbox, profile: null));
    }

    // AC-FR-057-03: another employee, an administrator included, cannot retrieve or act on a recipient's pending action.
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task Only_the_intended_recipient_retrieves_or_marks_a_pending_action(IdentityMode mode)
    {
        await using var api = await WorkflowApi.StartAsync(server, mode);
        var pending = await EventAsync(api.Db, api.Employee(Profile.Employee));

        foreach (var other in new[] { Profile.Applicant, Profile.Mentor, Profile.Administrator, Profile.Newcomer })
        {
            var retrieval = await api.SendAsync(HttpMethod.Get, $"{Inbox}/{pending}", other);
            var marking = await api.SendAsync(HttpMethod.Post, $"{Inbox}/{pending}/read", other);

            Assert.Equal(HttpStatusCode.Forbidden, retrieval.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, marking.StatusCode);
            foreach (var refusal in new[] { retrieval, marking })
            {
                var body = await refusal.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
                Assert.DoesNotContain("requiredAction", body);
                Assert.DoesNotContain("RespondToInvitation", body);
            }
        }

        Assert.False(await api.Db.ScalarAsync<bool>(Login.Owner, "SELECT read_at IS NOT NULL FROM mentor.notification_event WHERE id = $1", pending));
        var own = await api.JsonAsync(HttpMethod.Get, $"{Inbox}/{pending}", Profile.Employee);
        Assert.Equal("InvitationSent", own.Text("eventType"));
        Assert.Equal("CANDIDATE", own.Text("subjectType"));
        Assert.Equal("Mentor", own.Text("recipientRole"));
        Assert.Equal("RespondToInvitation", own.Text("requiredAction"));
        Assert.Equal(HttpStatusCode.NotFound, await api.StatusAsync(HttpMethod.Get, $"{Inbox}/{Guid.NewGuid()}", Profile.Employee));
        Assert.Equal(HttpStatusCode.NotFound, await api.StatusAsync(HttpMethod.Post, $"{Inbox}/{Guid.NewGuid()}/read", Profile.Employee));
        await ProblemAssert.IsProblem(HttpStatusCode.Unauthorized, await api.SendAsync(HttpMethod.Post, $"{Inbox}/{pending}/read", profile: null));
    }

    [Fact]
    public async Task The_recipient_marks_an_event_read_once_with_the_server_clock()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var read = await EventAsync(api.Db, api.Employee(Profile.Employee));
        var unread = await EventAsync(api.Db, api.Employee(Profile.Employee));

        var marked = await api.JsonAsync(HttpMethod.Post, $"{Inbox}/{read}/read", Profile.Employee);
        var markedAgain = await api.JsonAsync(HttpMethod.Post, $"{Inbox}/{read}/read", Profile.Employee);

        Assert.NotNull(marked.OptionalTime("readAt"));
        Assert.Equal(marked.OptionalTime("readAt"), markedAgain.OptionalTime("readAt"));
        Assert.True(await api.Db.ScalarAsync<bool>(Login.Owner,
            "SELECT read_at >= created_at AND read_at <= now() FROM mentor.notification_event WHERE id = $1", read));
        Assert.Equal([unread], (await api.JsonAsync(HttpMethod.Get, $"{Inbox}?unread=true", Profile.Employee)).Items("events").Select(JsonElementExtensions.Id));
        Assert.Equal(2, (await api.JsonAsync(HttpMethod.Get, Inbox, Profile.Employee)).Items("events").Count);
    }

    // AC-FR-057-01, AC-FR-057-02: every material action of the catalogue, raised as a module would raise it, is
    // demonstrable in its intended recipient's inbox with the action, role and next step it identifies.
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task Every_material_action_is_demonstrable_in_its_recipients_inbox(IdentityMode mode)
    {
        await using var api = await WorkflowApi.StartAsync(server, mode);
        var mentorship = await CallerMentorship.FormedAsync(api.Db, api.Callers);
        var expected = new List<(Profile Recipient, WorkflowNotification Notification)>();
        using (var scope = api.Api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MentorDbContext>();
            var notifications = scope.ServiceProvider.GetRequiredService<NotificationEvents>();
            // The records as the commands would leave them; nothing here is tracked, so the stand-in states are not saved.
            var candidate = await db.MentorSelections.AsNoTracking().SingleAsync(candidate => candidate.Id == mentorship.Candidate, TestContext.Current.CancellationToken);
            var relationship = await db.MentorshipRelationships.AsNoTracking().SingleAsync(relationship => relationship.Id == mentorship.Relationship, TestContext.Current.CancellationToken);
            var goal = await db.DevelopmentGoals.AsNoTracking().SingleAsync(goal => goal.Id == mentorship.Goal, TestContext.Current.CancellationToken);
            var profile = await db.MentorProfiles.AsNoTracking().SingleAsync(profile => profile.EmployeeId == api.Employee(Profile.Applicant), TestContext.Current.CancellationToken);
            profile.ApprovalStatus = MentorApprovalStatus.Approved;
            candidate.Status = CandidateStatus.Drafting;
            var withdrawn = new MentorSelection { Id = candidate.Id, ProtegeEmployeeId = candidate.ProtegeEmployeeId, MentorEmployeeId = candidate.MentorEmployeeId, WithdrawnByEmployeeId = candidate.MentorEmployeeId };

            expected.AddRange(
            [
                (Profile.Applicant, MentorApplicationDecided(profile)),
                (Profile.Mentor, InvitationSent(candidate)),
                (Profile.Employee, MentorResponded(candidate)),
                (Profile.Mentor, WillingnessRecorded(candidate, candidate.ProtegeEmployeeId)),
                (Profile.Employee, CandidateWithdrawn(withdrawn)),
                (Profile.Mentor, RevisionCreated(candidate, candidate.ProtegeEmployeeId)),
                (Profile.Employee, AcceptanceRecorded(candidate, candidate.MentorEmployeeId, otherParticipantHasAccepted: false)),
                (Profile.Mentor, GoalCompletionRequested(goal, relationship)),
                (Profile.Employee, GoalCompletionDecided(goal, relationship, GoalDecision.Rejected)),
            ]);
            var formed = FormationDecided(candidate, FormationOutcome.Approved);
            expected.Add((Profile.Employee, formed[0]));
            expected.Add((Profile.Mentor, formed[1]));

            notifications.Raise(expected.Select(e => e.Notification));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var missing = new List<string>();
        foreach (var recipientProfile in new[] { Profile.Employee, Profile.Mentor, Profile.Applicant })
        {
            var inbox = await api.JsonAsync(HttpMethod.Get, Inbox, recipientProfile);
            var events = inbox.Items("events");
            Assert.Equal(expected.Count(e => e.Recipient == recipientProfile), events.Count);
            foreach (var (_, notification) in expected.Where(e => e.Recipient == recipientProfile))
            {
                var shown = events.SingleOrDefault(e => Matches(e, notification));
                if (shown.ValueKind != JsonValueKind.Object)
                {
                    missing.Add($"{recipientProfile}: {notification}");
                    continue;
                }

                Assert.Equal(api.Employee(recipientProfile), shown.GetProperty("recipientEmployeeId").GetGuid());
                Assert.Equal("Simulated", shown.Text("delivery"));
                Assert.Equal(shown.OptionalTime("createdAt"), shown.OptionalTime("deliveredAt"));
            }
        }

        Assert.Empty(missing);
        Assert.Empty((await api.JsonAsync(HttpMethod.Get, Inbox, Profile.Administrator)).Items("events"));
        Assert.Equal(Enum.GetValues<NotificationEventType>().Order(), expected.Select(e => e.Notification.EventType).Distinct().Order());
    }

    // ADR-009: the event is part of the action's transaction; it is written when the action commits and not otherwise.
    [Fact]
    public async Task An_event_is_written_only_when_the_action_that_raised_it_commits()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var candidateId = await CallerMentorship.CandidateAsync(api.Db, api.Callers, "INVITED");
        using var scope = api.Api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MentorDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationEvents>();
        var candidate = await db.MentorSelections.AsNoTracking().SingleAsync(candidate => candidate.Id == candidateId, TestContext.Current.CancellationToken);

        await using (var action = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            var raised = notifications.Raise(InvitationSent(candidate));
            Assert.Equal(0, await CountAsync(api.Db, api.Employee(Profile.Mentor)));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            Assert.Equal(0, await CountAsync(api.Db, api.Employee(Profile.Mentor)));
            Assert.Equal(NotificationDelivery.Simulated, raised.Delivery);
            await action.RollbackAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(0, await CountAsync(api.Db, api.Employee(Profile.Mentor)));
        db.ChangeTracker.Clear();

        await using (var action = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            notifications.Raise(InvitationSent(candidate));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            await action.CommitAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(1, await CountAsync(api.Db, api.Employee(Profile.Mentor)));
        var shown = Assert.Single((await api.JsonAsync(HttpMethod.Get, Inbox, Profile.Mentor)).Items("events"));
        Assert.Equal(candidateId, shown.GetProperty("subjectId").GetGuid());
        Assert.Equal("RespondToInvitation", shown.Text("requiredAction"));
    }

    // AC-NFR-010-01: with no live transport the pending action is still recorded and shown; nothing claims a delivery.
    [Fact]
    public async Task Without_simulated_delivery_the_pending_action_is_recorded_but_no_delivery_is_claimed()
    {
        await using var api = await WorkflowApi.StartAsync(server, settings: new Dictionary<string, string> { ["Notifications:Delivery"] = "None" });
        var candidateId = await CallerMentorship.CandidateAsync(api.Db, api.Callers, "INVITED");
        using (var scope = api.Api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MentorDbContext>();
            var candidate = await db.MentorSelections.AsNoTracking().SingleAsync(candidate => candidate.Id == candidateId, TestContext.Current.CancellationToken);
            scope.ServiceProvider.GetRequiredService<NotificationEvents>().Raise(InvitationSent(candidate));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var inbox = await api.JsonAsync(HttpMethod.Get, Inbox, Profile.Mentor);
        var capabilities = await api.JsonAsync(HttpMethod.Get, CapabilitiesEndpoint.Route, profile: null);

        Assert.Equal("None", inbox.Text("delivery"));
        Assert.Equal("None", capabilities.Text("notificationDelivery"));
        var shown = Assert.Single(inbox.Items("events"));
        Assert.Equal("None", shown.Text("delivery"));
        Assert.Null(shown.OptionalTime("deliveredAt"));
        Assert.Equal("RespondToInvitation", shown.Text("requiredAction"));
    }

    private static bool Matches(JsonElement shown, WorkflowNotification notification) =>
        shown.Text("eventType") == notification.EventType.ToString()
        && shown.Text("subjectType") == notification.SubjectType
        && shown.GetProperty("subjectId").GetGuid() == notification.SubjectId
        && shown.Text("recipientRole") == notification.RecipientRole.ToString()
        && shown.Text("requiredAction") == notification.RequiredAction.ToString();

    private static Task<Guid> EventAsync(TestDatabase db, Guid recipient, string eventType = "INVITATION_SENT", string role = "MENTOR", string action = "RESPOND_TO_INVITATION") =>
        db.ScalarAsync<Guid>(Login.Owner, """
            INSERT INTO mentor.notification_event (id, event_type, subject_type, subject_id, recipient_employee_id, recipient_role, required_action, delivery)
            VALUES (gen_random_uuid(), $1, 'CANDIDATE', gen_random_uuid(), $2, $3, $4, 'SIMULATED')
            RETURNING id
            """, eventType, recipient, role, action);

    private static Task<long> CountAsync(TestDatabase db, Guid recipient) =>
        db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.notification_event WHERE recipient_employee_id = $1", recipient);
}

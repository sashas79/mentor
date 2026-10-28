using System.Net;
using Mentor.Api.Hosting;
using Mentor.Tests.Integration.Persistence;
using Mentor.Tests.Integration.Support;

namespace Mentor.Tests.Integration.Meetings;

// The meeting schedule of ADR-010 (FR-055, FR-056, FR-059, DR-013): what the parties and Talent Management see and
// record, and what nobody else reaches, under either identity scheme.
public sealed class MeetingScheduleTests(PostgresServer server)
{
    private static readonly DateTimeOffset Proposed = new(2026, 11, 3, 9, 0, 0, TimeSpan.Zero);

    private static readonly string[] ScheduleFields =
    [
        "arrangedOutsideApplication", "candidateId", "createdAt", "createdByEmployeeId", "durationMinutes", "frequencyWeeks",
        "id", "kind", "relationshipId", "scheduledAt", "updatedAt",
    ];

    // AC-FR-055-01, AC-FR-056-01
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task Both_participants_and_talent_management_view_the_agreed_frequency_and_duration_and_the_planned_sessions(IdentityMode mode)
    {
        await using var api = await WorkflowApi.StartAsync(server, mode);
        var mentorship = await CallerMentorship.FormedAsync(api.Db, api.Callers);
        var route = Schedule(mentorship.Relationship);
        var planned = await api.JsonAsync(HttpMethod.Post, route, Profile.Employee, new { scheduledAt = Proposed }, HttpStatusCode.Created);
        var arranged = await api.JsonAsync(HttpMethod.Post, route, Profile.Mentor, new { arrangedOutsideApplication = true }, HttpStatusCode.Created);

        foreach (var viewer in new[] { Profile.Employee, Profile.Mentor, Profile.Administrator })
        {
            var schedule = await api.JsonAsync(HttpMethod.Get, route, viewer);

            Assert.Equal(mentorship.Relationship, schedule.GetProperty("relationshipId").GetGuid());
            Assert.Equal(CallerMentorship.AgreedFrequencyWeeks, schedule.OptionalInt("agreedFrequencyWeeks"));
            Assert.Equal(CallerMentorship.AgreedDurationMinutes, schedule.OptionalInt("agreedDurationMinutes"));
            var sessions = schedule.Items("sessions");
            Assert.Equal([planned.Id(), arranged.Id()], sessions.Select(JsonElementExtensions.Id));
            Assert.Equal(Proposed, sessions[0].OptionalTime("scheduledAt"));
            Assert.False(sessions[0].GetProperty("arrangedOutsideApplication").GetBoolean());
            Assert.Null(sessions[1].OptionalTime("scheduledAt"));
            Assert.True(sessions[1].GetProperty("arrangedOutsideApplication").GetBoolean());
            Assert.All(sessions, session =>
            {
                Assert.Equal("RecurringSession", session.Text("kind"));
                Assert.Equal(mentorship.Relationship, session.OptionalGuid("relationshipId"));
                Assert.Null(session.OptionalGuid("candidateId"));
                Assert.Equal(CallerMentorship.AgreedFrequencyWeeks, session.OptionalInt("frequencyWeeks"));
                Assert.Equal(CallerMentorship.AgreedDurationMinutes, session.OptionalInt("durationMinutes"));
            });
        }

        Assert.Equal(api.Employee(Profile.Employee), planned.GetProperty("createdByEmployeeId").GetGuid());
        Assert.Equal(api.Employee(Profile.Mentor), arranged.GetProperty("createdByEmployeeId").GetGuid());
        Assert.Equal(HttpStatusCode.Forbidden, await api.StatusAsync(HttpMethod.Get, route, Profile.Applicant));
        Assert.Equal(HttpStatusCode.Forbidden, await api.StatusAsync(HttpMethod.Get, route, Profile.Newcomer));
        Assert.Equal(HttpStatusCode.NotFound, await api.StatusAsync(HttpMethod.Get, Schedule(Guid.NewGuid()), Profile.Employee));
        await ProblemAssert.IsProblem(HttpStatusCode.Unauthorized, await api.SendAsync(HttpMethod.Get, route, profile: null));
    }

    // AC-FR-059-01, AC-DR-013-01: a session is recorded with nothing but its schedule, and the record holds nothing else.
    [Fact]
    public async Task A_session_is_recorded_without_any_notes_and_carries_only_its_schedule()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var mentorship = await CallerMentorship.FormedAsync(api.Db, api.Callers);

        var response = await api.SendAsync(HttpMethod.Post, Schedule(mentorship.Relationship), Profile.Employee, new { });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var session = await api.JsonAsync(HttpMethod.Get, response.Headers.Location!.PathAndQuery, Profile.Mentor);
        Assert.Equal($"/api/meetings/{session.Id()}", response.Headers.Location.AbsolutePath);
        Assert.Equal(ScheduleFields, session.PropertyNames());
        Assert.Equal(mentorship.Relationship, session.OptionalGuid("relationshipId"));
        Assert.Null(session.OptionalTime("scheduledAt"));
        Assert.False(session.GetProperty("arrangedOutsideApplication").GetBoolean());
        Assert.Equal(api.Employee(Profile.Employee), session.GetProperty("createdByEmployeeId").GetGuid());
        Assert.Equal(1, await api.Db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.meeting WHERE relationship_id = $1", mentorship.Relationship));
        Assert.Equal(HttpStatusCode.Forbidden, await api.StatusAsync(HttpMethod.Get, $"/api/meetings/{session.Id()}", Profile.Applicant));
        Assert.Equal(HttpStatusCode.NotFound, await api.StatusAsync(HttpMethod.Get, $"/api/meetings/{Guid.NewGuid()}", Profile.Employee));
    }

    // ADR-003: Talent Management reads a schedule but records nothing in it; nobody outside the relationship reaches it.
    [Fact]
    public async Task Only_the_parties_record_a_session()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var mentorship = await CallerMentorship.FormedAsync(api.Db, api.Callers);
        var route = Schedule(mentorship.Relationship);

        foreach (var outsider in new[] { Profile.Administrator, Profile.Applicant, Profile.Newcomer })
        {
            Assert.Equal(HttpStatusCode.Forbidden, await api.StatusAsync(HttpMethod.Post, route, outsider, new { scheduledAt = Proposed }));
        }

        Assert.Equal(HttpStatusCode.NotFound, await api.StatusAsync(HttpMethod.Post, Schedule(Guid.NewGuid()), Profile.Employee, new { }));
        Assert.Empty((await api.JsonAsync(HttpMethod.Get, route, Profile.Administrator)).Items("sessions"));
    }

    // AC-FR-018-02 (its API share), FR-056: a proposed introduction stays with the candidate and can be marked arranged
    // outside the application; no calendar is involved.
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task An_introduction_is_proposed_for_an_accepted_candidate_and_can_be_marked_arranged_outside_the_application(IdentityMode mode)
    {
        await using var api = await WorkflowApi.StartAsync(server, mode);
        var accepted = await CallerMentorship.CandidateAsync(api.Db, api.Callers, "ACCEPTED");
        var invited = await CallerMentorship.CandidateAsync(api.Db, api.Callers, "INVITED");

        var proposed = await api.JsonAsync(HttpMethod.Post, Introductions(accepted), Profile.Mentor, new { scheduledAt = Proposed }, HttpStatusCode.Created);
        var arranged = await api.JsonAsync(HttpMethod.Put, $"/api/meetings/{proposed.Id()}", Profile.Employee, new { scheduledAt = (DateTimeOffset?)null, arrangedOutsideApplication = true });

        Assert.Equal("Introductory", proposed.Text("kind"));
        Assert.Equal(accepted, proposed.OptionalGuid("candidateId"));
        Assert.Null(proposed.OptionalGuid("relationshipId"));
        Assert.Equal(Proposed, proposed.OptionalTime("scheduledAt"));
        Assert.Null(proposed.OptionalInt("frequencyWeeks"));
        Assert.Null(proposed.OptionalInt("durationMinutes"));
        Assert.Equal(proposed.Id(), arranged.Id());
        Assert.True(arranged.GetProperty("arrangedOutsideApplication").GetBoolean());
        Assert.Null(arranged.OptionalTime("scheduledAt"));
        Assert.True(arranged.OptionalTime("updatedAt") >= arranged.OptionalTime("createdAt"));
        foreach (var viewer in new[] { Profile.Employee, Profile.Mentor, Profile.Administrator })
        {
            var introductions = await api.JsonAsync(HttpMethod.Get, Introductions(accepted), viewer);
            var shown = Assert.Single(introductions.EnumerateArray());
            Assert.Equal(proposed.Id(), shown.Id());
            Assert.True(shown.GetProperty("arrangedOutsideApplication").GetBoolean());
        }

        Assert.Equal(HttpStatusCode.Conflict, await api.StatusAsync(HttpMethod.Post, Introductions(invited), Profile.Mentor, new { }));
        Assert.Equal(0, await api.Db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.meeting WHERE candidate_id = $1", invited));
        foreach (var outsider in new[] { Profile.Administrator, Profile.Applicant, Profile.Newcomer })
        {
            Assert.Equal(HttpStatusCode.Forbidden, await api.StatusAsync(HttpMethod.Post, Introductions(accepted), outsider, new { }));
            Assert.Equal(HttpStatusCode.Forbidden, await api.StatusAsync(HttpMethod.Put, $"/api/meetings/{proposed.Id()}", outsider, new { arrangedOutsideApplication = false }));
        }

        Assert.Equal(HttpStatusCode.Forbidden, await api.StatusAsync(HttpMethod.Get, Introductions(accepted), Profile.Applicant));
        Assert.Equal(HttpStatusCode.NotFound, await api.StatusAsync(HttpMethod.Get, Introductions(Guid.NewGuid()), Profile.Employee));
        Assert.Equal(HttpStatusCode.NotFound, await api.StatusAsync(HttpMethod.Put, $"/api/meetings/{Guid.NewGuid()}", Profile.Employee, new { }));
    }

    // A historical relationship has no formed agreement, so no agreed terms to show or copy; its parties still plan sessions.
    [Fact]
    public async Task A_relationship_without_a_formed_agreement_has_no_agreed_terms()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var legacy = await CallerMentorship.LegacyAsync(api.Db, api.Callers);

        var session = await api.JsonAsync(HttpMethod.Post, Schedule(legacy), Profile.Mentor, new { scheduledAt = Proposed }, HttpStatusCode.Created);
        var schedule = await api.JsonAsync(HttpMethod.Get, Schedule(legacy), Profile.Employee);

        Assert.Null(schedule.OptionalInt("agreedFrequencyWeeks"));
        Assert.Null(schedule.OptionalInt("agreedDurationMinutes"));
        Assert.Null(session.OptionalInt("frequencyWeeks"));
        Assert.Null(session.OptionalInt("durationMinutes"));
        Assert.Equal([session.Id()], schedule.Items("sessions").Select(JsonElementExtensions.Id));
    }

    private static string Schedule(Guid relationship) => $"/api/relationships/{relationship}/meetings";

    private static string Introductions(Guid candidate) => $"/api/candidates/{candidate}/meetings";
}

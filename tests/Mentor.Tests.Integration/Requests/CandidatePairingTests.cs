using System.Net;
using System.Text.Json;
using Mentor.Api.Hosting;
using Mentor.Api.Requests;
using Mentor.Tests.Integration.MentorRegistry;
using Mentor.Tests.Integration.Persistence;
using Mentor.Tests.Integration.Support;
using static Mentor.Tests.Integration.Requests.PairingApi;

namespace Mentor.Tests.Integration.Requests;

// The candidate lifecycle of ADR-015 through the API: the protégé's selection of a discoverable mentor, the mentor's
// response, each participant's own affirmative willingness and withdrawal. Every decision is recorded in the audit trail
// with its actor and server time (ADR-007) and notified to the other participant (ADR-009). COMP-MENTORSHIP-REQUEST.
public sealed class CandidatePairingTests(PostgresServer server)
{
    private static readonly string[] CandidateFields =
    [
        .. new[]
        {
            "currentRevisionId", "id", "invitedAt", "mentorDisplayName", "mentorEmployeeId", "mentorRespondedAt", "mentorResponse",
            "mentorWillingAt", "protegeDisplayName", "protegeEmployeeId", "protegeWillingAt", "requestId", "status", "updatedAt", "version",
            "withdrawnAt", "withdrawnByEmployeeId",
        }.Order(),
    ];

    // AC-FR-018-01, AC-FR-019-01, AC-FR-020-01, AC-FR-020-02, AC-DR-004-01
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task A_protege_invites_a_mentor_who_accepts_and_drafting_begins_only_once_each_has_recorded_their_own_willingness(IdentityMode mode)
    {
        await using var api = await WorkflowApi.StartAsync(server, mode);
        var (protege, mentor) = (api.Employee(Profile.Employee), api.Employee(Profile.Mentor));
        var request = await SubmitAsync(api, Profile.Employee);

        var response = await api.SendAsync(HttpMethod.Post, RequestCandidates(request), Profile.Employee, new { mentorEmployeeId = mentor });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var invited = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var id = invited.Id();
        Assert.Equal(Candidate(id), response.Headers.Location!.PathAndQuery);
        Assert.Equal("\"1\"", response.Headers.ETag!.Tag.ToString());
        Assert.Equal(CandidateFields, invited.PropertyNames());
        Assert.Equal((request, protege, mentor), (RequestOf(invited), invited.GetProperty("protegeEmployeeId").GetGuid(), invited.GetProperty("mentorEmployeeId").GetGuid()));
        Assert.Equal(("Employee", "Mentor"), (invited.Text("protegeDisplayName"), invited.Text("mentorDisplayName")));
        Assert.Equal(("Invited", "Pending", 1L), (invited.Text("status"), invited.Text("mentorResponse"), invited.GetProperty("version").GetInt64()));
        Assert.NotNull(invited.OptionalTime("invitedAt"));
        Assert.All(new[] { "mentorRespondedAt", "protegeWillingAt", "mentorWillingAt", "withdrawnAt" }, field => Assert.Null(invited.OptionalTime(field)));
        Assert.Null(invited.OptionalGuid("withdrawnByEmployeeId"));
        Assert.Null(invited.OptionalGuid("currentRevisionId"));
        // The selection belongs to the request, and each participant finds it among their own candidates.
        Assert.Equal([id], (await api.JsonAsync(HttpMethod.Get, RequestCandidates(request), Profile.Employee)).EnumerateArray().Select(JsonElementExtensions.Id));
        Assert.Equal([id], (await api.JsonAsync(HttpMethod.Get, CandidatesRoute, Profile.Employee)).EnumerateArray().Select(JsonElementExtensions.Id));
        Assert.Equal([id], (await api.JsonAsync(HttpMethod.Get, CandidatesRoute, Profile.Mentor)).EnumerateArray().Select(JsonElementExtensions.Id));
        Assert.Empty((await api.JsonAsync(HttpMethod.Get, CandidatesRoute, Profile.Applicant)).EnumerateArray());

        var accepted = await DecideAsync(api, Accept(id), Profile.Mentor, invited);
        Assert.Equal(("Accepted", "Accepted", 2L), (accepted.Text("status"), accepted.Text("mentorResponse"), accepted.GetProperty("version").GetInt64()));
        Assert.NotNull(accepted.OptionalTime("mentorRespondedAt"));

        // One participant's willingness is not mutual acceptance.
        var protegeWilling = await DecideAsync(api, Willingness(id), Profile.Employee, accepted);
        Assert.Equal("Accepted", protegeWilling.Text("status"));
        Assert.NotNull(protegeWilling.OptionalTime("protegeWillingAt"));
        Assert.Null(protegeWilling.OptionalTime("mentorWillingAt"));

        var drafting = await DecideAsync(api, Willingness(id), Profile.Mentor, protegeWilling);
        Assert.Equal(("Drafting", 4L), (drafting.Text("status"), drafting.GetProperty("version").GetInt64()));
        Assert.Equal(protegeWilling.OptionalTime("protegeWillingAt"), drafting.OptionalTime("protegeWillingAt"));
        Assert.NotNull(drafting.OptionalTime("mentorWillingAt"));
        Assert.Equal("DRAFTING", await StatusAsync(api.Db, id));

        // The trail names each decision's own actor, at the server time the candidate records for it.
        var entries = (await api.JsonAsync(HttpMethod.Get, Evidence(id), Profile.Administrator)).Items("entries");
        Assert.Equal(["InvitationSent", "MentorResponded", "WillingnessRecorded", "WillingnessRecorded"], entries.Select(entry => entry.Text("action")));
        Assert.Equal([protege, mentor, protege, mentor], entries.Select(entry => entry.GetProperty("actor").GetProperty("employeeId").GetGuid()));
        Assert.Equal(
            [drafting.OptionalTime("invitedAt"), drafting.OptionalTime("mentorRespondedAt"), drafting.OptionalTime("protegeWillingAt"), drafting.OptionalTime("mentorWillingAt")],
            entries.Select(entry => entry.OptionalTime("occurredAt")));
        Assert.Equal([null, "ACCEPTED", null, null], entries.Select(entry => entry.GetProperty("outcome").GetString()));
        Assert.All(entries, entry =>
        {
            Assert.Equal(mode.ToString(), entry.GetProperty("actor").Text("identityScheme"));
            Assert.Equal(("Candidate", "Application"), (entry.Text("recordType"), entry.Text("provenance")));
            Assert.Equal(id, entry.OptionalGuid("recordId"));
            Assert.Equal(id, entry.OptionalGuid("candidateId"));
        });
        Assert.Equal(mentor, entries[0].GetProperty("newValues").GetProperty("mentorEmployeeId").GetGuid());
        Assert.Equal(("ACCEPTED", "ACCEPTED"), (entries[2].GetProperty("newValues").Text("status"), entries[2].GetProperty("priorValues").Text("status")));
        Assert.Equal(drafting.OptionalTime("protegeWillingAt"), entries[2].GetProperty("newValues").OptionalTime("protegeWillingAt"));
        Assert.Equal("DRAFTING", entries[3].GetProperty("newValues").Text("status"));
        Assert.Equal(drafting.OptionalTime("mentorWillingAt"), entries[3].GetProperty("newValues").OptionalTime("mentorWillingAt"));

        // Each decision is notified to the other participant with the next action it asks of them (FR-057).
        Assert.Equal([("WillingnessRecorded", id, "RecordWillingness"), ("InvitationSent", id, "RespondToInvitation")], await InboxAsync(api, Profile.Mentor));
        Assert.Equal([("WillingnessRecorded", id, "PrepareAgreementDraft"), ("MentorResponded", id, "RecordWillingness")], await InboxAsync(api, Profile.Employee));
        Assert.Empty(await InboxAsync(api, Profile.Administrator));
    }

    // AC-FR-019-01, AC-FR-019-02, AC-FR-021-01, AC-DR-004-01
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task A_declined_invitation_ends_the_pairing_without_mutual_acceptance_or_a_relationship(IdentityMode mode)
    {
        await using var api = await WorkflowApi.StartAsync(server, mode);
        var invited = await CandidateAsync(api);
        var id = invited.Id();

        var declined = await DecideAsync(api, Decline(id), Profile.Mentor, invited);

        Assert.Equal(("Declined", "Declined"), (declined.Text("status"), declined.Text("mentorResponse")));
        Assert.NotNull(declined.OptionalTime("mentorRespondedAt"));
        // Nothing builds on a declined invitation: no willingness, no second answer, no withdrawal of what has ended.
        foreach (var (route, caller) in new[]
                 {
                     (Willingness(id), Profile.Employee), (Willingness(id), Profile.Mentor), (Accept(id), Profile.Mentor),
                     (Decline(id), Profile.Mentor), (Withdraw(id), Profile.Employee), (Withdraw(id), Profile.Mentor),
                 })
        {
            var refused = await ProblemAssert.IsProblem(HttpStatusCode.Conflict, await TryAsync(api, route, caller, declined));
            Assert.Equal(MentorshipRequestEndpoints.NotPossibleNow, refused.Text("title"));
        }

        Assert.Equal(("DECLINED", 2L), (await StatusAsync(api.Db, id), await VersionAsync(api.Db, id)));
        Assert.Equal(0L, await RelationshipsAsync(api.Db, id));
        Assert.Null(declined.OptionalTime("protegeWillingAt"));
        var entry = (await api.JsonAsync(HttpMethod.Get, Evidence(id), Profile.Administrator)).Items("entries")[^1];
        Assert.Equal(("MentorResponded", "DECLINED"), (entry.Text("action"), entry.Text("outcome")));
        Assert.Equal(("MentorResponded", id, "SelectAnotherMentor"), (await InboxAsync(api, Profile.Employee))[0]);

        // The protégé may select again, the same mentor included; the new candidate carries no decision of the old.
        var again = await SelectAsync(api, RequestOf(invited), Profile.Employee, api.Employee(Profile.Mentor));
        Assert.NotEqual(id, again.Id());
        Assert.Equal(("Invited", "Pending", 1L), (again.Text("status"), again.Text("mentorResponse"), again.GetProperty("version").GetInt64()));
        Assert.Null(again.OptionalTime("mentorRespondedAt"));
    }

    // ADR-003, ADR-015: only the named approved mentor answers; each participant records only their own willingness and
    // either may withdraw; Talent Management takes none of these decisions; nobody else reaches them or reads the candidate.
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task Only_the_participants_take_the_pairing_decisions_each_only_their_own(IdentityMode mode)
    {
        await using var api = await WorkflowApi.StartAsync(server, mode);
        await ApproveApplicantAsync(api);
        var invited = await CandidateAsync(api);
        var id = invited.Id();

        // Only the named mentor answers: not the protégé, not another approved mentor, not Talent Management.
        foreach (var caller in new[] { Profile.Employee, Profile.Applicant, Profile.Administrator, Profile.Newcomer })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await TryAsync(api, Accept(id), caller, invited)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await TryAsync(api, Decline(id), caller, invited)).StatusCode);
        }

        await ProblemAssert.IsProblem(HttpStatusCode.Unauthorized, await TryAsync(api, Accept(id), caller: null, invited));
        Assert.Equal(HttpStatusCode.NotFound, (await TryAsync(api, Accept(Guid.NewGuid()), Profile.Mentor, invited)).StatusCode);
        var accepted = await DecideAsync(api, Accept(id), Profile.Mentor, invited);

        // Willingness and withdrawal are the participants' alone.
        foreach (var caller in new[] { Profile.Applicant, Profile.Administrator, Profile.Newcomer })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await TryAsync(api, Willingness(id), caller, accepted)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await TryAsync(api, Withdraw(id), caller, accepted)).StatusCode);
        }

        // The protégé's willingness is theirs: it sets the protégé's and never the mentor's, and is recorded once.
        var protegeWilling = await DecideAsync(api, Willingness(id), Profile.Employee, accepted);
        Assert.Null(protegeWilling.OptionalTime("mentorWillingAt"));
        var twice = await ProblemAssert.IsProblem(HttpStatusCode.Conflict, await TryAsync(api, Willingness(id), Profile.Employee, protegeWilling));
        Assert.Equal(MentorshipRequestEndpoints.NotPossibleNow, twice.Text("title"));
        Assert.Equal(("ACCEPTED", 3L), (await StatusAsync(api.Db, id), await VersionAsync(api.Db, id)));

        // The candidate is read by its participants and Talent Management alone.
        foreach (var reader in new[] { Profile.Employee, Profile.Mentor, Profile.Administrator })
        {
            Assert.Equal(protegeWilling.GetRawText(), (await api.JsonAsync(HttpMethod.Get, Candidate(id), reader)).GetRawText());
        }

        Assert.Equal(HttpStatusCode.Forbidden, await api.StatusAsync(HttpMethod.Get, Candidate(id), Profile.Applicant));
        Assert.Equal(HttpStatusCode.Forbidden, await api.StatusAsync(HttpMethod.Get, Candidate(id), Profile.Newcomer));
        Assert.Equal(HttpStatusCode.NotFound, await api.StatusAsync(HttpMethod.Get, Candidate(Guid.NewGuid()), Profile.Administrator));
        // Talent Management lists the request's candidates; a mentor named by none of them lists nothing of the request.
        Assert.Equal([id], (await api.JsonAsync(HttpMethod.Get, RequestCandidates(RequestOf(invited)), Profile.Administrator)).EnumerateArray().Select(JsonElementExtensions.Id));
        Assert.Equal(HttpStatusCode.Forbidden, await api.StatusAsync(HttpMethod.Get, RequestCandidates(RequestOf(invited)), Profile.Applicant));
    }

    // AC-FR-021-01, AC-FR-021-02: either participant withdraws before formation; the pairing keeps its withdrawn
    // disposition, acquires no relationship, and the draft evidence of its current revision stops applying.
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task A_withdrawn_pairing_keeps_its_disposition_acquires_no_relationship_and_its_draft_evidence_stops_applying(IdentityMode mode)
    {
        await using var api = await WorkflowApi.StartAsync(server, mode);
        var (protege, mentor, administrator) = (api.Employee(Profile.Employee), api.Employee(Profile.Mentor), api.Employee(Profile.Administrator));

        // The protégé withdraws while the invitation is pending; the mentor once they have accepted.
        var invited = await CandidateAsync(api);
        var byProtege = await DecideAsync(api, Withdraw(invited.Id()), Profile.Employee, invited);
        var accepted = await CandidateAsync(api, "Accepted");
        var byMentor = await DecideAsync(api, Withdraw(accepted.Id()), Profile.Mentor, accepted);

        Assert.Equal(("Withdrawn", protege), (byProtege.Text("status"), byProtege.OptionalGuid("withdrawnByEmployeeId")!.Value));
        Assert.Equal(("Withdrawn", mentor), (byMentor.Text("status"), byMentor.OptionalGuid("withdrawnByEmployeeId")!.Value));
        Assert.Contains(("CandidateWithdrawn", invited.Id(), "None"), await InboxAsync(api, Profile.Mentor));
        Assert.Contains(("CandidateWithdrawn", accepted.Id(), "SelectAnotherMentor"), await InboxAsync(api, Profile.Employee));

        // While drafting: a revision both participants accepted and a rejected formation decision on it, as the draft and
        // formation commands (TASK-09, TASK-10) leave them.
        var drafting = await CandidateAsync(api, "Drafting");
        var seed = new Seed(api.Db);
        var revision = await seed.RevisionAsync(drafting.Id(), protege);
        var protegeAcceptance = await seed.AcceptanceAsync(revision, protege);
        var mentorAcceptance = await seed.AcceptanceAsync(revision, mentor);
        var rejection = await seed.FormationDecisionAsync(drafting.Id(), revision, administrator, "REJECTED");
        var current = await api.JsonAsync(HttpMethod.Get, Candidate(drafting.Id()), Profile.Employee);
        Assert.Equal(revision.Id, current.OptionalGuid("currentRevisionId"));

        var withdrawn = await DecideAsync(api, Withdraw(drafting.Id()), Profile.Employee, current);

        Assert.Equal(("Withdrawn", revision.Id), (withdrawn.Text("status"), withdrawn.OptionalGuid("currentRevisionId")!.Value));
        var evidence = await api.JsonAsync(HttpMethod.Get, Evidence(drafting.Id()), Profile.Administrator);
        var entry = evidence.Items("entries").Single(item => item.Text("action") == "CandidateWithdrawn");
        Assert.Equal(("WITHDRAWN", protege), (entry.Text("outcome"), entry.GetProperty("actor").GetProperty("employeeId").GetGuid()));
        Assert.Equal((revision.Id, revision.ContentHash), (entry.OptionalGuid("revisionId")!.Value, entry.Text("contentHash")));
        Assert.Equal([protegeAcceptance, mentorAcceptance], entry.Items("invalidatedAcceptanceIds").Select(item => item.GetGuid()));
        Assert.Equal([rejection], entry.Items("invalidatedDecisionIds").Select(item => item.GetGuid()));
        Assert.Equal(("DRAFTING", "WITHDRAWN"), (entry.GetProperty("priorValues").Text("status"), entry.GetProperty("newValues").Text("status")));
        Assert.Equal(withdrawn.OptionalTime("withdrawnAt"), entry.OptionalTime("occurredAt"));
        Assert.All(evidence.Items("acceptances"), acceptance => Assert.Equal("Withdrawn", acceptance.Text("applicability")));
        Assert.All(evidence.Items("decisions"), decision => Assert.Equal("Withdrawn", decision.Text("applicability")));
        Assert.Contains(("CandidateWithdrawn", drafting.Id(), "None"), await InboxAsync(api, Profile.Mentor));

        // A withdrawn pairing is final: it takes no decision, is not withdrawn again and never becomes a relationship.
        foreach (var ended in new[] { byProtege, byMentor, withdrawn })
        {
            foreach (var (route, caller) in new[]
                     {
                         (Accept(ended.Id()), Profile.Mentor), (Willingness(ended.Id()), Profile.Employee), (Willingness(ended.Id()), Profile.Mentor),
                         (Withdraw(ended.Id()), Profile.Employee), (Withdraw(ended.Id()), Profile.Mentor),
                     })
            {
                Assert.Equal(HttpStatusCode.Conflict, (await TryAsync(api, route, caller, ended)).StatusCode);
            }

            Assert.Equal("WITHDRAWN", await StatusAsync(api.Db, ended.Id()));
            Assert.Equal(0L, await RelationshipsAsync(api.Db, ended.Id()));
        }
    }

    // AC-FR-018-02: once the mentor accepts, an introductory conversation is proposed for the candidate and can be marked
    // arranged outside the application; it stays with the candidate whatever the candidate becomes.
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task An_introduction_stays_associated_with_its_candidate_and_can_be_marked_arranged_outside_the_application(IdentityMode mode)
    {
        await using var api = await WorkflowApi.StartAsync(server, mode);
        var invited = await CandidateAsync(api);
        var id = invited.Id();
        Assert.Equal(HttpStatusCode.Conflict, await api.StatusAsync(HttpMethod.Post, Introductions(id), Profile.Employee, new { }));

        var accepted = await DecideAsync(api, Accept(id), Profile.Mentor, invited);
        var introduction = await api.JsonAsync(HttpMethod.Post, Introductions(id), Profile.Employee, new { arrangedOutsideApplication = true }, HttpStatusCode.Created);

        Assert.Equal(("Introductory", id), (introduction.Text("kind"), introduction.OptionalGuid("candidateId")!.Value));
        Assert.True(introduction.GetProperty("arrangedOutsideApplication").GetBoolean());
        Assert.Null(introduction.OptionalTime("scheduledAt"));
        // Recording the introduction is no pairing decision: the candidate's version is unchanged.
        Assert.Equal(accepted.GetRawText(), (await api.JsonAsync(HttpMethod.Get, Candidate(id), Profile.Employee)).GetRawText());

        var protegeWilling = await DecideAsync(api, Willingness(id), Profile.Employee, accepted);
        var drafting = await DecideAsync(api, Willingness(id), Profile.Mentor, protegeWilling);
        await DecideAsync(api, Withdraw(id), Profile.Mentor, drafting);

        foreach (var viewer in new[] { Profile.Employee, Profile.Mentor, Profile.Administrator })
        {
            var shown = Assert.Single((await api.JsonAsync(HttpMethod.Get, Introductions(id), viewer)).EnumerateArray());
            Assert.Equal(introduction.Id(), shown.Id());
            Assert.True(shown.GetProperty("arrangedOutsideApplication").GetBoolean());
        }
    }

    // FR-008, FR-018, BR-002: the protégé selects, on their own request, an approved mentor other than themselves,
    // whatever that mentor's existing relationships; nobody else selects for them.
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task A_protege_selects_on_their_own_request_only_an_approved_mentor_other_than_themselves(IdentityMode mode)
    {
        await using var api = await WorkflowApi.StartAsync(server, mode);
        var request = await SubmitAsync(api, Profile.Employee);
        var (declinedMentor, _) = await MentorRegistryApi.MentorAsync(api.Db, "Zanele Dlamini", api.Employee(Profile.Administrator), "DECLINED");

        // A pending applicant, a declined one, an employee who never applied and an unknown id are all equally undiscoverable.
        foreach (var notDiscoverable in new[] { api.Employee(Profile.Applicant), declinedMentor, api.Employee(Profile.Administrator), Guid.NewGuid() })
        {
            var refused = await ProblemAssert.IsProblem(HttpStatusCode.UnprocessableEntity,
                await api.SendAsync(HttpMethod.Post, RequestCandidates(request), Profile.Employee, new { mentorEmployeeId = notDiscoverable }));
            Assert.Equal(MentorshipRequestEndpoints.NotDiscoverable, refused.Text("title"));
        }

        var ownRequest = await SubmitAsync(api, Profile.Mentor);
        var self = await ProblemAssert.IsProblem(HttpStatusCode.UnprocessableEntity,
            await api.SendAsync(HttpMethod.Post, RequestCandidates(ownRequest), Profile.Mentor, new { mentorEmployeeId = api.Employee(Profile.Mentor) }));
        Assert.Equal(MentorshipRequestEndpoints.OwnRequest, self.Text("title"));
        await ProblemAssert.IsProblem(HttpStatusCode.BadRequest, await api.SendAsync(HttpMethod.Post, RequestCandidates(request), Profile.Employee, new { }));
        await ProblemAssert.IsProblem(HttpStatusCode.BadRequest, await api.SendAsync(HttpMethod.Post, RequestCandidates(request), Profile.Employee,
            new { mentorEmployeeId = api.Employee(Profile.Mentor), protegeEmployeeId = api.Employee(Profile.Applicant) }));

        // Only the request's protégé selects for it, Talent Management included.
        foreach (var other in new[] { Profile.Mentor, Profile.Applicant, Profile.Administrator, Profile.Newcomer })
        {
            Assert.Equal(HttpStatusCode.Forbidden,
                await api.StatusAsync(HttpMethod.Post, RequestCandidates(request), other, new { mentorEmployeeId = api.Employee(Profile.Mentor) }));
        }

        await ProblemAssert.IsProblem(HttpStatusCode.Unauthorized,
            await api.SendAsync(HttpMethod.Post, RequestCandidates(request), profile: null, new { mentorEmployeeId = api.Employee(Profile.Mentor) }));
        Assert.Equal(HttpStatusCode.NotFound,
            await api.StatusAsync(HttpMethod.Post, RequestCandidates(Guid.NewGuid()), Profile.Employee, new { mentorEmployeeId = api.Employee(Profile.Mentor) }));
        Assert.Equal(0L, await api.Db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.mentor_selection"));

        // A mentor who already mentors two people is selected like any other (BR-002).
        await CallerMentorship.FormedAsync(api.Db, api.Callers);
        await CallerMentorship.FormedAsync(api.Db, api.Callers);
        var invited = await SelectAsync(api, request, Profile.Employee, api.Employee(Profile.Mentor));
        Assert.Equal(("Invited", request), (invited.Text("status"), RequestOf(invited)));
    }

    // ADR-015: a request holds one candidate at a time; another mentor is selected once the standing one has ended.
    [Fact]
    public async Task A_request_holds_one_candidate_at_a_time()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var applicant = await ApproveApplicantAsync(api);
        var invited = await CandidateAsync(api);
        var request = RequestOf(invited);

        var whileInvited = await ProblemAssert.IsProblem(HttpStatusCode.Conflict,
            await api.SendAsync(HttpMethod.Post, RequestCandidates(request), Profile.Employee, new { mentorEmployeeId = applicant }));
        var accepted = await DecideAsync(api, Accept(invited.Id()), Profile.Mentor, invited);
        var whileAccepted = await api.StatusAsync(HttpMethod.Post, RequestCandidates(request), Profile.Employee, new { mentorEmployeeId = applicant });

        Assert.Equal(MentorshipRequestEndpoints.CandidateInProgress, whileInvited.Text("title"));
        Assert.Equal(HttpStatusCode.Conflict, whileAccepted);
        await DecideAsync(api, Withdraw(invited.Id()), Profile.Employee, accepted);
        var next = await SelectAsync(api, request, Profile.Employee, applicant);
        Assert.Equal((applicant, "Applicant"), (next.GetProperty("mentorEmployeeId").GetGuid(), next.Text("mentorDisplayName")));
        Assert.Equal([invited.Id(), next.Id()], (await api.JsonAsync(HttpMethod.Get, RequestCandidates(request), Profile.Employee)).EnumerateArray().Select(JsonElementExtensions.Id));
    }

    // Two selections racing each other on one request: the request row queues them, and the second sees the first.
    [Fact]
    public async Task Two_simultaneous_selections_on_one_request_end_with_one_candidate()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var applicant = await ApproveApplicantAsync(api);
        var request = await SubmitAsync(api, Profile.Employee);

        var responses = await Task.WhenAll(
            api.SendAsync(HttpMethod.Post, RequestCandidates(request), Profile.Employee, new { mentorEmployeeId = api.Employee(Profile.Mentor) }),
            api.SendAsync(HttpMethod.Post, RequestCandidates(request), Profile.Employee, new { mentorEmployeeId = applicant }));

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], responses.Select(response => response.StatusCode).Order());
        Assert.Equal(1L, await api.Db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.mentor_selection WHERE request_id = $1", request));
        Assert.Equal(1L, await api.Db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.audit_entry WHERE action = 'INVITATION_SENT'"));
        Assert.Equal(1L, await api.Db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.notification_event WHERE event_type = 'INVITATION_SENT'"));
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }
}

using System.Net;
using Mentor.Api.Hosting;
using Mentor.Api.Requests;
using Mentor.Tests.Integration.Persistence;
using Mentor.Tests.Integration.Support;
using Npgsql;
using static Mentor.Tests.Integration.Requests.PairingApi;

namespace Mentor.Tests.Integration.Requests;

// BR-021, E2E-11: a candidate's participants are fixed. Before formation a party is substituted by withdrawing the
// candidate, which takes its pairing and draft decisions out of effect, and selecting anew, which needs fresh decisions
// by the correct named parties; after formation a different mentor needs a new request. COMP-MENTORSHIP-REQUEST.
public sealed class PartySubstitutionTests(PostgresServer server)
{
    // AC-BR-021-01, AC-E2E-11
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task Substituting_the_mentor_before_formation_invalidates_the_old_pairing_and_needs_fresh_decisions_by_the_new_parties(IdentityMode mode)
    {
        await using var api = await WorkflowApi.StartAsync(server, mode);
        var (protege, mentor, administrator) = (api.Employee(Profile.Employee), api.Employee(Profile.Mentor), api.Employee(Profile.Administrator));
        var applicant = await ApproveApplicantAsync(api);

        // The original pairing reaches drafting: a revision both accepted and a rejected formation decision on it, as the
        // draft and formation commands (TASK-09, TASK-10) leave them.
        var old = await CandidateAsync(api, "Drafting");
        var request = RequestOf(old);
        var seed = new Seed(api.Db);
        var revision = await seed.RevisionAsync(old.Id(), protege);
        var acceptances = new[] { await seed.AcceptanceAsync(revision, protege), await seed.AcceptanceAsync(revision, mentor) };
        var rejection = await seed.FormationDecisionAsync(old.Id(), revision, administrator, "REJECTED");
        old = await api.JsonAsync(HttpMethod.Get, Candidate(old.Id()), Profile.Employee);

        // The participants cannot be changed in place: no operation takes a mentor for an existing candidate, the
        // application's database account cannot rewrite one, and another mentor is refused while the pairing stands.
        Assert.Equal(HttpStatusCode.Forbidden,
            await api.StatusAsync(HttpMethod.Put, Candidate(old.Id()), Profile.Employee, new { mentorEmployeeId = applicant }, Tag(old)));
        await Rejected.WithAsync(PostgresErrorCodes.InsufficientPrivilege, () => api.Db.ExecuteAsync(Login.App,
            "UPDATE mentor.mentor_selection SET mentor_employee_id = $2 WHERE id = $1", old.Id(), applicant));
        var whileStanding = await ProblemAssert.IsProblem(HttpStatusCode.Conflict,
            await api.SendAsync(HttpMethod.Post, RequestCandidates(request), Profile.Employee, new { mentorEmployeeId = applicant }));
        Assert.Equal(MentorshipRequestEndpoints.CandidateInProgress, whileStanding.Text("title"));

        // The protégé withdraws the pairing: every acceptance and decision of it stops applying, and the trail says which.
        await DecideAsync(api, Withdraw(old.Id()), Profile.Employee, old);
        var evidence = await api.JsonAsync(HttpMethod.Get, Evidence(old.Id()), Profile.Administrator);
        var withdrawal = evidence.Items("entries").Single(entry => entry.Text("action") == "CandidateWithdrawn");
        Assert.Equal(acceptances, withdrawal.Items("invalidatedAcceptanceIds").Select(item => item.GetGuid()));
        Assert.Equal([rejection], withdrawal.Items("invalidatedDecisionIds").Select(item => item.GetGuid()));
        Assert.All(evidence.Items("acceptances").Concat(evidence.Items("decisions")), item => Assert.Equal("Withdrawn", item.Text("applicability")));

        // The new pairing starts from nothing: no response, no willingness, no revision, a version of its own.
        var fresh = await SelectAsync(api, request, Profile.Employee, applicant);
        Assert.Equal((request, applicant, "Applicant"), (RequestOf(fresh), fresh.GetProperty("mentorEmployeeId").GetGuid(), fresh.Text("mentorDisplayName")));
        Assert.Equal(("Invited", "Pending", 1L), (fresh.Text("status"), fresh.Text("mentorResponse"), fresh.GetProperty("version").GetInt64()));
        Assert.Null(fresh.OptionalTime("protegeWillingAt"));
        Assert.Null(fresh.OptionalGuid("currentRevisionId"));

        // The old mentor takes no decision on it; the new mentor and the protégé decide anew, the protégé's earlier
        // willingness counting for nothing.
        Assert.Equal(HttpStatusCode.Forbidden, (await TryAsync(api, Accept(fresh.Id()), Profile.Mentor, fresh)).StatusCode);
        var accepted = await DecideAsync(api, Accept(fresh.Id()), Profile.Applicant, fresh);
        var protegeWilling = await DecideAsync(api, Willingness(fresh.Id()), Profile.Employee, accepted);
        Assert.Equal("Accepted", protegeWilling.Text("status"));
        var drafting = await DecideAsync(api, Willingness(fresh.Id()), Profile.Applicant, protegeWilling);
        Assert.Equal("Drafting", drafting.Text("status"));
        var freshEvidence = await api.JsonAsync(HttpMethod.Get, Evidence(fresh.Id()), Profile.Administrator);
        Assert.Empty(freshEvidence.Items("acceptances"));
        Assert.Empty(freshEvidence.Items("decisions"));
        Assert.Equal([protege, applicant, protege, applicant],
            freshEvidence.Items("entries").Select(entry => entry.GetProperty("actor").GetProperty("employeeId").GetGuid()));

        // Neither pairing has an operational State: no relationship exists until formation's prerequisites hold.
        Assert.Equal(0L, await RelationshipsAsync(api.Db, old.Id()));
        Assert.Equal(0L, await RelationshipsAsync(api.Db, fresh.Id()));
        Assert.Equal("WITHDRAWN", await StatusAsync(api.Db, old.Id()));
        // The old mentor reads the request no longer; the new one does.
        Assert.Equal(HttpStatusCode.Forbidden, await api.StatusAsync(HttpMethod.Get, Request(request), Profile.Mentor));
        Assert.Equal(HttpStatusCode.OK, await api.StatusAsync(HttpMethod.Get, Request(request), Profile.Applicant));
    }

    // AC-E2E-11 (after formation), FR-032: a request a relationship was formed from takes no further pairing; a different
    // mentor needs a new request, and the formed relationship is untouched.
    [Fact]
    public async Task After_formation_a_different_mentor_needs_a_new_request()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var applicant = await ApproveApplicantAsync(api);
        var mentorship = await CallerMentorship.FormedAsync(api.Db, api.Callers);

        var closed = await ProblemAssert.IsProblem(HttpStatusCode.Conflict,
            await api.SendAsync(HttpMethod.Post, RequestCandidates(mentorship.Request), Profile.Employee, new { mentorEmployeeId = applicant }));

        Assert.Equal(MentorshipRequestEndpoints.RequestClosed, closed.Text("title"));
        Assert.Equal(1L, await api.Db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.mentor_selection WHERE request_id = $1", mentorship.Request));
        var request = await SubmitAsync(api, Profile.Employee);
        var fresh = await SelectAsync(api, request, Profile.Employee, applicant);
        Assert.Equal(("Invited", request), (fresh.Text("status"), RequestOf(fresh)));
        Assert.Equal(("FORMED", "IN_PROGRESS"), (await StatusAsync(api.Db, mentorship.Candidate),
            await api.Db.ScalarAsync<string>(Login.Owner, "SELECT state FROM mentor.mentorship_relationship WHERE id = $1", mentorship.Relationship)));
    }
}

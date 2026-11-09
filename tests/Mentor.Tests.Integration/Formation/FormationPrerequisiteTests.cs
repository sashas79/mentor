using System.Net;
using System.Text.Json;
using Mentor.Api.Agreements;
using Mentor.Api.Formation;
using Mentor.Api.Requests;
using Mentor.Tests.Integration.Agreements;
using Mentor.Tests.Integration.Persistence;
using Mentor.Tests.Integration.Requests;
using Mentor.Tests.Integration.Support;
using static Mentor.Tests.Integration.Formation.FormationApi;

namespace Mentor.Tests.Integration.Formation;

// The prerequisites of formation, checked at the moment of formation under the candidate's version lock (ADR-005, FR-030,
// FR-031, FR-070, BR-003, BR-021): a missing, superseded, stale or withdrawn prerequisite fails and persists nothing:
// no decision, relationship, formed agreement or goal, no audit entry and no notification.
public sealed class FormationPrerequisiteTests(PostgresServer server)
{
    // AC-E2E-02, AC-FR-030-01, AC-FR-031-01: without both participants' acceptances of the current revision Talent
    // Management decides nothing, and two acceptances without an approval form nothing.
    [Fact]
    public async Task Without_both_acceptances_of_the_current_revision_nothing_is_decided_or_formed()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var revised = await DraftApi.ReviseAsync(api, await DraftApi.DraftingAsync(api), Profile.Employee);
        var candidate = DraftApi.CandidateOf(revised);
        var protegeAccepted = await DraftApi.AcceptAsync(api, revised, Profile.Employee);
        var (entries, notifications) = (await PairingApi.AuditEntriesAsync(api.Db, candidate), await PairingApi.NotificationsAsync(api.Db, candidate));

        foreach (var outcome in new[] { "Approved", "Rejected" })
        {
            var refused = await ProblemAssert.IsProblem(HttpStatusCode.Conflict, await TryDecideAsync(api, protegeAccepted, Profile.Administrator, outcome));
            Assert.Equal(FormationEndpoints.NotAccepted, refused.Text("title"));
        }

        Assert.Equal(2, DraftApi.Step(protegeAccepted));
        Assert.Equal(protegeAccepted.GetProperty("version").GetInt64(), await PairingApi.VersionAsync(api.Db, candidate));
        Assert.Equal((entries, notifications), (await PairingApi.AuditEntriesAsync(api.Db, candidate), await PairingApi.NotificationsAsync(api.Db, candidate)));

        var bothAccepted = await DraftApi.AcceptAsync(api, protegeAccepted, Profile.Mentor);

        Assert.Equal(3, DraftApi.Step(bothAccepted));
        Assert.Equal(("DRAFTING", 0L, 0L, 0L),
            (await PairingApi.StatusAsync(api.Db, candidate), await DecisionCountAsync(api.Db, candidate), await PairingApi.RelationshipsAsync(api.Db, candidate),
                await GoalCountAsync(api.Db, candidate)));
        var formation = await ReadAsync(api, candidate, Profile.Administrator);
        Assert.Equal(JsonValueKind.Null, Relationship(formation).ValueKind);
    }

    // AC-E2E-10, AC-FR-030-03, AC-FR-070-01, AC-FR-070-02: Talent Management reviewed draft A, then a participant replaced it.
    // The approval on the version read fails; on the current version naming draft A it fails; naming draft B with draft
    // A's hash it fails; and draft B, not yet accepted by both, cannot be approved. Only after both accept draft B does an
    // approval of draft B form the relationship, on draft B.
    [Fact]
    public async Task A_stale_approval_or_one_of_superseded_content_forms_nothing()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var draftA = await BothAcceptedAsync(api);
        var candidate = DraftApi.CandidateOf(draftA);
        var revisionA = DraftApi.Current(draftA);
        var draftB = await DraftApi.ReviseAsync(api, draftA, Profile.Mentor, DraftApi.Form("Lead two design reviews by June 2027"));
        var revisionB = DraftApi.Current(draftB);

        var stale = await ProblemAssert.IsProblem(HttpStatusCode.Conflict, await TryDecideAsync(api, draftA, Profile.Administrator));
        var superseded = await ProblemAssert.IsProblem(HttpStatusCode.Conflict,
            await TryDecideAsync(api, draftB, Profile.Administrator, revision: revisionA.Id(), contentHash: revisionA.Text("contentHash")));
        var mismatch = await ProblemAssert.IsProblem(HttpStatusCode.UnprocessableEntity,
            await TryDecideAsync(api, draftB, Profile.Administrator, contentHash: revisionA.Text("contentHash")));
        var notAccepted = await ProblemAssert.IsProblem(HttpStatusCode.Conflict, await TryDecideAsync(api, draftB, Profile.Administrator));

        Assert.Equal(
            [MentorshipRequestEndpoints.CandidateChanged, AgreementDraftEndpoints.RevisionSuperseded, AgreementDraftEndpoints.ContentMismatch, FormationEndpoints.NotAccepted],
            new[] { stale, superseded, mismatch, notAccepted }.Select(problem => problem.Text("title")));
        Assert.Equal((0L, 0L), (await DecisionCountAsync(api.Db, candidate), await PairingApi.RelationshipsAsync(api.Db, candidate)));
        Assert.Equal(draftB.GetProperty("version").GetInt64(), await PairingApi.VersionAsync(api.Db, candidate));

        var formed = await DecideAsync(api, await BothAcceptAsync(api, draftB, Profile.Mentor));

        var decision = Assert.Single(formed.Items("decisions"));
        Assert.Equal((revisionB.Id(), 2, revisionB.Text("contentHash")),
            (decision.GetProperty("revisionId").GetGuid(), decision.GetProperty("revisionNumber").GetInt32(), decision.Text("contentHash")));
        Assert.Equal(revisionB.Text("contentHash"), Relationship(formed).GetProperty("agreement").Text("contentHash"));
        Assert.Equal("Lead two design reviews by June 2027", GoalsOf(formed)[0]);
        Assert.Equal([("Protege", 1, "Superseded"), ("Mentor", 1, "Superseded"), ("Protege", 2, "Formed"), ("Mentor", 2, "Formed")],
            DraftApi.AcceptancesOf(await DraftApi.ReadAsync(api, candidate, Profile.Administrator)));
    }

    // ADR-004, AC-FR-027-04, AC-BR-019-01: an acceptance is of a revision, not of its content alone. A replacement that
    // restores the content both participants accepted is a new revision with the same hash and none of the acceptances:
    // both accept it afresh before Talent Management can approve it, and the formation names the new revision.
    [Fact]
    public async Task Restored_content_in_a_new_revision_needs_both_acceptances_afresh()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var draftA = await BothAcceptedAsync(api);
        var candidate = DraftApi.CandidateOf(draftA);
        var revisionA = DraftApi.Current(draftA);
        var draftB = await DraftApi.ReviseAsync(api, draftA, Profile.Mentor, DraftApi.Form("Lead two design reviews by June 2027"));
        var restored = await DraftApi.ReviseAsync(api, draftB, Profile.Mentor);
        var revisionC = DraftApi.Current(restored);
        Assert.Equal((3, revisionA.Text("contentHash"), (int?)1),
            (revisionC.GetProperty("revisionNumber").GetInt32(), revisionC.Text("contentHash"), DraftApi.Step(restored)));

        var refused = await ProblemAssert.IsProblem(HttpStatusCode.Conflict, await TryDecideAsync(api, restored, Profile.Administrator));

        Assert.Equal(FormationEndpoints.NotAccepted, refused.Text("title"));
        Assert.Equal((0L, 0L), (await DecisionCountAsync(api.Db, candidate), await PairingApi.RelationshipsAsync(api.Db, candidate)));

        var formed = await DecideAsync(api, await BothAcceptAsync(api, restored, Profile.Mentor));

        var decision = Assert.Single(formed.Items("decisions"));
        Assert.Equal((revisionC.Id(), 3), (decision.GetProperty("revisionId").GetGuid(), decision.GetProperty("revisionNumber").GetInt32()));
        Assert.Equal(revisionC.Id(), Relationship(formed).GetProperty("agreement").GetProperty("revisionId").GetGuid());
    }

    // AC-FR-070-01, ADR-005: an approval and a replacement taken at once on one version queue on the candidate. Exactly one
    // is taken: either the relationship is formed and the replacement refused, or the replacement stands and nothing is formed.
    [Fact]
    public async Task An_approval_and_a_replacement_taken_at_once_end_with_exactly_one_taken()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var bothAccepted = await BothAcceptedAsync(api);
        var candidate = DraftApi.CandidateOf(bothAccepted);

        var responses = await Task.WhenAll(
            TryDecideAsync(api, bothAccepted, Profile.Administrator),
            DraftApi.TryReviseAsync(api, bothAccepted, Profile.Mentor, DraftApi.Form("Lead two design reviews by June 2027")));

        Assert.Single(responses, response => response.IsSuccessStatusCode);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        var formedFirst = responses[0].IsSuccessStatusCode;
        Assert.Equal((formedFirst ? "FORMED" : "DRAFTING", formedFirst ? 1L : 0L, formedFirst ? 1L : 0L, formedFirst ? 1L : 2L),
            (await PairingApi.StatusAsync(api.Db, candidate), await DecisionCountAsync(api.Db, candidate), await PairingApi.RelationshipsAsync(api.Db, candidate),
                await DraftApi.RevisionCountAsync(api.Db, candidate)));
        Assert.Equal(bothAccepted.GetProperty("version").GetInt64() + 1, await PairingApi.VersionAsync(api.Db, candidate));
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    // ADR-003: formation is decided by Talent Management alone; the participants, an applicant and a newcomer are refused
    // and an anonymous caller is unauthenticated. Nothing is recorded.
    [Fact]
    public async Task Only_talent_management_decides_formation()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var bothAccepted = await BothAcceptedAsync(api);
        var candidate = DraftApi.CandidateOf(bothAccepted);

        foreach (var caller in new Profile?[] { Profile.Employee, Profile.Mentor, Profile.Applicant, Profile.Newcomer, null })
        {
            foreach (var outcome in new[] { "Approved", "Rejected" })
            {
                using var response = await TryDecideAsync(api, bothAccepted, caller, outcome);
                Assert.Equal(caller is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
            }
        }

        Assert.Equal((0L, 0L), (await DecisionCountAsync(api.Db, candidate), await PairingApi.RelationshipsAsync(api.Db, candidate)));
        Assert.Equal(bothAccepted.GetRawText(), (await DraftApi.ReadAsync(api, candidate, Profile.Mentor)).GetRawText());
    }

    // ADR-005: a decision without a usable If-Match is refused with 428, one on a stale version with 409, a malformed body
    // with 400 and a revision that is not the candidate's with 404; none of them persists anything.
    [Fact]
    public async Task A_decision_without_a_usable_version_or_body_persists_nothing()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var bothAccepted = await BothAcceptedAsync(api);
        var candidate = DraftApi.CandidateOf(bothAccepted);
        var revision = DraftApi.Current(bothAccepted);
        var hash = revision.Text("contentHash");
        var route = Decisions(candidate, revision.Id());
        var (entries, notifications) = (await PairingApi.AuditEntriesAsync(api.Db, candidate), await PairingApi.NotificationsAsync(api.Db, candidate));
        var other = DraftApi.Current(await BothAcceptedAsync(api));

        foreach (var unusable in new string?[] { null, "*", "W/\"7\"", "7" })
        {
            var missing = await ProblemAssert.IsProblem(HttpStatusCode.PreconditionRequired,
                await api.SendAsync(HttpMethod.Post, route, Profile.Administrator, new { outcome = "Approved", contentHash = hash }, unusable));
            Assert.Equal(MentorshipRequestEndpoints.VersionRequired, missing.Text("title"));
        }

        var stale = await ProblemAssert.IsProblem(HttpStatusCode.Conflict,
            await api.SendAsync(HttpMethod.Post, route, Profile.Administrator, new { outcome = "Approved", contentHash = hash }, "\"6\""));
        Assert.Equal(MentorshipRequestEndpoints.CandidateChanged, stale.Text("title"));

        foreach (var malformed in new object[]
        {
            new { contentHash = hash },
            new { outcome = "Approved" },
            new { outcome = "Approved", contentHash = "  " },
            new { outcome = 7, contentHash = hash },
            new { outcome = "Approved", contentHash = hash, isActive = "Y" },
            new { outcome = "Approved", contentHash = hash, state = "OnHold" },
        })
        {
            await ProblemAssert.IsProblem(HttpStatusCode.BadRequest, await api.SendAsync(HttpMethod.Post, route, Profile.Administrator, malformed, DraftApi.Tag(bothAccepted)));
        }

        foreach (var elsewhere in new[] { Guid.NewGuid(), other.Id() })
        {
            using var response = await TryDecideAsync(api, bothAccepted, Profile.Administrator, revision: elsewhere, contentHash: hash);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        Assert.Equal(bothAccepted.GetProperty("version").GetInt64(), await PairingApi.VersionAsync(api.Db, candidate));
        Assert.Equal((0L, 0L), (await DecisionCountAsync(api.Db, candidate), await PairingApi.RelationshipsAsync(api.Db, candidate)));
        Assert.Equal((entries, notifications), (await PairingApi.AuditEntriesAsync(api.Db, candidate), await PairingApi.NotificationsAsync(api.Db, candidate)));
    }

    // AC-E2E-11, AC-BR-021-01, AC-FR-021-02: a withdrawal after both acceptances leaves nothing to form: its acceptances read
    // Withdrawn and no decision can be taken. Substituting the mentor is a new pairing on the same request whose own
    // pairing decisions, acceptances and approval form the relationship with the new mentor; the old pairing never forms one.
    [Fact]
    public async Task A_withdrawn_pairing_forms_nothing_and_a_substituted_mentor_needs_every_step_anew()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var bothAccepted = await BothAcceptedAsync(api);
        var withdrawnCandidate = DraftApi.CandidateOf(bothAccepted);
        var head = await api.JsonAsync(HttpMethod.Get, PairingApi.Candidate(withdrawnCandidate), Profile.Employee);
        await PairingApi.DecideAsync(api, PairingApi.Withdraw(withdrawnCandidate), Profile.Employee, head);
        var withdrawn = await DraftApi.ReadAsync(api, withdrawnCandidate, Profile.Administrator);

        var refused = await ProblemAssert.IsProblem(HttpStatusCode.Conflict, await TryDecideAsync(api, withdrawn, Profile.Administrator));

        Assert.Equal(MentorshipRequestEndpoints.NotPossibleNow, refused.Text("title"));
        Assert.Equal([("Protege", 1, "Withdrawn"), ("Mentor", 1, "Withdrawn")], DraftApi.AcceptancesOf(withdrawn));
        var outcome = await ReadAsync(api, withdrawnCandidate, Profile.Employee);
        Assert.Equal(("Withdrawn", (int?)null, JsonValueKind.Null), (outcome.Text("candidateStatus"), outcome.OptionalInt("step"), Relationship(outcome).ValueKind));

        var newMentor = await PairingApi.ApproveApplicantAsync(api);
        var drafting = await DraftingWithAsync(api, await RequestOfAsync(api.Db, withdrawnCandidate), Profile.Applicant);
        var substituted = await BothAcceptAsync(api, await DraftApi.ReviseAsync(api, drafting, Profile.Employee), Profile.Applicant);

        var formed = await DecideAsync(api, substituted);

        Assert.Equal((api.Employee(Profile.Employee), newMentor),
            (Relationship(formed).GetProperty("protegeEmployeeId").GetGuid(), Relationship(formed).GetProperty("mentorEmployeeId").GetGuid()));
        Assert.Equal(newMentor, Relationship(formed).GetProperty("agreement").GetProperty("content").GetProperty("parties").GetProperty("mentor").GetProperty("employeeId").GetGuid());
        Assert.Equal((0L, 0L), (await DecisionCountAsync(api.Db, withdrawnCandidate), await PairingApi.RelationshipsAsync(api.Db, withdrawnCandidate)));
        Assert.Equal("WITHDRAWN", await PairingApi.StatusAsync(api.Db, withdrawnCandidate));
    }
}

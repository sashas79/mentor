using System.Net;
using System.Text.Json;
using Mentor.Api.Requests;
using Mentor.Tests.Integration.Persistence;
using Mentor.Tests.Integration.Requests;
using Mentor.Tests.Integration.Support;
using static Mentor.Tests.Integration.Agreements.DraftApi;

namespace Mentor.Tests.Integration.Agreements;

// Draft commands as candidate commands (ADR-005, ADR-003, COMP-AGREEMENT-DRAFTING): versioned by the candidate's ETag,
// open only to whom the matrix grants them, admitted only while the candidate is drafting, and never an edit of a
// revision or an acceptance.
public sealed class DraftCommandTests(PostgresServer server)
{
    // The draft is part of the candidate: it carries the candidate's version, and each command advances it.
    [Fact]
    public async Task Every_draft_response_carries_the_candidate_version_as_its_etag_and_every_command_advances_it()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var candidate = (await PairingApi.CandidateAsync(api, "Drafting")).Id();

        using var read = await api.SendAsync(HttpMethod.Get, Draft(candidate), Profile.Employee);
        var drafting = await JsonAsync(read);
        using var revisedResponse = await TryReviseAsync(api, drafting, Profile.Employee);
        var revised = await JsonAsync(revisedResponse);
        using var acceptedResponse = await TryAcceptAsync(api, revised, Profile.Mentor, Current(revised).Id(), Current(revised).Text("contentHash"));
        var accepted = await JsonAsync(acceptedResponse);
        using var annexedResponse = await TryChangeAnnexAsync(api, accepted, Profile.Administrator, "AdministrativeReference", "TM-2026-014");

        Assert.Equal((HttpStatusCode.OK, "\"4\"", false), (read.StatusCode, read.Headers.ETag!.Tag.ToString(), read.Headers.ETag.IsWeak));
        Assert.Equal((HttpStatusCode.Created, "\"5\""), (revisedResponse.StatusCode, revisedResponse.Headers.ETag!.Tag.ToString()));
        Assert.Equal(Revision(candidate, Current(revised).Id()), revisedResponse.Headers.Location!.PathAndQuery);
        Assert.Equal((HttpStatusCode.OK, "\"6\""), (acceptedResponse.StatusCode, acceptedResponse.Headers.ETag!.Tag.ToString()));
        Assert.Equal((HttpStatusCode.OK, "\"7\""), (annexedResponse.StatusCode, annexedResponse.Headers.ETag!.Tag.ToString()));
        var head = await api.JsonAsync(HttpMethod.Get, PairingApi.Candidate(candidate), Profile.Employee);
        Assert.Equal((7L, (Guid?)Current(revised).Id()), (head.GetProperty("version").GetInt64(), head.OptionalGuid("currentRevisionId")));
    }

    // ADR-005: a draft command without a usable If-Match is refused with 428, one on a version no longer current with 409,
    // and either way nothing is persisted: no revision, acceptance or permitted change, no audit entry, no notification.
    [Fact]
    public async Task A_draft_command_without_a_usable_version_or_on_a_stale_one_persists_nothing()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var draft = await ReviseAsync(api, await DraftingAsync(api), Profile.Employee);
        var candidate = CandidateOf(draft);
        var revision = Current(draft);
        var (entries, notifications) = (await PairingApi.AuditEntriesAsync(api.Db, candidate), await PairingApi.NotificationsAsync(api.Db, candidate));
        var commands = new (string Route, Profile Caller, object Body)[]
        {
            (Revisions(candidate), Profile.Mentor, Form("Lead two design reviews by June 2027")),
            (Acceptances(candidate, revision.Id()), Profile.Employee, new { contentHash = revision.Text("contentHash") }),
            (Annex(candidate), Profile.Administrator, new { basis = "AdministrativeReference", value = "TM-2026-014" }),
        };

        foreach (var (route, caller, body) in commands)
        {
            foreach (var unusable in new string?[] { null, "*", "W/\"5\"", "5" })
            {
                var missing = await ProblemAssert.IsProblem(HttpStatusCode.PreconditionRequired, await api.SendAsync(HttpMethod.Post, route, caller, body, unusable));
                Assert.Equal(MentorshipRequestEndpoints.VersionRequired, missing.Text("title"));
            }

            var stale = await ProblemAssert.IsProblem(HttpStatusCode.Conflict, await api.SendAsync(HttpMethod.Post, route, caller, body, "\"4\""));
            Assert.Equal(MentorshipRequestEndpoints.CandidateChanged, stale.Text("title"));
        }

        Assert.Equal(5L, await PairingApi.VersionAsync(api.Db, candidate));
        Assert.Equal((1L, 0L, 0L), (await RevisionCountAsync(api.Db, candidate), await AcceptanceCountAsync(api.Db, candidate), await PermittedChangeCountAsync(api.Db, candidate)));
        Assert.Equal((entries, notifications), (await PairingApi.AuditEntriesAsync(api.Db, candidate), await PairingApi.NotificationsAsync(api.Db, candidate)));
        Assert.Equal(draft.GetRawText(), (await ReadAsync(api, candidate, Profile.Employee)).GetRawText());
    }

    // ADR-005: a replacement and an acceptance taken at once on one version queue on the candidate: exactly one is taken,
    // so an acceptance can never land on a revision that a concurrent replacement has superseded.
    [Fact]
    public async Task A_replacement_and_an_acceptance_taken_at_once_end_with_exactly_one_taken()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var draft = await ReviseAsync(api, await DraftingAsync(api), Profile.Employee);
        var candidate = CandidateOf(draft);
        var revision = Current(draft);

        var responses = await Task.WhenAll(
            TryReviseAsync(api, draft, Profile.Mentor, Form("Lead two design reviews by June 2027")),
            TryAcceptAsync(api, draft, Profile.Employee, revision.Id(), revision.Text("contentHash")));

        Assert.Single(responses, response => response.IsSuccessStatusCode);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        var revisedFirst = responses[0].IsSuccessStatusCode;
        Assert.Equal((revisedFirst ? 2L : 1L, revisedFirst ? 0L : 1L), (await RevisionCountAsync(api.Db, candidate), await AcceptanceCountAsync(api.Db, candidate)));
        Assert.Equal(draft.GetProperty("version").GetInt64() + 1, await PairingApi.VersionAsync(api.Db, candidate));
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    // ADR-015, COMP-AGREEMENT-DRAFTING: before mutual willingness there is no draft to work on.
    [Fact]
    public async Task A_candidate_before_drafting_admits_no_draft_operation()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var invited = await PairingApi.CandidateAsync(api);
        var accepted = await PairingApi.CandidateAsync(api, "Accepted");

        foreach (var candidate in new[] { invited.Id(), accepted.Id() })
        {
            var draft = await ReadAsync(api, candidate, Profile.Employee);
            Assert.Null(Step(draft));
            var refused = await ProblemAssert.IsProblem(HttpStatusCode.Conflict, await TryReviseAsync(api, draft, Profile.Employee));
            Assert.Equal(MentorshipRequestEndpoints.NotPossibleNow, refused.Text("title"));
            using var annex = await TryChangeAnnexAsync(api, draft, Profile.Administrator, "AdministrativeReference", "TM-2026-014");
            Assert.Equal(HttpStatusCode.Conflict, annex.StatusCode);
            Assert.Equal((0L, 0L), (await RevisionCountAsync(api.Db, candidate), await PermittedChangeCountAsync(api.Db, candidate)));
        }
    }

    // AC-FR-021-02, ADR-004: a withdrawn candidate keeps its draft as evidence; its acceptances apply to nothing, and it
    // admits no further draft operation.
    [Fact]
    public async Task A_withdrawn_candidates_draft_stays_as_evidence_and_admits_no_further_operation()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var first = await ReviseAsync(api, await DraftingAsync(api), Profile.Employee);
        var candidate = CandidateOf(first);
        var revision = Current(first);
        var stored = await ContentAsync(api.Db, revision.Id());
        await AcceptAsync(api, first, Profile.Employee);
        var head = await api.JsonAsync(HttpMethod.Get, PairingApi.Candidate(candidate), Profile.Mentor);
        await PairingApi.DecideAsync(api, PairingApi.Withdraw(candidate), Profile.Mentor, head);

        var withdrawn = await ReadAsync(api, candidate, Profile.Employee);

        Assert.Equal(("Withdrawn", (int?)null), (withdrawn.Text("candidateStatus"), Step(withdrawn)));
        Assert.Equal([("Protege", 1, "Withdrawn")], AcceptancesOf(withdrawn));
        Assert.Equal(revision.Id(), Current(withdrawn).Id());
        await AdmitsNothingAsync(api, withdrawn);
        Assert.Equal(stored, await ContentAsync(api.Db, revision.Id()));
        Assert.Equal((1L, 1L, 0L), (await RevisionCountAsync(api.Db, candidate), await AcceptanceCountAsync(api.Db, candidate), await PermittedChangeCountAsync(api.Db, candidate)));
    }

    // AC-E2E-05, AC-NFR-005-01, AC-DR-006-01: once formed, the accepted agreement is fixed: no revision, acceptance or
    // annex change is admitted, and the revision and the formed agreement keep exactly the accepted content.
    [Fact]
    public async Task A_formed_candidates_agreement_admits_no_further_draft_operation()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var first = await ReviseAsync(api, await DraftingAsync(api), Profile.Employee);
        var candidate = CandidateOf(first);
        var revision = Current(first);
        var stored = await ContentAsync(api.Db, revision.Id());
        var bothAccepted = await AcceptAsync(api, await AcceptAsync(api, first, Profile.Employee), Profile.Mentor);
        var formedAgreement = await FormAsync(api, bothAccepted);

        var formed = await ReadAsync(api, candidate, Profile.Employee);

        Assert.Equal(("Formed", (int?)4), (formed.Text("candidateStatus"), Step(formed)));
        Assert.Equal([("Protege", 1, "Formed"), ("Mentor", 1, "Formed")], AcceptancesOf(formed));
        await AdmitsNothingAsync(api, formed);
        Assert.Equal(stored, await ContentAsync(api.Db, revision.Id()));
        Assert.Equal(stored, await api.Db.ScalarAsync<string>(Login.Owner, "SELECT content FROM mentor.formed_agreement WHERE id = $1", formedAgreement));
        Assert.Equal((1L, 2L), (await RevisionCountAsync(api.Db, candidate), await AcceptanceCountAsync(api.Db, candidate)));
    }

    // ADR-003: the two participants revise and accept their own draft; Talent Management reads it but neither revises nor
    // accepts, and nobody else does anything with it.
    [Fact]
    public async Task Only_the_participants_revise_and_accept_the_draft()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var draft = await ReviseAsync(api, await DraftingAsync(api), Profile.Employee);
        var candidate = CandidateOf(draft);
        var revision = Current(draft);

        foreach (var outsider in new Profile?[] { Profile.Administrator, Profile.Applicant, Profile.Newcomer, null })
        {
            var expected = outsider is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
            using var revised = await TryReviseAsync(api, draft, outsider, Form("Lead two design reviews by June 2027"));
            using var accepted = await TryAcceptAsync(api, draft, outsider, revision.Id(), revision.Text("contentHash"));
            Assert.Equal((expected, expected), (revised.StatusCode, accepted.StatusCode));
        }

        Assert.Equal((1L, 0L), (await RevisionCountAsync(api.Db, candidate), await AcceptanceCountAsync(api.Db, candidate)));
        Assert.Equal(draft.GetRawText(), (await ReadAsync(api, candidate, Profile.Employee)).GetRawText());
    }

    // AC-FR-029-01: no route edits or removes a revision or an acceptance, whoever asks; the accepted snapshot stays as it
    // was. (The database refuses such a statement too: Persistence/ImmutabilityTests.)
    [Fact]
    public async Task No_route_edits_or_removes_an_accepted_revision_or_its_acceptance()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var first = await ReviseAsync(api, await DraftingAsync(api), Profile.Employee);
        var candidate = CandidateOf(first);
        var revision = Current(first);
        var stored = await ContentAsync(api.Db, revision.Id());
        var accepted = await AcceptAsync(api, first, Profile.Employee);

        foreach (var method in new[] { HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete })
        {
            foreach (var caller in new[] { Profile.Employee, Profile.Mentor, Profile.Administrator })
            {
                using var revisionEdit = await api.SendAsync(method, Revision(candidate, revision.Id()), caller, Form("Edited in place"), Tag(accepted));
                using var acceptanceEdit = await api.SendAsync(method, Acceptances(candidate, revision.Id()), caller, new { contentHash = "edited" }, Tag(accepted));
                Assert.False(revisionEdit.IsSuccessStatusCode, $"{method} of a revision as {caller}: {revisionEdit.StatusCode}");
                Assert.False(acceptanceEdit.IsSuccessStatusCode, $"{method} of an acceptance as {caller}: {acceptanceEdit.StatusCode}");
            }
        }

        Assert.Equal(stored, await ContentAsync(api.Db, revision.Id()));
        Assert.Equal(1L, await AcceptanceCountAsync(api.Db, candidate));
        Assert.Equal(accepted.GetRawText(), (await ReadAsync(api, candidate, Profile.Employee)).GetRawText());
    }

    /// <summary>Every draft command on a candidate that has left DRAFTING is refused as not possible now, and changes nothing.</summary>
    private static async Task AdmitsNothingAsync(WorkflowApi api, JsonElement draft)
    {
        var revision = Current(draft);
        var refusals = new List<HttpResponseMessage>
        {
            await TryReviseAsync(api, draft, Profile.Employee, Form("Lead two design reviews by June 2027")),
            await TryAcceptAsync(api, draft, Profile.Mentor, revision.Id(), revision.Text("contentHash")),
            await TryChangeAnnexAsync(api, draft, Profile.Administrator, "AdministrativeReference", "TM-2026-014"),
        };

        foreach (var refusal in refusals)
        {
            var problem = await ProblemAssert.IsProblem(HttpStatusCode.Conflict, refusal);
            Assert.Equal(MentorshipRequestEndpoints.NotPossibleNow, problem.Text("title"));
            refusal.Dispose();
        }

        Assert.Equal(draft.GetRawText(), (await ReadAsync(api, CandidateOf(draft), Profile.Employee)).GetRawText());
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
}

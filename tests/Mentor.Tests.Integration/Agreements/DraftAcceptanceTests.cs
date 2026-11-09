using System.Net;
using System.Text.Json;
using Mentor.Api.Agreements;
using Mentor.Api.Hosting;
using Mentor.Api.Requests;
using Mentor.Core.Agreements;
using Mentor.Tests.Integration.Persistence;
using Mentor.Tests.Integration.Requests;
using Mentor.Tests.Integration.Support;
using static Mentor.Tests.Integration.Agreements.DraftApi;

namespace Mentor.Tests.Integration.Agreements;

// Participant acceptances of the current revision and replacement revisions through the API (ADR-004, FR-027 to
// FR-029, FR-067, FR-069, BR-004, BR-012, BR-019, DR-007): each acceptance is the participant's own, binds the exact
// content displayed, and stops applying, without being touched, once a replacement supersedes its revision.
public sealed class DraftAcceptanceTests(PostgresServer server)
{
    private const string ChangedGoal = "Lead two design reviews by June 2027";

    // AC-FR-027-01, AC-FR-027-02, AC-BR-012-01, AC-DR-007-01: each acceptance is recorded separately for the
    // participant who gave it, under the identity they signed in with; one participant's action, writing the revision
    // included, never stands for the other's acceptance.
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task Each_participant_accepts_the_current_revision_for_themselves(IdentityMode mode)
    {
        await using var api = await WorkflowApi.StartAsync(server, mode);
        var (protege, mentor) = (api.Employee(Profile.Employee), api.Employee(Profile.Mentor));
        var revised = await ReviseAsync(api, await DraftingAsync(api), Profile.Mentor);
        var candidate = CandidateOf(revised);
        var revision = Current(revised);
        var hash = revision.Text("contentHash");

        var protegeAccepted = await AcceptAsync(api, revised, Profile.Employee);

        Assert.Equal(2, Step(protegeAccepted));
        var first = Assert.Single(protegeAccepted.Items("acceptances"));
        Assert.Equal((protege, "Protege", revision.Id(), 1, hash, "Applicable"),
            (first.GetProperty("participantEmployeeId").GetGuid(), first.Text("participant"), first.GetProperty("revisionId").GetGuid(),
                first.GetProperty("revisionNumber").GetInt32(), first.Text("contentHash"), first.Text("applicability")));
        Assert.NotNull(first.OptionalTime("acceptedAt"));

        var bothAccepted = await AcceptAsync(api, protegeAccepted, Profile.Mentor);

        Assert.Equal(3, Step(bothAccepted));
        Assert.Equal([("Protege", 1, "Applicable"), ("Mentor", 1, "Applicable")], AcceptancesOf(bothAccepted));
        Assert.Equal([protege, mentor], Current(bothAccepted).Items("acceptances").Select(acceptance => acceptance.GetProperty("participantEmployeeId").GetGuid()));
        Assert.Equal(2L, await AcceptanceCountAsync(api.Db, candidate));
        Assert.Equal(1L, await api.Db.ScalarAsync<long>(Login.Owner, """
            SELECT count(DISTINCT acceptance.content_hash) FROM mentor.agreement_acceptance AS acceptance
            JOIN mentor.agreement_revision AS revision ON revision.id = acceptance.revision_id AND revision.content_hash = acceptance.content_hash
            WHERE revision.candidate_id = $1
            """, candidate));

        // The trail attributes each acceptance to its participant, as authenticated, with the exact content it accepted.
        var entries = (await api.JsonAsync(HttpMethod.Get, PairingApi.Evidence(candidate), Profile.Administrator)).Items("entries")
            .Where(entry => entry.Text("action") == "AcceptanceRecorded").ToList();
        Assert.Equal([protege, mentor], entries.Select(entry => entry.GetProperty("actor").GetProperty("employeeId").GetGuid()));
        Assert.Equal(bothAccepted.Items("acceptances").Select(JsonElementExtensions.Id), entries.Select(entry => entry.OptionalGuid("recordId")!.Value));
        Assert.Equal(bothAccepted.Items("acceptances").Select(acceptance => acceptance.OptionalTime("acceptedAt")), entries.Select(entry => entry.OptionalTime("occurredAt")));
        Assert.All(entries, entry =>
        {
            Assert.Equal(mode.ToString(), entry.GetProperty("actor").Text("identityScheme"));
            Assert.Equal(("AgreementAcceptance", "ACCEPTED", "Application"), (entry.Text("recordType"), entry.Text("outcome"), entry.Text("provenance")));
            Assert.Equal((candidate, revision.Id(), hash), (entry.OptionalGuid("candidateId")!.Value, entry.OptionalGuid("revisionId")!.Value, entry.Text("contentHash")));
        });
    }

    // AC-FR-029-03, AC-FR-029-04, AC-FR-027-04, AC-FR-067-01, AC-FR-067-02, AC-E2E-05, AC-NFR-005-01, AC-DR-006-01: a
    // substantive change after acceptance is a replacement revision; the draft returns to Step 1, the accepted revision
    // stays exactly as it was, and both participants accept the replacement anew.
    [Fact]
    public async Task A_replacement_after_acceptance_returns_the_draft_to_step_1_and_needs_both_acceptances_anew()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var first = await ReviseAsync(api, await DraftingAsync(api), Profile.Employee);
        var candidate = CandidateOf(first);
        var accepted = Current(first);
        var acceptedContent = await ContentAsync(api.Db, accepted.Id());
        var bothAccepted = await AcceptAsync(api, await AcceptAsync(api, first, Profile.Employee), Profile.Mentor);
        Assert.Equal(3, Step(bothAccepted));

        var replaced = await ReviseAsync(api, bothAccepted, Profile.Mentor, Form(ChangedGoal));

        var replacement = Current(replaced);
        Assert.Equal(1, Step(replaced));
        Assert.Equal((2, (Guid?)accepted.Id(), api.Employee(Profile.Mentor)),
            (replacement.GetProperty("revisionNumber").GetInt32(), replacement.OptionalGuid("parentRevisionId"), replacement.GetProperty("createdByEmployeeId").GetGuid()));
        Assert.NotEqual(accepted.Text("contentHash"), replacement.Text("contentHash"));
        Assert.Equal(ChangedGoal, replacement.GetProperty("content").Items("goals")[0].GetString());
        Assert.Empty(replacement.Items("acceptances"));
        Assert.Equal([("Protege", 1, "Superseded"), ("Mentor", 1, "Superseded")], AcceptancesOf(replaced));

        // The accepted revision is untouched: the same bytes and hash, still readable in full, its acceptances retained.
        Assert.Equal(acceptedContent, await ContentAsync(api.Db, accepted.Id()));
        var retained = await api.JsonAsync(HttpMethod.Get, Revision(candidate, accepted.Id()), Profile.Employee);
        Assert.Equal((false, accepted.Text("contentHash")), (retained.GetProperty("isCurrent").GetBoolean(), retained.Text("contentHash")));
        Assert.Equal(accepted.GetProperty("content").GetRawText(), retained.GetProperty("content").GetRawText());
        Assert.Equal(["Superseded", "Superseded"], retained.Items("acceptances").Select(acceptance => acceptance.Text("applicability")));

        var protegeAcceptedAnew = await AcceptAsync(api, replaced, Profile.Employee);
        Assert.Equal(2, Step(protegeAcceptedAnew));
        var bothAcceptedAnew = await AcceptAsync(api, protegeAcceptedAnew, Profile.Mentor);
        Assert.Equal(3, Step(bothAcceptedAnew));
        Assert.Equal(
            [("Protege", 1, "Superseded"), ("Mentor", 1, "Superseded"), ("Protege", 2, "Applicable"), ("Mentor", 2, "Applicable")],
            AcceptancesOf(bothAcceptedAnew));
    }

    // AC-FR-027-03, AC-FR-029-02, AC-BR-019-01: an acceptance of the superseded revision and one of the current revision
    // are not two acceptances of the same content, and the superseded revision cannot be accepted to make up the pair.
    [Fact]
    public async Task An_acceptance_of_superseded_content_never_counts_with_one_of_the_current_content()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var first = await ReviseAsync(api, await DraftingAsync(api), Profile.Employee);
        var candidate = CandidateOf(first);
        var superseded = Current(first);
        var replaced = await ReviseAsync(api, await AcceptAsync(api, first, Profile.Employee), Profile.Mentor, Form(ChangedGoal));

        var mentorAccepted = await AcceptAsync(api, replaced, Profile.Mentor);

        Assert.Equal(2, Step(mentorAccepted));
        Assert.Equal([("Protege", 1, "Superseded"), ("Mentor", 2, "Applicable")], AcceptancesOf(mentorAccepted));
        var refused = await ProblemAssert.IsProblem(HttpStatusCode.Conflict,
            await TryAcceptAsync(api, mentorAccepted, Profile.Employee, superseded.Id(), superseded.Text("contentHash")));
        Assert.Equal(AgreementDraftEndpoints.RevisionSuperseded, refused.Text("title"));
        Assert.Equal(2L, await AcceptanceCountAsync(api.Db, candidate));
        Assert.Equal(mentorAccepted.GetRawText(), (await ReadAsync(api, candidate, Profile.Employee)).GetRawText());
    }

    // AC-FR-067-01, AC-FR-067-03, AC-FR-069-01, AC-FR-028-01, AC-FR-028-02: the replacement is audited with its actor, its
    // time, the outcome RESET_TO_STEP_1 and every acceptance and draft decision it made inapplicable; that evidence stays
    // as it was, now superseded, and none of it applies to the replacement.
    [Fact]
    public async Task A_replacement_is_audited_with_the_acceptances_and_decisions_it_made_inapplicable()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var (protege, mentor) = (api.Employee(Profile.Employee), api.Employee(Profile.Mentor));
        var first = await ReviseAsync(api, await DraftingAsync(api), Profile.Employee);
        var candidate = CandidateOf(first);
        var superseded = Current(first);
        var bothAccepted = await AcceptAsync(api, await AcceptAsync(api, first, Profile.Employee), Profile.Mentor);
        // Talent Management rejected formation of this revision (COMP-FORMATION records the decision; it is written directly here).
        var rejection = await new Seed(api.Db).FormationDecisionAsync(candidate,
            new Revision(superseded.Id(), await ContentAsync(api.Db, superseded.Id()), superseded.Text("contentHash")), api.Employee(Profile.Administrator), "REJECTED");

        var replaced = await ReviseAsync(api, bothAccepted, Profile.Mentor, Form(ChangedGoal));

        var replacement = Current(replaced);
        var evidence = await api.JsonAsync(HttpMethod.Get, PairingApi.Evidence(candidate), Profile.Administrator);
        var revisions = evidence.Items("entries").Where(entry => entry.Text("action") == "RevisionCreated").ToList();
        Assert.Equal(2, revisions.Count);
        // The first revision replaced nothing and reset nothing.
        Assert.Equal((protege, superseded.Id(), superseded.Text("contentHash")),
            (revisions[0].GetProperty("actor").GetProperty("employeeId").GetGuid(), revisions[0].OptionalGuid("revisionId")!.Value, revisions[0].Text("contentHash")));
        Assert.All(new[] { "outcome", "invalidatedAcceptanceIds", "invalidatedDecisionIds" },
            field => Assert.Equal(JsonValueKind.Null, revisions[0].GetProperty(field).ValueKind));
        // The replacement names who, when, the revision it made current and what stopped applying.
        var reset = revisions[1];
        Assert.Equal(("RESET_TO_STEP_1", mentor, replacement.Id(), replacement.Text("contentHash"), "AgreementRevision"),
            (reset.Text("outcome"), reset.GetProperty("actor").GetProperty("employeeId").GetGuid(), reset.OptionalGuid("revisionId")!.Value,
                reset.Text("contentHash"), reset.Text("recordType")));
        Assert.Equal(replacement.OptionalTime("createdAt"), reset.OptionalTime("occurredAt"));
        Assert.Equal(bothAccepted.Items("acceptances").Select(JsonElementExtensions.Id), reset.GetProperty("invalidatedAcceptanceIds").EnumerateArray().Select(id => id.GetGuid()));
        Assert.Equal([rejection], reset.GetProperty("invalidatedDecisionIds").EnumerateArray().Select(id => id.GetGuid()));
        Assert.Equal(superseded.Id(), reset.GetProperty("priorValues").GetProperty("currentRevisionId").GetGuid());
        Assert.Equal((replacement.Id(), 2),
            (reset.GetProperty("newValues").GetProperty("currentRevisionId").GetGuid(), reset.GetProperty("newValues").GetProperty("revisionNumber").GetInt32()));

        // The evidence itself is unchanged and inapplicable; it is told apart from what applies now.
        Assert.Equal(["Superseded", "Superseded"], evidence.Items("acceptances").Select(acceptance => acceptance.Text("applicability")));
        var decision = Assert.Single(evidence.Items("decisions"));
        Assert.Equal((rejection, "Rejected", "Superseded"), (decision.Id(), decision.Text("outcome"), decision.Text("applicability")));
        var draft = await ReadAsync(api, candidate, Profile.Administrator);
        Assert.Equal(1, Step(draft));
        Assert.Equal([protege, mentor], draft.Items("acceptances").Select(acceptance => acceptance.GetProperty("participantEmployeeId").GetGuid()));
        Assert.All(draft.Items("acceptances"), acceptance =>
        {
            Assert.Equal((superseded.Id(), superseded.Text("contentHash"), "Superseded"),
                (acceptance.GetProperty("revisionId").GetGuid(), acceptance.Text("contentHash"), acceptance.Text("applicability")));
            Assert.NotNull(acceptance.OptionalTime("acceptedAt"));
        });
    }

    // FR-067: a replacement presupposes a change. Submitting exactly the current content, whoever submits it and however
    // its text is spaced, replaces nothing, and the acceptances of the current revision keep applying.
    [Fact]
    public async Task Submitting_exactly_the_current_content_replaces_nothing()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var first = await ReviseAsync(api, await DraftingAsync(api), Profile.Employee);
        var candidate = CandidateOf(first);
        var accepted = await AcceptAsync(api, first, Profile.Employee);
        var respaced = Form("  Lead a design review by June 2027\r\n");

        foreach (var (author, form) in new[] { (Profile.Mentor, Form()), (Profile.Employee, respaced) })
        {
            var unchanged = await ProblemAssert.IsProblem(HttpStatusCode.Conflict, await TryReviseAsync(api, accepted, author, form));
            Assert.Equal(AgreementDraftEndpoints.ContentUnchanged, unchanged.Text("title"));
        }

        Assert.Equal(1L, await RevisionCountAsync(api.Db, candidate));
        var read = await ReadAsync(api, candidate, Profile.Employee);
        Assert.Equal(accepted.GetRawText(), read.GetRawText());
        Assert.Equal([("Protege", 1, "Applicable")], AcceptancesOf(read));
    }

    // ADR-004: an acceptance names the current revision of its own candidate and the hash of exactly the content
    // displayed, once per participant; anything else is refused and nothing is written.
    [Fact]
    public async Task An_acceptance_names_exactly_the_current_revision_and_its_hash_once_per_participant()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var draft = await ReviseAsync(api, await DraftingAsync(api), Profile.Employee);
        var candidate = CandidateOf(draft);
        var revision = Current(draft);
        var other = Current(await ReviseAsync(api, await DraftingAsync(api), Profile.Employee));

        var mismatch = await ProblemAssert.IsProblem(HttpStatusCode.UnprocessableEntity,
            await TryAcceptAsync(api, draft, Profile.Employee, revision.Id(), AgreementContentHash.Compute("{}")));
        Assert.Equal(AgreementDraftEndpoints.ContentMismatch, mismatch.Text("title"));
        Assert.Equal(HttpStatusCode.NotFound, (await TryAcceptAsync(api, draft, Profile.Employee, other.Id(), other.Text("contentHash"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await TryAcceptAsync(api, draft, Profile.Employee, Guid.NewGuid(), revision.Text("contentHash"))).StatusCode);
        await ProblemAssert.IsProblem(HttpStatusCode.BadRequest,
            await api.SendAsync(HttpMethod.Post, Acceptances(candidate, revision.Id()), Profile.Employee, new { }, Tag(draft)));
        Assert.Equal(0L, await AcceptanceCountAsync(api.Db, candidate));
        Assert.Equal(draft.GetProperty("version").GetInt64(), await PairingApi.VersionAsync(api.Db, candidate));

        var accepted = await AcceptAsync(api, draft, Profile.Employee);

        var again = await ProblemAssert.IsProblem(HttpStatusCode.Conflict,
            await TryAcceptAsync(api, accepted, Profile.Employee, revision.Id(), revision.Text("contentHash")));
        Assert.Equal(MentorshipRequestEndpoints.NotPossibleNow, again.Text("title"));
        Assert.Equal(1L, await AcceptanceCountAsync(api.Db, candidate));
    }

    // ADR-009, AC-BR-017-01: a revision asks the other participant to review and accept it, an acceptance asks the other
    // participant for theirs or, once both have accepted, only informs; nothing else is raised, no reminder included,
    // and Talent Management's pending work is a query, not an inbox entry.
    [Fact]
    public async Task Each_revision_and_acceptance_asks_the_other_participant_for_their_next_action()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var first = await ReviseAsync(api, await DraftingAsync(api), Profile.Employee);
        var candidate = CandidateOf(first);
        var bothAccepted = await AcceptAsync(api, await AcceptAsync(api, first, Profile.Employee), Profile.Mentor);
        await ReviseAsync(api, bothAccepted, Profile.Mentor, Form(ChangedGoal));

        Assert.Equal(
            [
                ("AcceptanceRecorded", candidate, "AcceptCurrentRevision"), ("RevisionCreated", candidate, "AcceptCurrentRevision"),
                ("WillingnessRecorded", candidate, "RecordWillingness"), ("InvitationSent", candidate, "RespondToInvitation"),
            ],
            await PairingApi.InboxAsync(api, Profile.Mentor));
        Assert.Equal(
            [
                ("RevisionCreated", candidate, "AcceptCurrentRevision"), ("AcceptanceRecorded", candidate, "None"),
                ("WillingnessRecorded", candidate, "PrepareAgreementDraft"), ("MentorResponded", candidate, "RecordWillingness"),
            ],
            await PairingApi.InboxAsync(api, Profile.Employee));
        Assert.Empty(await PairingApi.InboxAsync(api, Profile.Administrator));
    }
}

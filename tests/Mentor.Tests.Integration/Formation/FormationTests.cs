using System.Net;
using System.Text.Json;
using Mentor.Api.Formation;
using Mentor.Api.Hosting;
using Mentor.Api.Requests;
using Mentor.Tests.Integration.Agreements;
using Mentor.Tests.Integration.Persistence;
using Mentor.Tests.Integration.Requests;
using Mentor.Tests.Integration.Support;
using Npgsql;
using static Mentor.Tests.Integration.Formation.FormationApi;

namespace Mentor.Tests.Integration.Formation;

// Formation through the API (COMP-FORMATION, ADR-005): Talent Management approves the current revision both participants
// accepted, and one transaction records the decision and forms the relationship, its formed agreement and its goals
// (FR-030 to FR-033, FR-071, BR-003, DR-005, DR-008); the participants and Talent Management read the outcome.
public sealed class FormationTests(PostgresServer server)
{
    // AC-E2E-01, AC-FR-030-02, AC-FR-031-02, AC-DR-005-01, AC-DR-008-01, AC-FR-071-01, AC-FR-071-02, AC-BR-012-01: the
    // approval names the administrator, the candidate, the revision and its hash at the server's time; the relationship is
    // In Progress, blank and N with nobody supplying a value; the formed agreement is the accepted content byte for byte.
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task Talent_management_approves_the_accepted_current_revision_and_the_relationship_is_formed(IdentityMode mode)
    {
        await using var api = await WorkflowApi.StartAsync(server, mode);
        var (protege, mentor, administrator) = (api.Employee(Profile.Employee), api.Employee(Profile.Mentor), api.Employee(Profile.Administrator));
        var drafting = await DraftApi.DraftingAsync(api);
        var candidate = DraftApi.CandidateOf(drafting);
        var request = await RequestOfAsync(api.Db, candidate);
        var annexed = await api.JsonAsync(HttpMethod.Post, DraftApi.Annex(candidate), Profile.Administrator,
            new { basis = "AdministrativeReference", value = "TM-2026-014" }, ifMatch: DraftApi.Tag(drafting));
        var bothAccepted = await BothAcceptAsync(api, await DraftApi.ReviseAsync(api, annexed, Profile.Employee), Profile.Mentor);
        var revision = DraftApi.Current(bothAccepted);
        var accepted = await DraftApi.ContentAsync(api.Db, revision.Id());
        // Two acceptances alone form nothing (AC-FR-030-01).
        Assert.Equal(0L, await PairingApi.RelationshipsAsync(api.Db, candidate));

        var formed = await DecideAsync(api, bothAccepted);

        // The approval: Talent Management's, of the current revision and the exact content reviewed.
        Assert.Equal(("Formed", (int?)4, (Guid?)revision.Id()), (formed.Text("candidateStatus"), formed.OptionalInt("step"), formed.OptionalGuid("currentRevisionId")));
        var decision = Assert.Single(formed.Items("decisions"));
        Assert.Equal(("Approved", administrator, revision.Id(), 1, revision.Text("contentHash"), "Formed"),
            (decision.Text("outcome"), decision.GetProperty("administratorEmployeeId").GetGuid(), decision.GetProperty("revisionId").GetGuid(),
                decision.GetProperty("revisionNumber").GetInt32(), decision.Text("contentHash"), decision.Text("applicability")));
        var decidedAt = decision.OptionalTime("decidedAt");
        Assert.NotNull(decidedAt);

        // The relationship: digital, between the candidate's participants, In Progress, blank and N, with the accepted duration and dates.
        var relationship = Relationship(formed);
        Assert.Equal(("Digital", protege, mentor),
            (relationship.Text("provenance"), relationship.GetProperty("protegeEmployeeId").GetGuid(), relationship.GetProperty("mentorEmployeeId").GetGuid()));
        Assert.Equal(("InProgress", "Blank", "N"), (relationship.Text("state"), relationship.Text("status"), relationship.Text("isActive")));
        Assert.Equal(("TwelveMonths", "2026-12-01", "2027-11-30"),
            (relationship.Text("durationCategory"), relationship.Text("startDate"), relationship.Text("endDate")));
        Assert.Equal(decidedAt, relationship.OptionalTime("formedAt"));

        // The formed agreement: exactly the accepted bytes and hash, with the annex retained beside it.
        var agreement = relationship.GetProperty("agreement");
        Assert.Equal((revision.Id(), revision.Text("contentHash")), (agreement.GetProperty("revisionId").GetGuid(), agreement.Text("contentHash")));
        Assert.Equal(revision.GetProperty("content").GetRawText(), agreement.GetProperty("content").GetRawText());
        Assert.Equal(accepted, await api.Db.ScalarAsync<string>(Login.Owner, "SELECT content FROM mentor.formed_agreement WHERE id = $1", agreement.Id()));
        Assert.Equal(("TM-2026-014", JsonValueKind.Null),
            (agreement.GetProperty("annex").Text("adminReference"), agreement.GetProperty("annex").GetProperty("adminRemarks").ValueKind));

        // The goals: one per agreed goal, worded as agreed, not yet complete.
        Assert.Equal([(1, "Lead a design review by June 2027", "Incomplete"), (2, "Present at the architecture forum", "Incomplete")],
            relationship.Items("goals").Select(goal => (goal.GetProperty("position").GetInt32(), goal.Text("agreedDescription"), goal.Text("completionStatus"))));

        // The candidate and its request are FORMED, and the acceptances are now the record of the formation.
        Assert.Equal(("FORMED", "FORMED"), (await PairingApi.StatusAsync(api.Db, candidate), await DispositionAsync(api.Db, request)));
        Assert.Equal([("Protege", 1, "Formed"), ("Mentor", 1, "Formed")], DraftApi.AcceptancesOf(await DraftApi.ReadAsync(api, candidate, Profile.Mentor)));

        // The trail: one entry for each record formation wrote or changed, by the administrator under their scheme, at the decision's time.
        var entries = (await api.JsonAsync(HttpMethod.Get, PairingApi.Evidence(candidate), Profile.Administrator)).Items("entries")
            .Where(entry => entry.Text("action") == "FormationDecided")
            .ToDictionary(entry => entry.Text("recordType"));
        Assert.Equal(["Candidate", "FormationDecision", "MentorshipRequest", "Relationship"], entries.Keys.Order(StringComparer.Ordinal));
        Assert.Equal((decision.Id(), candidate, relationship.Id(), request),
            (entries["FormationDecision"].OptionalGuid("recordId")!.Value, entries["Candidate"].OptionalGuid("recordId")!.Value,
                entries["Relationship"].OptionalGuid("recordId")!.Value, entries["MentorshipRequest"].OptionalGuid("recordId")!.Value));
        Assert.All(entries.Values, entry =>
        {
            Assert.Equal((administrator, mode.ToString(), "APPROVED", decidedAt),
                (entry.GetProperty("actor").GetProperty("employeeId").GetGuid(), entry.GetProperty("actor").Text("identityScheme"), entry.Text("outcome"),
                    entry.OptionalTime("occurredAt")));
            Assert.Equal((candidate, revision.Id(), revision.Text("contentHash")),
                (entry.OptionalGuid("candidateId")!.Value, entry.OptionalGuid("revisionId")!.Value, entry.Text("contentHash")));
        });
        Assert.Equal("APPROVED", entries["FormationDecision"].GetProperty("newValues").Text("outcome"));
        Assert.Equal(("DRAFTING", "FORMED"),
            (entries["Candidate"].GetProperty("priorValues").Text("status"), entries["Candidate"].GetProperty("newValues").Text("status")));
        Assert.Equal(("OPEN", "FORMED"),
            (entries["MentorshipRequest"].GetProperty("priorValues").Text("disposition"), entries["MentorshipRequest"].GetProperty("newValues").Text("disposition")));
        var created = entries["Relationship"].GetProperty("newValues");
        Assert.Equal(("DIGITAL", "IN_PROGRESS", "BLANK", "N", "TWELVE_MONTHS"),
            (created.Text("provenance"), created.Text("state"), created.Text("status"), created.Text("isActive"), created.Text("durationCategory")));
        Assert.Equal((agreement.Id(), 2), (created.GetProperty("formedAgreementId").GetGuid(), created.GetProperty("goalCount").GetInt32()));

        // Both participants are told to arrange their first session; Talent Management's pending work is a query, not an inbox entry.
        Assert.Equal(("FormationDecided", candidate, "ArrangeFirstSession"), (await PairingApi.InboxAsync(api, Profile.Employee))[0]);
        Assert.Equal(("FormationDecided", candidate, "ArrangeFirstSession"), (await PairingApi.InboxAsync(api, Profile.Mentor))[0]);
        Assert.Empty(await PairingApi.InboxAsync(api, Profile.Administrator));
    }

    // ADR-005, AC-FR-070-02: a rejection is evidence only. It forms nothing and leaves the participants free to revise;
    // a replacement makes it inapplicable and needs both acceptances and an approval of its own.
    [Fact]
    public async Task A_rejection_forms_nothing_and_a_replacement_needs_its_own_acceptances_and_approval()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var bothAccepted = await BothAcceptedAsync(api);
        var candidate = DraftApi.CandidateOf(bothAccepted);

        var rejected = await DecideAsync(api, bothAccepted, "Rejected");

        Assert.Equal(("Drafting", (int?)3), (rejected.Text("candidateStatus"), rejected.OptionalInt("step")));
        Assert.Equal([("Rejected", 1, "Applicable")], DecisionsOf(rejected));
        Assert.Equal(JsonValueKind.Null, Relationship(rejected).ValueKind);
        Assert.Equal((0L, 0L, 0L),
            (await PairingApi.RelationshipsAsync(api.Db, candidate), await FormedAgreementCountAsync(api.Db, candidate), await GoalCountAsync(api.Db, candidate)));
        Assert.Equal("DRAFTING", await PairingApi.StatusAsync(api.Db, candidate));
        var rejection = Assert.Single((await api.JsonAsync(HttpMethod.Get, PairingApi.Evidence(candidate), Profile.Administrator)).Items("entries"),
            entry => entry.Text("action") == "FormationDecided");
        Assert.Equal(("FormationDecision", "REJECTED"), (rejection.Text("recordType"), rejection.Text("outcome")));
        Assert.Equal(("FormationDecided", candidate, "ReviseDraft"), (await PairingApi.InboxAsync(api, Profile.Employee))[0]);
        Assert.Equal(("FormationDecided", candidate, "ReviseDraft"), (await PairingApi.InboxAsync(api, Profile.Mentor))[0]);

        var replaced = await DraftApi.ReviseAsync(api, await DraftApi.ReadAsync(api, candidate, Profile.Mentor), Profile.Mentor,
            DraftApi.Form("Lead two design reviews by June 2027"));

        Assert.Equal([("Rejected", 1, "Superseded")], DecisionsOf(await ReadAsync(api, candidate, Profile.Employee)));
        var notYet = await ProblemAssert.IsProblem(HttpStatusCode.Conflict, await TryDecideAsync(api, replaced, Profile.Administrator));
        Assert.Equal(FormationEndpoints.NotAccepted, notYet.Text("title"));

        var formed = await DecideAsync(api, await BothAcceptAsync(api, replaced, Profile.Mentor));

        Assert.Equal([("Rejected", 1, "Superseded"), ("Approved", 2, "Formed")], DecisionsOf(formed));
        Assert.Equal(["Lead two design reviews by June 2027", "Present at the architecture forum"], GoalsOf(formed));
    }

    // ADR-005: a later approval of the revision Talent Management rejected passes every check anew; the rejection stays.
    [Fact]
    public async Task A_rejected_revision_may_still_be_approved_while_it_is_current_and_accepted()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var bothAccepted = await BothAcceptedAsync(api);
        var candidate = DraftApi.CandidateOf(bothAccepted);
        await DecideAsync(api, bothAccepted, "Rejected");

        var formed = await DecideAsync(api, await DraftApi.ReadAsync(api, candidate, Profile.Administrator));

        Assert.Equal([("Rejected", 1, "Formed"), ("Approved", 1, "Formed")], DecisionsOf(formed));
        Assert.Equal((1L, 2L), (await PairingApi.RelationshipsAsync(api.Db, candidate), await DecisionCountAsync(api.Db, candidate)));
    }

    // ADR-003: the parties read the outcome and Talent Management reads it too; nobody else does. The outcome carries the
    // candidate's version as its ETag.
    [Fact]
    public async Task The_outcome_is_read_by_the_participants_and_talent_management_only()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var bothAccepted = await BothAcceptedAsync(api);
        var candidate = DraftApi.CandidateOf(bothAccepted);

        var before = await ReadAsync(api, candidate, Profile.Mentor);

        Assert.Equal(("Drafting", (int?)3, JsonValueKind.Null), (before.Text("candidateStatus"), before.OptionalInt("step"), Relationship(before).ValueKind));
        Assert.Empty(before.Items("decisions"));

        var formed = await DecideAsync(api, bothAccepted);

        foreach (var reader in new[] { Profile.Employee, Profile.Mentor, Profile.Administrator })
        {
            using var response = await api.SendAsync(HttpMethod.Get, Outcome(candidate), reader);
            Assert.Equal((HttpStatusCode.OK, $"\"{formed.GetProperty("version").GetInt64()}\""), (response.StatusCode, response.Headers.ETag!.Tag.ToString()));
            Assert.Equal(formed.GetRawText(), (await ReadAsync(api, candidate, reader)).GetRawText());
        }

        foreach (var outsider in new[] { Profile.Applicant, Profile.Newcomer })
        {
            Assert.Equal(HttpStatusCode.Forbidden, await api.StatusAsync(HttpMethod.Get, Outcome(candidate), outsider));
        }

        Assert.Equal(HttpStatusCode.Unauthorized, await api.StatusAsync(HttpMethod.Get, Outcome(candidate), profile: null));
        Assert.Equal(HttpStatusCode.NotFound, await api.StatusAsync(HttpMethod.Get, Outcome(Guid.NewGuid()), Profile.Administrator));
    }

    // AC-FR-032-02, AC-FR-032-03, AC-E2E-05, AC-E2E-11: after formation the agreement cannot change and its request pairs no
    // other mentor; different terms take a new request, candidate, agreement, acceptances and approval, which form a second
    // relationship with goals of its own, and nothing transfers from the first.
    [Fact]
    public async Task After_formation_different_terms_need_a_new_request_and_nothing_transfers()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var first = await DecideAsync(api, await BothAcceptedAsync(api));
        var firstCandidate = first.GetProperty("candidateId").GetGuid();
        var firstRelationship = Relationship(first).Id();
        var formedDraft = await DraftApi.ReadAsync(api, firstCandidate, Profile.Employee);

        using var revised = await DraftApi.TryReviseAsync(api, formedDraft, Profile.Employee, DraftApi.Form("Mentor a new joiner"));
        Assert.Equal(HttpStatusCode.Conflict, revised.StatusCode);
        var again = await ProblemAssert.IsProblem(HttpStatusCode.Conflict, await TryDecideAsync(api, formedDraft, Profile.Administrator));
        Assert.Equal(MentorshipRequestEndpoints.NotPossibleNow, again.Text("title"));
        var closed = await ProblemAssert.IsProblem(HttpStatusCode.Conflict, await api.SendAsync(HttpMethod.Post,
            PairingApi.RequestCandidates(await RequestOfAsync(api.Db, firstCandidate)), Profile.Employee, new { mentorEmployeeId = await PairingApi.ApproveApplicantAsync(api) }));
        Assert.Equal(MentorshipRequestEndpoints.RequestClosed, closed.Text("title"));

        var second = await DecideAsync(api, await BothAcceptedAsync(api, DraftApi.Form("Mentor a new joiner")));

        Assert.NotEqual(firstCandidate, second.GetProperty("candidateId").GetGuid());
        Assert.NotEqual(firstRelationship, Relationship(second).Id());
        Assert.Equal(["Mentor a new joiner", "Present at the architecture forum"], GoalsOf(second));
        Assert.Equal(first.GetRawText(), (await ReadAsync(api, firstCandidate, Profile.Employee)).GetRawText());
        Assert.Equal(2L, await api.Db.ScalarAsync<long>(Login.Owner, "SELECT count(*) FROM mentor.development_goal WHERE relationship_id = $1", firstRelationship));
    }

    // AC-FR-032-01, AC-FR-033-01, AC-FR-071-03: formation evidence is independent of the relationship's administrative
    // attributes. A later explicit choice of State or IsActive (the edit is COMP-RELATIONSHIP-ADMIN's; written here as the
    // application's account may write it) stands, the default N is never applied again, and the decision, the formed
    // agreement and the goals' agreed wording stay exactly as formed; the database refuses to change them.
    [Fact]
    public async Task Formation_evidence_stays_as_formed_whatever_later_changes_the_relationship()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var formed = await DecideAsync(api, await BothAcceptedAsync(api));
        var candidate = formed.GetProperty("candidateId").GetGuid();
        var relationship = Relationship(formed);
        var agreement = relationship.GetProperty("agreement").Id();
        var decision = Assert.Single(formed.Items("decisions")).Id();

        await api.Db.ExecuteAsync(Login.App,
            "UPDATE mentor.mentorship_relationship SET state = 'ON_HOLD', is_active = 'Y', version = version + 1 WHERE id = $1", relationship.Id());
        await DecideAsync(api, await BothAcceptedAsync(api));

        var after = await ReadAsync(api, candidate, Profile.Administrator);
        var changed = Relationship(after);
        Assert.Equal(("OnHold", "Blank", "Y"), (changed.Text("state"), changed.Text("status"), changed.Text("isActive")));
        Assert.Equal(relationship.GetProperty("agreement").GetRawText(), changed.GetProperty("agreement").GetRawText());
        Assert.Equal(relationship.GetProperty("goals").GetRawText(), changed.GetProperty("goals").GetRawText());
        Assert.Equal(formed.GetProperty("decisions").GetRawText(), after.GetProperty("decisions").GetRawText());

        await Rejected.WithAsync(PostgresErrorCodes.InsufficientPrivilege,
            () => api.Db.ExecuteAsync(Login.App, "UPDATE mentor.formed_agreement SET admin_reference = 'Changed' WHERE id = $1", agreement));
        await Rejected.WithAsync(PostgresErrorCodes.InsufficientPrivilege,
            () => api.Db.ExecuteAsync(Login.App, "UPDATE mentor.formation_decision SET outcome = 'REJECTED' WHERE id = $1", decision));
        await Rejected.WithAsync(PostgresErrorCodes.InsufficientPrivilege,
            () => api.Db.ExecuteAsync(Login.App, "UPDATE mentor.development_goal SET agreed_description = 'Something else' WHERE relationship_id = $1", relationship.Id()));
        await Rejected.WithAsync(PostgresErrorCodes.RestrictViolation,
            () => api.Db.ExecuteAsync(Login.Owner, "UPDATE mentor.formed_agreement SET content_hash = content_hash WHERE id = $1", agreement));
        await Rejected.WithAsync(PostgresErrorCodes.RestrictViolation,
            () => api.Db.ExecuteAsync(Login.Owner, "DELETE FROM mentor.formation_decision WHERE id = $1", decision));
        Assert.Equal(relationship.GetProperty("agreement").GetRawText(), Relationship(await ReadAsync(api, candidate, Profile.Employee)).GetProperty("agreement").GetRawText());
    }
}

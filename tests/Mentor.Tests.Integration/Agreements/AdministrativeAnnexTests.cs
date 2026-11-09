using System.Net;
using System.Text;
using System.Text.Json;
using Mentor.Api.Agreements;
using Mentor.Api.Hosting;
using Mentor.Tests.Integration.Persistence;
using Mentor.Tests.Integration.Requests;
using Mentor.Tests.Integration.Support;
using static Mentor.Tests.Integration.Agreements.DraftApi;

namespace Mentor.Tests.Integration.Agreements;

// The administrative annex of a draft (ADR-004, FR-068, BR-020, BR-023, DR-015): outside the accepted content, changed
// only by a Talent Management administrator under a permission basis, recorded and audited, and the one change that
// resets nothing.
public sealed class AdministrativeAnnexTests(PostgresServer server)
{
    // AC-FR-068-01, AC-E2E-09, AC-BR-023-01, AC-BR-020-01, AC-DR-015-01: after both acceptances an administrator records a
    // reference and a remark; the accepted revision, its hash and both acceptances are untouched and still applicable, and
    // each change is recorded with its basis, actor, time and before and after values, apart from the content. A
    // participant's change to the content is the other kind of event: a replacement that resets the draft.
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task Talent_management_changes_the_annex_after_both_acceptances_without_resetting_the_draft(IdentityMode mode)
    {
        await using var api = await WorkflowApi.StartAsync(server, mode);
        var administrator = api.Employee(Profile.Administrator);
        var first = await ReviseAsync(api, await DraftingAsync(api), Profile.Employee);
        var candidate = CandidateOf(first);
        var revision = Current(first);
        var stored = await ContentAsync(api.Db, revision.Id());
        var bothAccepted = await AcceptAsync(api, await AcceptAsync(api, first, Profile.Employee), Profile.Mentor);

        var referenced = await api.JsonAsync(HttpMethod.Post, Annex(candidate), Profile.Administrator,
            new { basis = "AdministrativeReference", value = "TM-2026-014" }, ifMatch: Tag(bothAccepted));
        var remarked = await api.JsonAsync(HttpMethod.Post, Annex(candidate), Profile.Administrator,
            new { basis = "AdministrativeRemark", value = "Filed with the register." }, ifMatch: Tag(referenced));

        Assert.Equal(3, Step(remarked));
        Assert.Equal((revision.Id(), revision.Text("contentHash")), (Current(remarked).Id(), Current(remarked).Text("contentHash")));
        Assert.Equal(revision.GetProperty("content").GetRawText(), Current(remarked).GetProperty("content").GetRawText());
        Assert.Equal([("Protege", 1, "Applicable"), ("Mentor", 1, "Applicable")], AcceptancesOf(remarked));
        Assert.Equal(stored, await ContentAsync(api.Db, revision.Id()));
        Assert.Equal(1L, await RevisionCountAsync(api.Db, candidate));

        var annex = remarked.GetProperty("annex");
        Assert.Equal(("TM-2026-014", "Filed with the register.", AdministrativeAnnexView.Label),
            (annex.Text("adminReference"), annex.Text("adminRemarks"), annex.Text("notice")));
        var changes = annex.Items("changes");
        Assert.Equal(
            new (string, string, string?, string?)[] { ("AdministrativeReference", "AdminReference", null, "TM-2026-014"), ("AdministrativeRemark", "AdminRemarks", null, "Filed with the register.") },
            changes.Select(change => (change.Text("basis"), change.Text("field"), change.GetProperty("priorValue").GetString(), change.GetProperty("newValue").GetString())));
        Assert.All(changes, change => Assert.Equal(administrator, change.GetProperty("actorEmployeeId").GetGuid()));
        Assert.Equal(2L, await PermittedChangeCountAsync(api.Db, candidate));

        var entries = (await api.JsonAsync(HttpMethod.Get, PairingApi.Evidence(candidate), Profile.Administrator)).Items("entries");
        var annexed = entries.Where(entry => entry.Text("action") == "AnnexChanged").ToList();
        Assert.Equal(changes.Select(JsonElementExtensions.Id), annexed.Select(entry => entry.OptionalGuid("recordId")!.Value));
        Assert.Equal(changes.Select(change => change.OptionalTime("changedAt")), annexed.Select(entry => entry.OptionalTime("occurredAt")));
        Assert.All(annexed, entry =>
        {
            Assert.Equal((administrator, mode.ToString()), (entry.GetProperty("actor").GetProperty("employeeId").GetGuid(), entry.GetProperty("actor").Text("identityScheme")));
            Assert.Equal(("PermittedChange", candidate), (entry.Text("recordType"), entry.OptionalGuid("candidateId")!.Value));
            Assert.All(new[] { "revisionId", "contentHash", "outcome", "invalidatedAcceptanceIds" }, field => Assert.Equal(JsonValueKind.Null, entry.GetProperty(field).ValueKind));
        });
        Assert.Equal(JsonValueKind.Null, annexed[0].GetProperty("priorValues").GetProperty("adminReference").ValueKind);
        Assert.Equal(("TM-2026-014", "ADMINISTRATIVE_REFERENCE"),
            (annexed[0].GetProperty("newValues").Text("adminReference"), annexed[0].GetProperty("newValues").Text("basis")));
        Assert.Equal(("Filed with the register.", "ADMINISTRATIVE_REMARK"),
            (annexed[1].GetProperty("newValues").Text("adminRemarks"), annexed[1].GetProperty("newValues").Text("basis")));
        Assert.DoesNotContain(entries, entry => entry.Text("action") == "RevisionCreated" && entry.GetProperty("outcome").GetString() == "RESET_TO_STEP_1");

        var replaced = await ReviseAsync(api, remarked, Profile.Mentor, Form("Lead two design reviews by June 2027"));

        Assert.Equal(1, Step(replaced));
        Assert.Contains((await api.JsonAsync(HttpMethod.Get, PairingApi.Evidence(candidate), Profile.Administrator)).Items("entries"),
            entry => entry.Text("action") == "RevisionCreated" && entry.GetProperty("outcome").GetString() == "RESET_TO_STEP_1");
        // The annex belongs to the candidate, not to a revision, so a replacement leaves it as it was.
        Assert.Equal(annex.GetRawText(), replaced.GetProperty("annex").GetRawText());
    }

    // AC-BR-023-02: no participant and no other employee makes a no-reset change; nothing is recorded.
    [Fact]
    public async Task Nobody_but_talent_management_makes_a_change_that_resets_nothing()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var first = await ReviseAsync(api, await DraftingAsync(api), Profile.Employee);
        var candidate = CandidateOf(first);
        var bothAccepted = await AcceptAsync(api, await AcceptAsync(api, first, Profile.Employee), Profile.Mentor);

        foreach (var caller in new[] { Profile.Employee, Profile.Mentor, Profile.Applicant, Profile.Newcomer })
        {
            using var refused = await TryChangeAnnexAsync(api, bothAccepted, caller, "AdministrativeReference", "TM-2026-014");
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        using var anonymous = await TryChangeAnnexAsync(api, bothAccepted, null, "AdministrativeReference", "TM-2026-014");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(0L, await PermittedChangeCountAsync(api.Db, candidate));
        Assert.Equal(bothAccepted.GetRawText(), (await ReadAsync(api, candidate, Profile.Employee)).GetRawText());
    }

    // AC-FR-068-02, AC-FR-068-03: the exception carries no content: a change naming goals, terms or parties, or a field
    // or basis outside the permitted set, is never classified as non-substantive; it is refused, and nothing is recorded.
    [Fact]
    public async Task A_change_that_carries_content_or_names_no_permitted_basis_cannot_use_the_exception()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var first = await ReviseAsync(api, await DraftingAsync(api), Profile.Employee);
        var candidate = CandidateOf(first);
        var bothAccepted = await AcceptAsync(api, await AcceptAsync(api, first, Profile.Employee), Profile.Mentor);
        string[] bodies =
        [
            """{"basis":"AdministrativeRemark","value":"Goal corrected","goals":["Lead two design reviews"]}""",
            """{"basis":"AdministrativeRemark","value":"Terms corrected","terms":{"durationCategory":"SixMonths"}}""",
            """{"basis":"AdministrativeReference","value":"TM-2026-014","parties":{"mentor":{"employeeId":"8f14e45f-ceea-467a-9575-7b9b1a7e0c11"}}}""",
            """{"basis":"AdministrativeRemark","field":"Goals","value":"Lead two design reviews"}""",
            """{"basis":"Typographical","value":"Lead two design reviews"}""",
            """{"value":"TM-2026-014"}""",
            """{"basis":"AdministrativeReference","value":"TM-2026-014\nsecond line"}""",
        ];

        foreach (var body in bodies)
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            await ProblemAssert.IsProblem(HttpStatusCode.BadRequest, await api.SendAsync(HttpMethod.Post, Annex(candidate), Profile.Administrator, content, Tag(bothAccepted)));
        }

        Assert.Equal(0L, await PermittedChangeCountAsync(api.Db, candidate));
        Assert.Equal(bothAccepted.GetRawText(), (await ReadAsync(api, candidate, Profile.Employee)).GetRawText());
    }

    // The annex is a draft's from the start of drafting; a change records a real difference, and a blank clears the field.
    [Fact]
    public async Task An_annex_change_must_change_the_field_and_a_blank_clears_it()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var drafting = await DraftingAsync(api);
        var candidate = CandidateOf(drafting);

        var referenced = await api.JsonAsync(HttpMethod.Post, Annex(candidate), Profile.Administrator,
            new { basis = "AdministrativeReference", value = "TM-2026-014" }, ifMatch: Tag(drafting));
        var unchanged = await ProblemAssert.IsProblem(HttpStatusCode.Conflict,
            await TryChangeAnnexAsync(api, referenced, Profile.Administrator, "AdministrativeReference", " TM-2026-014 "));
        var cleared = await api.JsonAsync(HttpMethod.Post, Annex(candidate), Profile.Administrator,
            new { basis = "AdministrativeReference", value = "  " }, ifMatch: Tag(referenced));

        Assert.Equal(AgreementDraftEndpoints.AnnexUnchanged, unchanged.Text("title"));
        Assert.Equal(1, Step(cleared));
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("annex").GetProperty("adminReference").ValueKind);
        Assert.Equal(
            new (string?, string?)[] { (null, "TM-2026-014"), ("TM-2026-014", null) },
            cleared.GetProperty("annex").Items("changes").Select(change => (change.GetProperty("priorValue").GetString(), change.GetProperty("newValue").GetString())));
        Assert.Equal(2L, await PermittedChangeCountAsync(api.Db, candidate));
    }
}

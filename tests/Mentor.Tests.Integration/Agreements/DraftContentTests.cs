using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mentor.Core.Agreements;
using Mentor.Tests.Integration.Persistence;
using Mentor.Tests.Integration.Support;
using static Mentor.Tests.Integration.Agreements.DraftApi;

namespace Mentor.Tests.Integration.Agreements;

// The content of an agreement draft through the API (COMP-AGREEMENT-DRAFTING, CONTRACT-AGREEMENT-CONTENT): what a
// revision holds, who reviews it and how its identity is kept (FR-022 to FR-026, FR-055, BR-010, BR-011, BR-017,
// BR-018, DR-006).
public sealed class DraftContentTests(PostgresServer server)
{
    private static readonly string[] Sections = ["barriers", "contingencies", "goals", "parties", "policy", "responsibilities", "terms"];

    // AC-FR-022-01, AC-FR-025-01, AC-FR-025-02, AC-FR-026-01, AC-FR-026-02, AC-BR-017-01, AC-BR-018-01: a revision
    // reproduces the substantive commitments of the agreement: the identified participants, written by the server; the
    // fixed policy text; the terms, the individual goals, the barriers, the contingency plans and each participant's duties.
    [Fact]
    public async Task A_revision_holds_the_identified_participants_the_fixed_commitments_and_the_entered_terms()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var drafting = await DraftingAsync(api);
        Assert.Equal(1, Step(drafting));
        Assert.Equal(JsonValueKind.Null, drafting.GetProperty("currentRevision").ValueKind);
        Assert.Empty(drafting.Items("revisions"));

        var revised = await ReviseAsync(api, drafting, Profile.Employee);

        var current = Current(revised);
        var content = current.GetProperty("content");
        Assert.Equal(Sections, content.PropertyNames());
        var parties = content.GetProperty("parties");
        Assert.Equal((api.Employee(Profile.Employee), "Employee"), (parties.GetProperty("protege").GetProperty("employeeId").GetGuid(), parties.GetProperty("protege").Text("displayName")));
        Assert.Equal((api.Employee(Profile.Mentor), "Mentor"), (parties.GetProperty("mentor").GetProperty("employeeId").GetGuid(), parties.GetProperty("mentor").Text("displayName")));
        Assert.Equal(AgreementPolicy.Clauses.Select(clause => (clause.Clause, clause.Text)),
            content.Items("policy").Select(clause => (clause.Text("clause"), clause.Text("text"))));
        var terms = content.GetProperty("terms");
        Assert.Equal(("TwelveMonths", "2026-12-01", "2027-11-30", "2027-03-01"),
            (terms.Text("durationCategory"), terms.Text("startDate"), terms.Text("endDate"), terms.Text("reviewDate")));
        Assert.Equal((2, 45), (terms.GetProperty("meetingIntervalWeeks").GetInt32(), terms.GetProperty("approximateMinutesPerMeeting").GetInt32()));
        Assert.Equal((1, "Weeks"), (terms.GetProperty("betweenMeetingContact").GetProperty("interval").GetInt32(), terms.GetProperty("betweenMeetingContact").Text("unit")));
        Assert.Equal("Whoever cannot attend tells the other a day ahead and proposes a new date.", terms.Text("missedMeetingArrangements"));
        Assert.Equal(["Lead a design review by June 2027", "Present at the architecture forum"], Texts(content, "goals"));
        Assert.Equal(["Delivery deadlines"], Texts(content, "barriers"));
        Assert.Equal(["Move the meeting rather than skip it"], Texts(content, "contingencies"));
        Assert.Equal(["Share experience of leading design reviews"], Texts(content.GetProperty("responsibilities"), "mentor"));
        Assert.Equal(["Prepare an agenda for each meeting"], Texts(content.GetProperty("responsibilities"), "protege"));

        // The first revision is Step 1, accepted by nobody: writing a revision is not accepting it (AC-FR-027-02).
        Assert.Equal(((int?)1, 1, true, (Guid?)null), (Step(revised), current.GetProperty("revisionNumber").GetInt32(), current.GetProperty("isCurrent").GetBoolean(), current.OptionalGuid("parentRevisionId")));
        Assert.Equal(api.Employee(Profile.Employee), current.GetProperty("createdByEmployeeId").GetGuid());
        Assert.Empty(revised.Items("acceptances"));
        Assert.Empty(current.Items("acceptances"));

        // The revision stores exactly that content in canonical form, and its hash is the hash of the stored text.
        var stored = await ContentAsync(api.Db, current.Id());
        Assert.True(CanonicalJson.IsCanonical(stored));
        Assert.Equal(AgreementContentHash.Compute(stored), current.Text("contentHash"));
        var parsed = AgreementContent.Parse(stored);
        Assert.Equal((2, 45), (parsed.Terms.MeetingIntervalWeeks, parsed.Terms.ApproximateMinutesPerMeeting));
        Assert.Equal(api.Employee(Profile.Mentor), parsed.Parties.Mentor.EmployeeId);
        // No member of the content, at any level, holds or names meeting discussion.
        Assert.DoesNotContain("note", stored, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("discussion\":", stored, StringComparison.OrdinalIgnoreCase);
    }

    // AC-FR-022-02, AC-FR-055-01: before accepting, each participant reviews the complete content, the fixed policy text
    // included, with the hash that identifies it; Talent Management reads it too, and nobody else.
    [Fact]
    public async Task Both_participants_and_talent_management_review_the_complete_content_and_its_identity_before_accepting()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var revised = await ReviseAsync(api, await DraftingAsync(api), Profile.Mentor);
        var candidate = CandidateOf(revised);
        var current = Current(revised);

        foreach (var reader in new[] { Profile.Employee, Profile.Mentor, Profile.Administrator })
        {
            var draft = await ReadAsync(api, candidate, reader);
            Assert.Equal(current.GetRawText(), Current(draft).GetRawText());
            var terms = Current(draft).GetProperty("content").GetProperty("terms");
            Assert.Equal((2, 45), (terms.GetProperty("meetingIntervalWeeks").GetInt32(), terms.GetProperty("approximateMinutesPerMeeting").GetInt32()));
            var revision = await api.JsonAsync(HttpMethod.Get, Revision(candidate, current.Id()), reader);
            Assert.Equal(current.GetRawText(), revision.GetRawText());
        }

        foreach (var outsider in new[] { Profile.Applicant, Profile.Newcomer })
        {
            Assert.Equal(HttpStatusCode.Forbidden, await api.StatusAsync(HttpMethod.Get, Draft(candidate), outsider));
            Assert.Equal(HttpStatusCode.Forbidden, await api.StatusAsync(HttpMethod.Get, Revision(candidate, current.Id()), outsider));
        }

        Assert.Equal(HttpStatusCode.Unauthorized, await api.StatusAsync(HttpMethod.Get, Draft(candidate), profile: null));
        Assert.Equal(HttpStatusCode.NotFound, await api.StatusAsync(HttpMethod.Get, Draft(Guid.NewGuid()), Profile.Administrator));
        Assert.Equal(HttpStatusCode.NotFound, await api.StatusAsync(HttpMethod.Get, Revision(candidate, Guid.NewGuid()), Profile.Employee));
    }

    // AC-FR-023-01, AC-FR-023-02, AC-FR-024-01, AC-FR-024-02, AC-BR-010-01, AC-BR-011-01: each duration category is saved
    // with either date, both or neither, a 12+ agreement with no end date among them, and every date entered stays as
    // entered. Each submission is a revision of its own, numbered on from the one it replaces.
    [Fact]
    public async Task Each_duration_category_is_saved_with_either_date_both_or_neither()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var draft = await DraftingAsync(api);
        var candidate = CandidateOf(draft);
        var number = 0;
        Guid? parent = null;

        foreach (var category in new[] { "SixMonths", "TwelveMonths", "TwelvePlusMonths" })
        {
            foreach (var (start, end) in new (string?, string?)[] { (null, null), ("2026-12-01", null), (null, "2027-11-30"), ("2026-12-01", "2027-11-30") })
            {
                var form = Form();
                var terms = form["terms"]!.AsObject();
                terms["durationCategory"] = category;
                terms["startDate"] = start;
                terms["endDate"] = end;

                draft = await ReviseAsync(api, draft, Profile.Employee, form);

                var current = Current(draft);
                var saved = current.GetProperty("content").GetProperty("terms");
                Assert.Equal((category, start, end), (saved.Text("durationCategory"), saved.GetProperty("startDate").GetString(), saved.GetProperty("endDate").GetString()));
                Assert.Equal((++number, parent), (current.GetProperty("revisionNumber").GetInt32(), current.OptionalGuid("parentRevisionId")));
                parent = current.Id();
            }
        }

        Assert.Equal(12L, await RevisionCountAsync(api.Db, candidate));
        Assert.Equal(Enumerable.Range(1, 12), draft.Items("revisions").Select(revision => revision.GetProperty("revisionNumber").GetInt32()));
        Assert.Equal([.. Enumerable.Repeat(false, 11), true], draft.Items("revisions").Select(revision => revision.GetProperty("isCurrent").GetBoolean()));
    }

    // A submission is validated before anything is written: the refusal names the members at fault, and the draft is
    // unchanged.
    [Fact]
    public async Task An_incomplete_or_impossible_revision_is_refused_and_persists_nothing()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var draft = await DraftingAsync(api);
        var candidate = CandidateOf(draft);
        var form = Form();
        form["terms"]!["durationCategory"] = null;
        form["terms"]!["startDate"] = "2027-01-02";
        form["terms"]!["endDate"] = "2027-01-01";
        form["terms"]!["meetingIntervalWeeks"] = 0;
        form["goals"] = new JsonArray();
        form["responsibilities"]!["protege"] = new JsonArray("  ");

        var refused = await ProblemAssert.IsProblem(HttpStatusCode.BadRequest, await TryReviseAsync(api, draft, Profile.Employee, form));

        Assert.Equal(
            ["goals", "responsibilities.protege[0]", "terms.durationCategory", "terms.endDate", "terms.meetingIntervalWeeks"],
            refused.GetProperty("errors").PropertyNames());
        Assert.Equal(0L, await RevisionCountAsync(api.Db, candidate));
        Assert.Equal(draft.GetRawText(), (await ReadAsync(api, candidate, Profile.Employee)).GetRawText());
    }

    // AC-FR-068-03, AC-BR-012-01, AC-BR-018-01, ADR-004: the parties and the policy are the server's to write, and no body
    // carries a signature, a scanned document or meeting notes; such a body is malformed, and nothing is written.
    [Theory]
    [InlineData("parties")]
    [InlineData("policy")]
    [InlineData("signature")]
    [InlineData("notes")]
    public async Task A_revision_naming_its_parties_its_policy_a_signature_or_notes_is_malformed(string member)
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var draft = await DraftingAsync(api);
        var form = Form();
        form[member] = member == "parties"
            ? new JsonObject { ["protege"] = new JsonObject { ["employeeId"] = api.Employee(Profile.Applicant).ToString() } }
            : (JsonNode)"Anything";

        using var response = await TryReviseAsync(api, draft, Profile.Employee, form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0L, await RevisionCountAsync(api.Db, CandidateOf(draft)));
        Assert.Equal(draft.GetProperty("version").GetInt64(), (await ReadAsync(api, CandidateOf(draft), Profile.Employee)).GetProperty("version").GetInt64());
    }

    private static List<string> Texts(JsonElement element, string property) => [.. element.Items(property).Select(item => item.GetString()!)];
}

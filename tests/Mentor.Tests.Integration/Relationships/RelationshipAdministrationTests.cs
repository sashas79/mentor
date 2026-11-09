using System.Net;
using System.Text.Json;
using Mentor.Api.Hosting;
using Mentor.Api.Relationships;
using Mentor.Api.Requests;
using Mentor.Tests.Integration.Agreements;
using Mentor.Tests.Integration.Formation;
using Mentor.Tests.Integration.Persistence;
using Mentor.Tests.Integration.Requests;
using Mentor.Tests.Integration.Support;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using static Mentor.Tests.Integration.Relationships.RelationshipApi;

namespace Mentor.Tests.Integration.Relationships;

// Formed relationship administration through the API (COMP-RELATIONSHIP-ADMIN, ADR-005, ADR-006): Talent Management edits
// State, Status and IsActive independently, in any direction and combination, on the version it read; every change is
// audited with its prior and new value, the administrator and the time; nothing is derived, nothing is reassigned, and no
// edit forms a relationship (FR-043 to FR-051, FR-054, FR-071, BR-006 to BR-009, DR-005).
public sealed class RelationshipAdministrationTests(PostgresServer server)
{
    // AC-E2E-03, AC-FR-045-01, AC-FR-049-01, AC-FR-051-01, AC-BR-007-01, AC-BR-008-01: Complete + Needs reassigning + Y,
    // then On hold + blank + N: both are saved and retained in the trail, one entry per attribute with its prior and new
    // value, the administrator and the time; neither starts a reassignment or anything else.
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task Talent_management_saves_two_unusual_combinations_in_turn_and_each_change_is_audited(IdentityMode mode)
    {
        await using var api = await WorkflowApi.StartAsync(server, mode);
        var administrator = api.Employee(Profile.Administrator);
        var formed = await FormedAsync(api);
        var candidate = formed.OptionalGuid("candidateId")!.Value;
        Assert.Equal(("InProgress", "Blank", "N", 1L), (formed.Text("state"), formed.Text("status"), formed.Text("isActive"), formed.GetProperty("version").GetInt64()));
        var (notifications, candidateVersion) = (await NotificationCountAsync(api.Db), await PairingApi.VersionAsync(api.Db, candidate));

        var first = await EditAsync(api, formed, new { state = "Complete", status = "NeedsReassigning", isActive = "Y" });
        var second = await EditAsync(api, first, new { state = "OnHold", status = "Blank", isActive = "N" });

        Assert.Equal(("Complete", "NeedsReassigning", "Y", 2L), (first.Text("state"), first.Text("status"), first.Text("isActive"), first.GetProperty("version").GetInt64()));
        Assert.Equal(("OnHold", "Blank", "N", 3L), (second.Text("state"), second.Text("status"), second.Text("isActive"), second.GetProperty("version").GetInt64()));
        Assert.Equal(second.GetRawText(), (await ReadAsync(api, formed.Id(), Profile.Administrator)).GetRawText());

        // The relationship's history: its formation, then one entry per changed attribute of each edit.
        var history = await HistoryAsync(api, formed.Id());
        Assert.Equal("FormationDecided", history[0].Text("action"));
        var changes = history.Where(entry => entry.Text("action") == "AttributeChanged").ToList();
        var edits = changes.GroupBy(entry => entry.OptionalTime("occurredAt"))
            .Select(edit => edit.Select(ChangeOf).OrderBy(change => change.Field, StringComparer.Ordinal).ToList())
            .ToList();
        Assert.Equal(2, edits.Count);
        Assert.Equal([("isActive", "N", "Y"), ("state", "IN_PROGRESS", "COMPLETE"), ("status", "BLANK", "NEEDS_REASSIGNING")], edits[0]);
        Assert.Equal([("isActive", "Y", "N"), ("state", "COMPLETE", "ON_HOLD"), ("status", "NEEDS_REASSIGNING", "BLANK")], edits[1]);
        Assert.Equal([first.OptionalTime("updatedAt"), second.OptionalTime("updatedAt")], changes.Select(entry => entry.OptionalTime("occurredAt")).Distinct());
        Assert.All(changes, entry =>
        {
            Assert.Equal((administrator, mode.ToString(), "Relationship", formed.Id()),
                (entry.GetProperty("actor").GetProperty("employeeId").GetGuid(), entry.GetProperty("actor").Text("identityScheme"), entry.Text("recordType"),
                    entry.OptionalGuid("recordId")!.Value));
            Assert.Equal((JsonValueKind.Null, JsonValueKind.Null), (entry.GetProperty("outcome").ValueKind, entry.GetProperty("candidateId").ValueKind));
        });

        // Neither change reassigned anyone or told anyone anything, and the candidate is as formation left it.
        Assert.Equal((notifications, candidateVersion, 1L),
            (await NotificationCountAsync(api.Db), await PairingApi.VersionAsync(api.Db, candidate), await RelationshipCountAsync(api.Db)));
    }

    // AC-FR-043-01, AC-FR-044-01, AC-FR-047-01, AC-FR-047-02, AC-FR-048-01, AC-BR-006-01: each State is reached from each
    // other directly, In Progress to Complete included, by an edit naming the State alone, which leaves Status and IsActive
    // as they were; Needs reassigning is set and cleared while State stays as it is.
    [Fact]
    public async Task Every_state_is_reached_from_every_other_and_each_attribute_changes_alone()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var relationship = await FormedAsync(api);

        foreach (var state in new[] { "Complete", "InProgress", "OnHold", "Complete", "OnHold", "InProgress", "Complete" })
        {
            relationship = await EditAsync(api, relationship, new { state });
            Assert.Equal((state, "Blank", "N"), AttributesOf(relationship));
        }

        relationship = await EditAsync(api, relationship, new { status = "NeedsReassigning" });
        Assert.Equal(("Complete", "NeedsReassigning", "N"), AttributesOf(relationship));
        relationship = await EditAsync(api, relationship, new { status = "Blank" });
        Assert.Equal(("Complete", "Blank", "N"), AttributesOf(relationship));
        relationship = await EditAsync(api, relationship, new { isActive = "Y" });
        Assert.Equal(("Complete", "Blank", "Y"), AttributesOf(relationship));

        Assert.Equal(10, (await HistoryAsync(api, relationship.Id())).Count(entry => entry.Text("action") == "AttributeChanged"));
    }

    // AC-FR-050-01, AC-FR-071-03, AC-FR-046-01, AC-FR-049-02, AC-BR-008-01, BR-009: an explicit IsActive stands through later
    // State and Status changes, the formation default is never applied again, and every combination of the three is saved.
    [Fact]
    public async Task An_explicit_is_active_stands_through_later_changes_and_every_combination_is_saved()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var relationship = await EditAsync(api, await FormedAsync(api), new { state = "InProgress", status = "Blank", isActive = "Y" });

        // Of the three attributes named, only the one whose value differs is changed and audited (ADR-006, FR-051).
        Assert.Equal([("isActive", "N", "Y")],
            (await HistoryAsync(api, relationship.Id())).Where(entry => entry.Text("action") == "AttributeChanged").Select(ChangeOf));

        foreach (var change in new object[] { new { state = "OnHold" }, new { status = "NeedsReassigning" }, new { state = "Complete", status = "Blank" }, new { state = "InProgress" } })
        {
            relationship = await EditAsync(api, relationship, change);
            Assert.Equal("Y", relationship.Text("isActive"));
        }

        relationship = await EditAsync(api, relationship, new { isActive = "N" });
        Assert.Equal(("InProgress", "Blank", "N"), AttributesOf(relationship));

        foreach (var state in new[] { "InProgress", "OnHold", "Complete" })
        {
            foreach (var status in new[] { "Blank", "NeedsReassigning" })
            {
                foreach (var isActive in new[] { "Y", "N" })
                {
                    if (AttributesOf(relationship) != (state, status, isActive))
                    {
                        relationship = await EditAsync(api, relationship, new { state, status, isActive });
                    }

                    Assert.Equal((state, status, isActive), AttributesOf(relationship));
                }
            }
        }
    }

    // ADR-003: Talent Management alone edits; the parties and Talent Management read the relationship, nobody else does;
    // each party lists their own relationships.
    [Fact]
    public async Task Only_talent_management_edits_and_only_the_parties_and_talent_management_read()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var relationship = await FormedAsync(api);

        foreach (var caller in new Profile?[] { Profile.Employee, Profile.Mentor, Profile.Applicant, Profile.Newcomer, null })
        {
            using var response = await TryEditAsync(api, relationship, caller, new { state = "Complete" });
            Assert.Equal(caller is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
        }

        Assert.Equal(relationship.GetRawText(), (await ReadAsync(api, relationship.Id(), Profile.Administrator)).GetRawText());
        foreach (var reader in new[] { Profile.Employee, Profile.Mentor, Profile.Administrator })
        {
            using var response = await api.SendAsync(HttpMethod.Get, Route(relationship.Id()), reader);
            Assert.Equal((HttpStatusCode.OK, "\"1\""), (response.StatusCode, response.Headers.ETag!.Tag.ToString()));
        }

        foreach (var outsider in new[] { Profile.Applicant, Profile.Newcomer })
        {
            Assert.Equal(HttpStatusCode.Forbidden, await api.StatusAsync(HttpMethod.Get, Route(relationship.Id()), outsider));
        }

        Assert.Equal(HttpStatusCode.Unauthorized, await api.StatusAsync(HttpMethod.Get, Route(relationship.Id()), profile: null));
        Assert.Equal(HttpStatusCode.NotFound, await api.StatusAsync(HttpMethod.Get, Route(Guid.NewGuid()), Profile.Administrator));
        Assert.Equal([relationship.Id()], await OwnAsync(api, Profile.Employee));
        Assert.Equal([relationship.Id()], await OwnAsync(api, Profile.Mentor));
        Assert.Empty(await OwnAsync(api, Profile.Applicant));
        Assert.Empty(await OwnAsync(api, Profile.Administrator));
    }

    // ADR-005, AC-FR-050-01: an edit names the version it was taken on. Without a usable If-Match it is 428, on a version
    // no longer current 409; two edits taken at once on one version end with exactly one taken, so no administrator's
    // explicit choice is overwritten unseen.
    [Fact]
    public async Task An_edit_names_its_version_and_concurrent_edits_never_overwrite_each_other_unseen()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var relationship = await FormedAsync(api);

        foreach (var unusable in new[] { "*", "W/\"1\"", "1" })
        {
            var missing = await ProblemAssert.IsProblem(HttpStatusCode.PreconditionRequired, await TryEditAsync(api, relationship, Profile.Administrator, new { isActive = "Y" }, unusable));
            Assert.Equal(MentorshipRequestEndpoints.VersionRequired, missing.Text("title"));
        }

        var withoutTag = await ProblemAssert.IsProblem(HttpStatusCode.PreconditionRequired,
            await api.SendAsync(HttpMethod.Patch, Route(relationship.Id()), Profile.Administrator, new { isActive = "Y" }));
        Assert.Equal(MentorshipRequestEndpoints.VersionRequired, withoutTag.Text("title"));

        var moved = await EditAsync(api, relationship, new { isActive = "Y" });
        var stale = await ProblemAssert.IsProblem(HttpStatusCode.Conflict, await TryEditAsync(api, relationship, Profile.Administrator, new { isActive = "N" }));
        Assert.Equal(RelationshipEndpoints.RelationshipChanged, stale.Text("title"));
        Assert.Equal(moved.GetRawText(), (await ReadAsync(api, relationship.Id(), Profile.Administrator)).GetRawText());

        var responses = await Task.WhenAll(
            TryEditAsync(api, moved, Profile.Administrator, new { state = "Complete" }),
            TryEditAsync(api, moved, Profile.Administrator, new { state = "OnHold" }));

        Assert.Single(responses, response => response.IsSuccessStatusCode);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        var after = await ReadAsync(api, relationship.Id(), Profile.Administrator);
        Assert.Equal((responses[0].IsSuccessStatusCode ? "Complete" : "OnHold", 3L), (after.Text("state"), after.GetProperty("version").GetInt64()));
        Assert.Single(await HistoryAsync(api, relationship.Id()), entry => entry.Text("action") == "AttributeChanged" && ChangeOf(entry).Field == "state");
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    // AC-FR-044-01, AC-BR-006-01, AC-BR-007-01, AC-FR-071-02: only the business values are accepted, exactly as named; no
    // "Unknown" State, and IsActive only Y or N; an edit names at least one attribute and nothing else, and one that changes
    // nothing is refused. None of them is written or audited.
    [Fact]
    public async Task Only_business_values_are_accepted_and_an_edit_must_change_something()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var relationship = await FormedAsync(api);

        foreach (var malformed in new object[]
        {
            new { },
            new { state = (string?)null },
            new { state = "Unknown" },
            new { state = "IN_PROGRESS" },
            new { status = "Needs reassigning" },
            new { isActive = "y" },
            new { isActive = "Yes" },
            new { isActive = 1 },
            new { isActive = true },
            new { state = "Complete", provenance = "Legacy" },
            new { state = "Complete", startDate = "2027-01-01" },
            new { state = "Complete", mentorEmployeeId = api.Employee(Profile.Applicant) },
        })
        {
            await ProblemAssert.IsProblem(HttpStatusCode.BadRequest, await TryEditAsync(api, relationship, Profile.Administrator, malformed));
        }

        var unchanged = await ProblemAssert.IsProblem(HttpStatusCode.Conflict,
            await TryEditAsync(api, relationship, Profile.Administrator, new { state = "InProgress", status = "Blank", isActive = "N" }));

        Assert.Equal(RelationshipEndpoints.RelationshipUnchanged, unchanged.Text("title"));
        Assert.Equal(relationship.GetRawText(), (await ReadAsync(api, relationship.Id(), Profile.Administrator)).GetRawText());
        Assert.DoesNotContain(await HistoryAsync(api, relationship.Id()), entry => entry.Text("action") == "AttributeChanged");
    }

    // AC-FR-047-02, AC-BR-006-01, BR-009: an edit needs an existing relationship, so it can neither form nor activate a
    // candidate: the candidate, its revision or any other identifier is not a relationship.
    [Fact]
    public async Task No_edit_forms_or_activates_an_unformed_candidate()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var bothAccepted = await FormationApi.BothAcceptedAsync(api);
        var candidate = DraftApi.CandidateOf(bothAccepted);

        foreach (var target in new[] { candidate, DraftApi.Current(bothAccepted).Id(), Guid.NewGuid() })
        {
            using var response = await api.SendAsync(HttpMethod.Patch, Route(target), Profile.Administrator, new { state = "InProgress", isActive = "Y" }, "\"1\"");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        Assert.Equal(("DRAFTING", 0L), (await PairingApi.StatusAsync(api.Db, candidate), await RelationshipCountAsync(api.Db)));
        Assert.Equal(3, DraftApi.Step(await DraftApi.ReadAsync(api, candidate, Profile.Administrator)));
    }

    // AC-FR-044-01, AC-DR-005-01, BR-006: a legacy relationship's unresolved values read as unknown (null), never as a
    // value; Talent Management resolves one explicitly, and the trail records it from unknown to the value chosen.
    [Fact]
    public async Task A_legacy_relationships_unresolved_values_stay_unknown_until_an_administrator_resolves_one()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var administrator = api.Employee(Profile.Administrator);
        var seed = new Seed(api.Db);
        await seed.AuthoriseLegacyEntryAsync(Login.Alice, administrator);
        var legacyId = await seed.LegacyRelationshipAsync(Login.Alice, administrator, api.Employee(Profile.Employee), api.Employee(Profile.Mentor), sourceRow: 12);

        var legacy = await ReadAsync(api, legacyId, Profile.Administrator);

        Assert.Equal(("Legacy", JsonValueKind.Null, 12), (legacy.Text("provenance"), legacy.GetProperty("candidateId").ValueKind, legacy.GetProperty("source").GetProperty("row").GetInt32()));
        Assert.False(string.IsNullOrWhiteSpace(legacy.GetProperty("source").Text("file")));
        Assert.Equal((null, null, null), AttributesOf(legacy));
        Assert.All(new[] { "durationCategory", "startDate", "endDate" }, field => Assert.Equal(JsonValueKind.Null, legacy.GetProperty(field).ValueKind));
        Assert.Equal((api.Employee(Profile.Employee), api.Employee(Profile.Mentor)),
            (legacy.GetProperty("protege").GetProperty("employeeId").GetGuid(), legacy.GetProperty("mentor").GetProperty("employeeId").GetGuid()));
        Assert.Equal(legacyId, (await ReadAsync(api, legacyId, Profile.Employee)).Id());

        var stateResolved = await EditAsync(api, legacy, new { state = "Complete" });
        var resolved = await EditAsync(api, stateResolved, new { status = "Blank", isActive = "Y" });

        Assert.Equal(("Complete", null, null), AttributesOf(stateResolved));
        Assert.Equal(("Complete", "Blank", "Y"), AttributesOf(resolved));
        // Each unknown is recorded as resolved from unknown, the first value of an enumeration included; none is invented.
        Assert.Equal([("isActive", null, "Y"), ("state", null, "COMPLETE"), ("status", null, "BLANK")],
            (await HistoryAsync(api, legacyId)).Where(entry => entry.Text("action") == "AttributeChanged").Select(ChangeOf).OrderBy(change => change.Field, StringComparer.Ordinal));
    }

    // AC-DR-005-01, ADR-005: the view carries the participants, the provenance, the duration and dates of the accepted terms
    // and the three attributes; its version is the ETag of every response, an edit's included, and advances with each edit.
    [Fact]
    public async Task The_view_carries_participants_provenance_and_terms_and_each_edit_advances_its_etag()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var relationship = await FormedAsync(api);
        var candidate = relationship.OptionalGuid("candidateId")!.Value;

        Assert.Equal(("Digital", JsonValueKind.Null), (relationship.Text("provenance"), relationship.GetProperty("source").ValueKind));
        Assert.Equal(relationship.Id(), FormationApi.Relationship(await FormationApi.ReadAsync(api, candidate, Profile.Administrator)).Id());
        Assert.Equal((api.Employee(Profile.Employee), "Employee"),
            (relationship.GetProperty("protege").GetProperty("employeeId").GetGuid(), relationship.GetProperty("protege").Text("displayName")));
        Assert.Equal((api.Employee(Profile.Mentor), "Mentor"),
            (relationship.GetProperty("mentor").GetProperty("employeeId").GetGuid(), relationship.GetProperty("mentor").Text("displayName")));
        Assert.Equal(("TwelveMonths", "2026-12-01", "2027-11-30"),
            (relationship.Text("durationCategory"), relationship.Text("startDate"), relationship.Text("endDate")));
        Assert.Equal(relationship.OptionalTime("createdAt"), relationship.OptionalTime("updatedAt"));

        using var edited = await TryEditAsync(api, relationship, Profile.Administrator, new { status = "NeedsReassigning" });
        Assert.Equal((HttpStatusCode.OK, "\"2\"", false), (edited.StatusCode, edited.Headers.ETag!.Tag.ToString(), edited.Headers.ETag.IsWeak));
        using var read = await api.SendAsync(HttpMethod.Get, Route(relationship.Id()), Profile.Mentor);
        Assert.Equal((HttpStatusCode.OK, "\"2\""), (read.StatusCode, read.Headers.ETag!.Tag.ToString()));
        var listed = Assert.Single((await api.JsonAsync(HttpMethod.Get, Own, Profile.Employee)).EnumerateArray());
        Assert.Equal((2L, "NeedsReassigning"), (listed.GetProperty("version").GetInt64(), listed.Text("status")));
        Assert.True(listed.OptionalTime("updatedAt") > listed.OptionalTime("createdAt"));
    }

    // AC-FR-054-01, AC-DR-005-01: relationships involving the same people stay separately identifiable: each has its own
    // identity, attributes and history, and an edit of one leaves the other as it was.
    [Fact]
    public async Task Relationships_involving_the_same_people_stay_separately_identifiable()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var first = await FormedAsync(api);
        var second = await FormedAsync(api, DraftApi.Form("Mentor a new joiner"));

        Assert.NotEqual(first.Id(), second.Id());
        Assert.Equal(first.GetProperty("protege").GetRawText(), second.GetProperty("protege").GetRawText());
        Assert.Equal(first.GetProperty("mentor").GetRawText(), second.GetProperty("mentor").GetRawText());
        Assert.Equal([first.Id(), second.Id()], await OwnAsync(api, Profile.Employee));
        Assert.Equal([first.Id(), second.Id()], await OwnAsync(api, Profile.Mentor));

        await EditAsync(api, second, new { state = "Complete" });

        Assert.Equal(first.GetRawText(), (await ReadAsync(api, first.Id(), Profile.Administrator)).GetRawText());
        Assert.DoesNotContain(await HistoryAsync(api, first.Id()), entry => entry.Text("action") == "AttributeChanged");
    }

    // ADR-006: under /api/relationships the API answers exactly these routes: the module's own, none of which creates or
    // removes a relationship or changes anything but its three attributes, and a relationship's meetings
    // (COMP-MEETINGS-NOTIFICATIONS) and goals (COMP-GOALS-PROGRESS), which change no relationship.
    [Fact]
    public async Task Under_the_relationships_prefix_the_api_answers_exactly_these_routes()
    {
        await using var api = await WorkflowApi.StartAsync(server);

        var routes = api.Api.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText!.StartsWith("/api/relationships", StringComparison.Ordinal))
            .Select(endpoint => $"{string.Join(',', endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods)} {endpoint.RoutePattern.RawText}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
        [
            "GET /api/relationships",
            "GET /api/relationships/{relationshipId:guid}",
            "GET /api/relationships/{relationshipId:guid}/goals",
            "GET /api/relationships/{relationshipId:guid}/meetings",
            "PATCH /api/relationships/{relationshipId:guid}",
            "POST /api/relationships/{relationshipId:guid}/meetings",
        ], routes);
    }
}

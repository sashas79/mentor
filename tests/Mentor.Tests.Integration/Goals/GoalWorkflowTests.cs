using System.Net;
using System.Text.Json;
using Mentor.Api.Goals;
using Mentor.Api.Hosting;
using Mentor.Core.Goals;
using Mentor.Tests.Integration.Agreements;
using Mentor.Tests.Integration.Formation;
using Mentor.Tests.Integration.Persistence;
using Mentor.Tests.Integration.Relationships;
using Mentor.Tests.Integration.Requests;
using Mentor.Tests.Integration.Support;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using static Mentor.Tests.Integration.Goals.GoalApi;

namespace Mentor.Tests.Integration.Goals;

// The goal workflow through the API (COMP-GOALS-PROGRESS, BR-005): the protégé's progress needs no approval; a completion
// request stays visibly pending, with the goal incomplete, until the relationship's mentor alone approves or rejects it,
// once; an approval completes the goal in the same transaction, and a rejection leaves it incomplete with no reason,
// administrator stage or resubmission loop (FR-034 to FR-037, FR-039, DR-009 to DR-011).
public sealed class GoalWorkflowTests(PostgresServer server)
{
    // AC-E2E-04, AC-FR-034-01, AC-FR-035-01, AC-FR-036-01, AC-FR-037-01, AC-FR-037-02, AC-DR-009-01, AC-DR-010-01,
    // AC-DR-011-01: each goal is its own record with its own state; progress is recorded at once; a request is pending and
    // the goal incomplete until the mentor approves; the approval completes the goal without touching its wording or the
    // agreement, and is retained with the mentor and the server time; each action is audited and the other party told.
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task Progress_needs_no_approval_and_a_request_stays_pending_until_the_mentor_approves_it(IdentityMode mode)
    {
        await using var api = await WorkflowApi.StartAsync(server, mode);
        var (protege, mentor) = (api.Employee(Profile.Employee), api.Employee(Profile.Mentor));
        var (relationship, goals) = await FormedAsync(api);
        var (first, second) = (goals[0].Id(), goals[1].Id());
        var agreement = goals[0].GetProperty("formedAgreementId").GetGuid();
        var accepted = await api.Db.ScalarAsync<string>(Login.Owner, "SELECT content FROM mentor.formed_agreement WHERE id = $1", agreement);

        // Each agreed goal is its own record, worded and ordered as agreed, incomplete, with no progress yet.
        Assert.Equal([(1, "Lead a design review by June 2027", "Incomplete"), (2, "Present at the architecture forum", "Incomplete")],
            goals.Select(goal => (goal.GetProperty("position").GetInt32(), goal.Text("agreedDescription"), goal.Text("completionStatus"))));
        Assert.All(goals, goal =>
        {
            Assert.Equal((relationship, agreement, (Guid?)null), (goal.GetProperty("relationshipId").GetGuid(), goal.GetProperty("formedAgreementId").GetGuid(), Pending(goal)));
            Assert.Empty(goal.Items("progress"));
        });

        // Ordinary progress: recorded at once, dated by the server, needing no approval and telling nobody; the goal stays incomplete.
        var notifications = await RelationshipApi.NotificationCountAsync(api.Db);
        var progressed = await SubmitAsync(api, first);
        var progress = Latest(progressed);
        Assert.Equal((FirstUpdate, false, protege), (progress.Text("progressUpdate"), progress.GetProperty("completionRequested").GetBoolean(), progress.GetProperty("protegeEmployeeId").GetGuid()));
        Assert.Equal((JsonValueKind.Null, JsonValueKind.Null), (progress.GetProperty("completion").ValueKind, progress.GetProperty("decision").ValueKind));
        Assert.NotNull(progress.OptionalTime("submittedAt"));
        Assert.Equal(("Incomplete", (Guid?)null), (progressed.Text("completionStatus"), Pending(progressed)));
        Assert.Equal(notifications, await RelationshipApi.NotificationCountAsync(api.Db));

        // A completion request: visibly pending with the goal still incomplete; the mentor alone is told to decide.
        var requested = await RequestAsync(api, first);
        var request = Latest(requested);
        Assert.Equal([(FirstUpdate, false, null), (RequestUpdate, true, "Pending")], EntriesOf(requested));
        Assert.Equal(("Incomplete", (Guid?)request.Id()), (requested.Text("completionStatus"), Pending(requested)));
        Assert.Equal("INCOMPLETE", await StatusAsync(api.Db, first));
        Assert.Equal(("GoalCompletionRequested", first, "DecideGoalCompletion"), (await PairingApi.InboxAsync(api, Profile.Mentor))[0]);
        Assert.DoesNotContain(await PairingApi.InboxAsync(api, Profile.Employee), notification => notification.EventType.StartsWith("Goal", StringComparison.Ordinal));
        Assert.Empty(await PairingApi.InboxAsync(api, Profile.Administrator));
        Assert.Equal(requested.GetRawText(), (await ReadAsync(api, first, Profile.Administrator)).GetRawText());

        // The mentor approves: the goal is complete and the request approved, with the mentor and the server time retained.
        var approved = await DecideAsync(api, first, request.Id(), "Approved");
        var decided = Latest(approved);
        var decision = decided.GetProperty("decision");
        var decidedAt = decision.OptionalTime("decidedAt");
        Assert.Equal(("Complete", (Guid?)null, "Approved"), (approved.Text("completionStatus"), Pending(approved), decided.Text("completion")));
        Assert.Equal(("Approved", mentor), (decision.Text("decision"), decision.GetProperty("mentorEmployeeId").GetGuid()));
        Assert.True(decidedAt > request.OptionalTime("submittedAt"));
        Assert.Equal(["decidedAt", "decision", "id", "mentorEmployeeId"], decision.PropertyNames());
        Assert.Equal(("COMPLETE", 1L), (await StatusAsync(api.Db, first), await DecisionCountAsync(api.Db)));
        Assert.Equal(("GoalCompletionDecided", first, "None"), (await PairingApi.InboxAsync(api, Profile.Employee))[0]);

        // The agreed wording and the formed agreement are as they were, and the other goal keeps its own state.
        Assert.Equal("Lead a design review by June 2027", approved.Text("agreedDescription"));
        Assert.Equal(accepted, await api.Db.ScalarAsync<string>(Login.Owner, "SELECT content FROM mentor.formed_agreement WHERE id = $1", agreement));
        var other = await ReadAsync(api, second, Profile.Mentor);
        Assert.Equal(("Incomplete", 0), (other.Text("completionStatus"), other.Items("progress").Count));
        Assert.True(approved.OptionalTime("updatedAt") > approved.OptionalTime("createdAt"));

        // The trail: each entry by the protégé, the decision and the goal's completion by the mentor, under their scheme, at their time.
        foreach (var (entry, completionRequested) in new[] { (progress, false), (request, true) })
        {
            var submitted = Assert.Single(await TrailAsync(api, "GoalProgress", entry.Id()));
            Assert.Equal(("GoalProgressSubmitted", protege, mode.ToString(), entry.OptionalTime("submittedAt")),
                (submitted.Text("action"), ActorOf(submitted), submitted.GetProperty("actor").Text("identityScheme"), submitted.OptionalTime("occurredAt")));
            Assert.Equal((first, completionRequested, JsonValueKind.Null),
                (submitted.GetProperty("newValues").GetProperty("goalId").GetGuid(), submitted.GetProperty("newValues").GetProperty("completionRequested").GetBoolean(),
                    submitted.GetProperty("outcome").ValueKind));
        }

        var decisionEntry = Assert.Single(await TrailAsync(api, "GoalApproval", decision.Id()));
        Assert.Equal(("GoalCompletionDecided", mentor, mode.ToString(), "APPROVED", decidedAt),
            (decisionEntry.Text("action"), ActorOf(decisionEntry), decisionEntry.GetProperty("actor").Text("identityScheme"), decisionEntry.Text("outcome"),
                decisionEntry.OptionalTime("occurredAt")));
        Assert.Equal(("APPROVED", request.Id()), (decisionEntry.GetProperty("newValues").Text("decision"), decisionEntry.GetProperty("newValues").GetProperty("progressId").GetGuid()));
        var completion = Assert.Single(await TrailAsync(api, "Goal", first));
        Assert.Equal(("GoalCompletionDecided", mentor, "APPROVED", decidedAt),
            (completion.Text("action"), ActorOf(completion), completion.Text("outcome"), completion.OptionalTime("occurredAt")));
        Assert.Equal(("INCOMPLETE", "COMPLETE"),
            (completion.GetProperty("priorValues").Text("completionStatus"), completion.GetProperty("newValues").Text("completionStatus")));
    }

    // AC-FR-037-02, AC-E2E-04, AC-DR-011-01, DEC-008: a rejection is retained with the mentor and the time and leaves the
    // goal incomplete. It takes no reason, has no administrator stage, and the rejected request is never decided again.
    // The protégé is told, progress continues, and a later request is a new request, decided on its own.
    [Fact]
    public async Task A_rejection_is_retained_and_leaves_the_goal_incomplete_with_no_reason_stage_or_second_decision()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var mentor = api.Employee(Profile.Mentor);
        var (_, goals) = await FormedAsync(api);
        var goal = goals[0].Id();
        var request = Latest(await RequestAsync(api, goal)).Id();

        await ProblemAssert.IsProblem(HttpStatusCode.BadRequest,
            await TryDecideAsync(api, goal, request, Profile.Mentor, new { decision = "Rejected", reason = "Not yet" }));
        Assert.Equal(0L, await DecisionCountAsync(api.Db));

        var rejected = await DecideAsync(api, goal, request, "Rejected");

        var entry = Latest(rejected);
        var decision = entry.GetProperty("decision");
        Assert.Equal(("Incomplete", (Guid?)null, "Rejected"), (rejected.Text("completionStatus"), Pending(rejected), entry.Text("completion")));
        Assert.Equal(("Rejected", mentor), (decision.Text("decision"), decision.GetProperty("mentorEmployeeId").GetGuid()));
        Assert.NotNull(decision.OptionalTime("decidedAt"));
        Assert.Equal("INCOMPLETE", await StatusAsync(api.Db, goal));
        Assert.Equal(("GoalCompletionDecided", goal, "SubmitProgress"), (await PairingApi.InboxAsync(api, Profile.Employee))[0]);
        Assert.Empty(await PairingApi.InboxAsync(api, Profile.Administrator));
        var trail = Assert.Single(await TrailAsync(api, "GoalApproval", decision.Id()));
        Assert.Equal(("REJECTED", "REJECTED", mentor, decision.OptionalTime("decidedAt")),
            (trail.Text("outcome"), trail.GetProperty("newValues").Text("decision"), ActorOf(trail), trail.OptionalTime("occurredAt")));
        Assert.Empty(await TrailAsync(api, "Goal", goal));

        foreach (var again in new[] { "Approved", "Rejected" })
        {
            var decidedAgain = await ProblemAssert.IsProblem(HttpStatusCode.Conflict, await TryDecideAsync(api, goal, request, Profile.Mentor, new { decision = again }));
            Assert.Equal(GoalEndpoints.CompletionDecided, decidedAgain.Text("title"));
        }

        Assert.Equal((1L, "INCOMPLETE"), (await DecisionCountAsync(api.Db), await StatusAsync(api.Db, goal)));

        await SubmitAsync(api, goal, "Ran a second design review");
        var renewed = await RequestAsync(api, goal, "Ran both reviews to the agreed checklist");

        Assert.Equal([(RequestUpdate, true, "Rejected"), ("Ran a second design review", false, null), ("Ran both reviews to the agreed checklist", true, "Pending")],
            EntriesOf(renewed));
        Assert.Equal((Guid?)Latest(renewed).Id(), Pending(renewed));

        // The new request is decided on its own; the rejected one stays rejected.
        var approved = await DecideAsync(api, goal, Latest(renewed).Id(), "Approved");

        Assert.Equal([(RequestUpdate, true, "Rejected"), ("Ran a second design review", false, null), ("Ran both reviews to the agreed checklist", true, "Approved")],
            EntriesOf(approved));
        Assert.Equal(("Complete", 2L, "COMPLETE"), (approved.Text("completionStatus"), await DecisionCountAsync(api.Db), await StatusAsync(api.Db, goal)));
    }

    // AC-FR-039-01, AC-FR-037-01, ADR-003: completion is decided only by the relationship's mentor. Talent Management, the
    // protégé, another approved mentor and anyone else are refused, and the request stays pending with the goal incomplete.
    [Fact]
    public async Task Only_the_relationships_mentor_decides_completion_and_talent_management_has_no_step()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var (_, goals) = await FormedAsync(api);
        var goal = goals[0].Id();
        var pending = await RequestAsync(api, goal);
        var request = Pending(pending)!.Value;
        await PairingApi.ApproveApplicantAsync(api);

        foreach (var caller in new Profile?[] { Profile.Administrator, Profile.Employee, Profile.Applicant, Profile.Newcomer, null })
        {
            foreach (var decision in new[] { "Approved", "Rejected" })
            {
                using var response = await TryDecideAsync(api, goal, request, caller, new { decision });
                Assert.Equal(caller is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
            }
        }

        Assert.Equal(pending.GetRawText(), (await ReadAsync(api, goal, Profile.Administrator)).GetRawText());
        Assert.Equal((0L, "INCOMPLETE"), (await DecisionCountAsync(api.Db), await StatusAsync(api.Db, goal)));
    }

    // ADR-003, BR-005: the protégé alone submits progress and asks for completion; nobody else records either.
    [Fact]
    public async Task Only_the_protege_submits_progress_and_asks_for_completion()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var (_, goals) = await FormedAsync(api);
        var goal = goals[0].Id();

        foreach (var caller in new Profile?[] { Profile.Mentor, Profile.Administrator, Profile.Applicant, Profile.Newcomer, null })
        {
            using var progress = await TrySubmitAsync(api, goal, caller);
            using var request = await TryRequestAsync(api, goal, caller);
            var expected = caller is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
            Assert.Equal((expected, expected), (progress.StatusCode, request.StatusCode));
        }

        using var unknownProgress = await TrySubmitAsync(api, Guid.NewGuid(), Profile.Employee);
        using var unknownRequest = await TryRequestAsync(api, Guid.NewGuid(), Profile.Employee);
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (unknownProgress.StatusCode, unknownRequest.StatusCode));
        Assert.Equal(0L, await ProgressCountAsync(api.Db));
        Assert.DoesNotContain(await PairingApi.InboxAsync(api, Profile.Mentor), notification => notification.EventType.StartsWith("Goal", StringComparison.Ordinal));
    }

    // AC-FR-034-01, AC-FR-040-01, AC-FR-066-01, ADR-003: the parties and Talent Management read the goals, each listed with
    // its own completion status and structured progress, all alike; nobody else does. A historical relationship of the
    // same people has no formed agreement, so no goals.
    [Fact]
    public async Task The_parties_and_talent_management_read_goals_and_progress_and_nobody_else_does()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var (relationship, goals) = await FormedAsync(api);
        var legacy = await CallerMentorship.LegacyAsync(api.Db, api.Callers);
        var (goal, second) = (goals[0].Id(), goals[1].Id());
        await SubmitAsync(api, goal);
        var requested = await RequestAsync(api, goal);
        var progressed = await SubmitAsync(api, second, "Booked a slot at the architecture forum");
        var listed = (await ListAsync(api, relationship, Profile.Employee)).Select(read => read.GetRawText()).ToList();

        Assert.Equal([requested.GetRawText(), progressed.GetRawText()], listed);
        foreach (var reader in new[] { Profile.Employee, Profile.Mentor, Profile.Administrator })
        {
            Assert.Equal(listed, (await ListAsync(api, relationship, reader)).Select(read => read.GetRawText()));
            Assert.Equal(requested.GetRawText(), (await ReadAsync(api, goal, reader)).GetRawText());
        }

        foreach (var outsider in new Profile?[] { Profile.Applicant, Profile.Newcomer, null })
        {
            var expected = outsider is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
            Assert.Equal(expected, await api.StatusAsync(HttpMethod.Get, OfRelationship(relationship), outsider));
            Assert.Equal(expected, await api.StatusAsync(HttpMethod.Get, Goal(goal), outsider));
        }

        foreach (var unknown in new[] { Guid.NewGuid(), goal })
        {
            Assert.Equal(HttpStatusCode.NotFound, await api.StatusAsync(HttpMethod.Get, OfRelationship(unknown), Profile.Administrator));
        }

        foreach (var unknown in new[] { Guid.NewGuid(), relationship })
        {
            Assert.Equal(HttpStatusCode.NotFound, await api.StatusAsync(HttpMethod.Get, Goal(unknown), Profile.Administrator));
        }

        Assert.Empty(await ListAsync(api, legacy, Profile.Administrator));
        Assert.Empty(await ListAsync(api, legacy, Profile.Employee));
    }

    // ADR-003: a goal is reached through its own relationship only. With two relationships of one protégé, each mentor
    // reads and decides the goals of their own relationship and is refused the other's; each list holds its own goals.
    [Fact]
    public async Task A_goal_is_read_and_decided_only_through_its_own_relationship()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var (first, firstGoals) = await FormedAsync(api);
        await PairingApi.ApproveApplicantAsync(api);
        var drafting = await FormationApi.DraftingWithAsync(api, await PairingApi.SubmitAsync(api, Profile.Employee), Profile.Applicant);
        var revised = await DraftApi.ReviseAsync(api, drafting, Profile.Employee, DraftApi.Form("Mentor a new joiner"));
        var second = FormationApi.Relationship(await FormationApi.DecideAsync(api, await FormationApi.BothAcceptAsync(api, revised, Profile.Applicant))).Id();
        var secondGoals = await ListAsync(api, second, Profile.Employee);
        var (mine, theirs) = (firstGoals[0].Id(), secondGoals[0].Id());
        var myRequest = Pending(await RequestAsync(api, mine))!.Value;
        var theirRequest = Pending(await RequestAsync(api, theirs))!.Value;

        Assert.Equal(["Mentor a new joiner", "Present at the architecture forum"], secondGoals.Select(goal => goal.Text("agreedDescription")));
        Assert.All(secondGoals, goal => Assert.Equal(second, goal.GetProperty("relationshipId").GetGuid()));
        foreach (var (mentor, goal, request, relationship) in new[] { (Profile.Mentor, theirs, theirRequest, second), (Profile.Applicant, mine, myRequest, first) })
        {
            Assert.Equal(HttpStatusCode.Forbidden, await api.StatusAsync(HttpMethod.Get, Goal(goal), mentor));
            Assert.Equal(HttpStatusCode.Forbidden, await api.StatusAsync(HttpMethod.Get, OfRelationship(relationship), mentor));
            using var refused = await TryDecideAsync(api, goal, request, mentor, new { decision = "Approved" });
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        var approved = await api.JsonAsync(HttpMethod.Post, Decision(theirs, theirRequest), Profile.Applicant, new { decision = "Approved" });

        Assert.Equal(("Complete", second), (approved.Text("completionStatus"), approved.GetProperty("relationshipId").GetGuid()));
        Assert.Equal(("INCOMPLETE", (Guid?)myRequest), (await StatusAsync(api.Db, mine), Pending(await ReadAsync(api, mine, Profile.Mentor))));
        Assert.Equal(HttpStatusCode.OK, await api.StatusAsync(HttpMethod.Get, Goal(theirs), Profile.Employee));
    }

    // BR-005, ADR-006: the goals go on whatever the relationship's State, which Talent Management administers apart from
    // them: progress, a request and its decision are taken while the relationship is On hold and after it is Complete.
    [Fact]
    public async Task Progress_requests_and_decisions_go_on_whatever_the_relationships_state()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var (relationship, goals) = await FormedAsync(api);
        var onHold = await RelationshipApi.EditAsync(api, await RelationshipApi.ReadAsync(api, relationship, Profile.Administrator), new { state = "OnHold" });

        await SubmitAsync(api, goals[0].Id());
        var request = Pending(await RequestAsync(api, goals[0].Id()))!.Value;
        await RelationshipApi.EditAsync(api, onHold, new { state = "Complete" });
        await SubmitAsync(api, goals[1].Id());
        var approved = await DecideAsync(api, goals[0].Id(), request, "Approved");

        Assert.Equal(("Complete", "Complete"),
            (approved.Text("completionStatus"), (await RelationshipApi.ReadAsync(api, relationship, Profile.Administrator)).Text("state")));
    }

    // BR-005, AC-FR-036-01: a goal has at most one request awaiting the mentor, and a complete goal takes none; ordinary
    // progress is never held back, a complete goal's included. A refused request records nothing and tells nobody.
    [Fact]
    public async Task One_request_awaits_the_mentor_at_a_time_and_a_complete_goal_takes_none()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var (_, goals) = await FormedAsync(api);
        var (goal, other) = (goals[0].Id(), goals[1].Id());
        var pending = await RequestAsync(api, goal);
        var notifications = await RelationshipApi.NotificationCountAsync(api.Db);

        var second = await ProblemAssert.IsProblem(HttpStatusCode.Conflict, await TryRequestAsync(api, goal, Profile.Employee));
        Assert.Equal(GoalEndpoints.CompletionPending, second.Text("title"));
        Assert.Equal(pending.GetRawText(), (await ReadAsync(api, goal, Profile.Employee)).GetRawText());

        await SubmitAsync(api, goal, "Shared the checklist with another team");
        Assert.NotNull(Pending(await RequestAsync(api, other)));
        await DecideAsync(api, goal, Pending(pending)!.Value, "Approved");

        var complete = await ProblemAssert.IsProblem(HttpStatusCode.Conflict, await TryRequestAsync(api, goal, Profile.Employee));
        Assert.Equal(GoalEndpoints.GoalComplete, complete.Text("title"));
        var after = await SubmitAsync(api, goal, "Coached a colleague through their first review");

        Assert.Equal(("Complete", (Guid?)null), (after.Text("completionStatus"), Pending(after)));
        Assert.Equal([(RequestUpdate, true, "Approved"), ("Shared the checklist with another team", false, null), ("Coached a colleague through their first review", false, null)],
            EntriesOf(after));
        Assert.Equal((4L, notifications + 2), (await ProgressCountAsync(api.Db), await RelationshipApi.NotificationCountAsync(api.Db)));
    }

    // DR-011, BR-005: a decision names a completion request of the goal (ordinary progress needs none, and an entry of
    // another goal, or none at all, is not found) and is Approved or Rejected, with nothing else. A refusal decides nothing.
    [Fact]
    public async Task A_decision_names_a_completion_request_of_the_goal_and_is_approved_or_rejected()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var (_, goals) = await FormedAsync(api);
        var (goal, other) = (goals[0].Id(), goals[1].Id());
        var ordinary = Latest(await SubmitAsync(api, goal)).Id();
        var request = Latest(await RequestAsync(api, goal)).Id();
        var elsewhere = Latest(await RequestAsync(api, other)).Id();

        var notARequest = await ProblemAssert.IsProblem(HttpStatusCode.Conflict, await TryDecideAsync(api, goal, ordinary, Profile.Mentor, new { decision = "Approved" }));
        Assert.Equal(GoalEndpoints.NotACompletionRequest, notARequest.Text("title"));
        foreach (var (target, entry) in new[] { (goal, elsewhere), (goal, Guid.NewGuid()), (goal, goal), (Guid.NewGuid(), request), (other, request) })
        {
            using var response = await TryDecideAsync(api, target, entry, Profile.Mentor, new { decision = "Approved" });
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        foreach (var malformed in new object[]
        {
            new { },
            new { decision = (string?)null },
            new { decision = "Pending" },
            new { decision = "Complete" },
            new { decision = 7 },
            new { decision = "Approved", mentorEmployeeId = api.Employee(Profile.Administrator) },
            new { decision = "Approved", decidedAt = "2026-01-01T00:00:00Z" },
            new { decision = "Approved", comment = "Well done" },
        })
        {
            await ProblemAssert.IsProblem(HttpStatusCode.BadRequest, await TryDecideAsync(api, goal, request, Profile.Mentor, malformed));
        }

        Assert.Equal((0L, "INCOMPLETE", "INCOMPLETE"), (await DecisionCountAsync(api.Db), await StatusAsync(api.Db, goal), await StatusAsync(api.Db, other)));
        Assert.Equal((Guid?)request, Pending(await ReadAsync(api, goal, Profile.Mentor)));
        Assert.Equal((Guid?)elsewhere, Pending(await ReadAsync(api, other, Profile.Mentor)));
    }

    // FR-035, FR-059, NFR-002: an entry states its progress update, at most 4000 characters after trimming, and nothing
    // else: no meeting notes, completion flag, status, wording, date or person. A refusal records nothing and repeats nothing.
    [Fact]
    public async Task A_progress_update_is_required_and_bounded_and_a_body_naming_anything_else_is_refused()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var (_, goals) = await FormedAsync(api);
        var goal = goals[0].Id();
        var overlong = new string('u', ProgressSubmission.MaxLength + 1);

        foreach (var malformed in new object[]
        {
            new { },
            new { progressUpdate = (string?)null },
            new { progressUpdate = "   " },
            new { progressUpdate = overlong },
            new { progressUpdate = "Ran the review\u0000" },
            new { progressUpdate = FirstUpdate, notes = "What we discussed" },
            new { progressUpdate = FirstUpdate, completionRequested = true },
            new { progressUpdate = FirstUpdate, completionStatus = "Complete" },
            new { progressUpdate = FirstUpdate, agreedDescription = "Lead every design review" },
            new { progressUpdate = FirstUpdate, submittedAt = "2026-01-01T00:00:00Z" },
            new { progressUpdate = FirstUpdate, protegeEmployeeId = api.Employee(Profile.Mentor) },
        })
        {
            foreach (var route in new[] { Progress(goal), CompletionRequests(goal) })
            {
                var refused = await ProblemAssert.IsProblem(HttpStatusCode.BadRequest, await api.SendAsync(HttpMethod.Post, route, Profile.Employee, malformed));
                Assert.DoesNotContain(overlong[..100], refused.GetRawText(), StringComparison.Ordinal);
            }
        }

        Assert.Equal(0L, await ProgressCountAsync(api.Db));
        Assert.DoesNotContain(await PairingApi.InboxAsync(api, Profile.Mentor), notification => notification.EventType.StartsWith("Goal", StringComparison.Ordinal));

        var longest = await SubmitAsync(api, goal, $"  {overlong[..^1]}  ");
        Assert.Equal(overlong[..^1], Latest(longest).Text("progressUpdate"));
    }

    // Commands on one goal run one after another: two requests at once leave one pending request, and two decisions at once
    // leave one decision and a goal whose status is the one that decision gives it.
    [Fact]
    public async Task Concurrent_requests_or_decisions_on_one_goal_never_both_take_effect()
    {
        await using var api = await WorkflowApi.StartAsync(server);
        var (_, goals) = await FormedAsync(api);
        var goal = goals[0].Id();

        var requests = await Task.WhenAll(TryRequestAsync(api, goal, Profile.Employee), TryRequestAsync(api, goal, Profile.Employee));

        Assert.Single(requests, response => response.IsSuccessStatusCode);
        Assert.Equal(GoalEndpoints.CompletionPending,
            (await ProblemAssert.IsProblem(HttpStatusCode.Conflict, Assert.Single(requests, response => !response.IsSuccessStatusCode))).Text("title"));
        var pending = await ReadAsync(api, goal, Profile.Mentor);
        Assert.Single(pending.Items("progress"));
        var request = Pending(pending)!.Value;

        var decisions = await Task.WhenAll(
            TryDecideAsync(api, goal, request, Profile.Mentor, new { decision = "Approved" }),
            TryDecideAsync(api, goal, request, Profile.Mentor, new { decision = "Rejected" }));

        Assert.Single(decisions, response => response.IsSuccessStatusCode);
        Assert.Equal(GoalEndpoints.CompletionDecided,
            (await ProblemAssert.IsProblem(HttpStatusCode.Conflict, Assert.Single(decisions, response => !response.IsSuccessStatusCode))).Text("title"));
        var approvedWon = decisions[0].IsSuccessStatusCode;
        var after = await ReadAsync(api, goal, Profile.Employee);
        Assert.Equal((approvedWon ? "Complete" : "Incomplete", approvedWon ? "Approved" : "Rejected"), (after.Text("completionStatus"), Latest(after).Text("completion")));
        Assert.Equal(1L, await DecisionCountAsync(api.Db));
        foreach (var response in requests.Concat(decisions))
        {
            response.Dispose();
        }
    }

    // AC-DR-009-01, BR-005: under /api/goals, with a relationship's goals, the API answers exactly these routes. None changes
    // a goal's agreed wording or removes a goal, an entry or a decision; the database refuses such changes too (ADR-013).
    [Fact]
    public async Task The_goal_routes_are_exactly_these_and_none_changes_an_agreed_goal()
    {
        await using var api = await WorkflowApi.StartAsync(server);

        var routes = api.Api.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText!.StartsWith("/api/goals", StringComparison.Ordinal)
                || endpoint.RoutePattern.RawText == GoalEndpoints.RelationshipGoalsRoute)
            .Select(endpoint => $"{string.Join(',', endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods)} {endpoint.RoutePattern.RawText}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
        [
            "GET /api/goals/{goalId:guid}",
            "GET /api/relationships/{relationshipId:guid}/goals",
            "POST /api/goals/{goalId:guid}/completion-requests",
            "POST /api/goals/{goalId:guid}/completion-requests/{requestId:guid}/decision",
            "POST /api/goals/{goalId:guid}/progress",
        ], routes);
    }
}

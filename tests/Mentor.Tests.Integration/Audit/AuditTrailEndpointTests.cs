using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mentor.Api.Audit;
using Mentor.Api.Hosting;
using Mentor.Core.Agreements;
using Mentor.Core.Audit;
using Mentor.Core.Relationships;
using Mentor.Infrastructure.Persistence;
using Mentor.Tests.Integration.Persistence;
using Mentor.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Mentor.Tests.Integration.Identity.IdentityApi;

namespace Mentor.Tests.Integration.Audit;

// Talent Management administrators verify the trail through read-only endpoints filtered by record (ADR-007), under
// audit-trail.read of the matrix (ADR-003), identically under Entra and the simulated identity.
public sealed class AuditTrailEndpointTests(PostgresServer server)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task Only_a_talent_management_administrator_reads_the_trail(IdentityMode mode)
    {
        await using var db = await server.CreateDatabaseAsync();
        var callers = await TestCallers.RecordAsync(db, mode);
        var mentorship = await new Seed(db).FormedMentorshipAsync();
        await using var api = Start(db, mode.ToString());

        foreach (var route in new[] { RecordRoute(AuditRecordType.Relationship, mentorship.Relationship), RecordTypeRoute(AuditRecordType.Register), CandidateRoute(mentorship.Candidate) })
        {
            await ProblemAssert.IsProblem(HttpStatusCode.Unauthorized, await SendAsync(api, route));
            foreach (var profile in new[] { Profile.Newcomer, Profile.Employee, Profile.Applicant, Profile.Mentor })
            {
                await ProblemAssert.IsProblem(HttpStatusCode.Forbidden, await SendAsync(api, route, As(callers, profile)));
            }

            Assert.Equal(HttpStatusCode.OK, (await SendAsync(api, route, As(callers, Profile.Administrator))).StatusCode);
        }
    }

    // AC-DR-014-01, AC-FR-051-01, AC-FR-065-01: every change names prior value, new value, acting administrator and
    // time; an application event and a legacy-source claim are told apart, the claim with its source reference.
    [Theory]
    [InlineData(IdentityMode.Entra)]
    [InlineData(IdentityMode.Simulated)]
    public async Task Entries_of_a_record_carry_actor_scheme_action_values_outcome_time_and_provenance(IdentityMode mode)
    {
        await using var db = await server.CreateDatabaseAsync();
        var callers = await TestCallers.RecordAsync(db, mode);
        var seed = new Seed(db);
        var mentorship = await seed.FormedMentorshipAsync();
        var scheme = mode == IdentityMode.Entra ? IdentityScheme.Entra : IdentityScheme.Simulated;
        await using (var context = db.Context(Login.App))
        {
            var trail = new AuditTrail(context);
            var administrator = Actors.Administrator(mentorship.Administrator, scheme);
            var relationship = await context.MentorshipRelationships.SingleAsync(row => row.Id == mentorship.Relationship, Cancellation);
            relationship.State = RelationshipState.OnHold;
            relationship.IsActive = ActiveFlag.Y;
            relationship.Version++;
            // One entry per attribute (ADR-006).
            trail.Append(administrator, new AuditEvent(AuditAction.AttributeChanged, AuditRecordType.Relationship, relationship.Id)
            {
                PriorValues = new AuditValues().Set("state", RelationshipState.InProgress),
                NewValues = new AuditValues().Set("state", RelationshipState.OnHold),
            });
            trail.Append(administrator, new AuditEvent(AuditAction.AttributeChanged, AuditRecordType.Relationship, relationship.Id)
            {
                PriorValues = new AuditValues().Set("isActive", ActiveFlag.N),
                NewValues = new AuditValues().Set("isActive", ActiveFlag.Y),
            });
            await context.SaveChangesAsync(Cancellation);
        }

        await seed.AuthoriseLegacyEntryAsync(Login.Alice, mentorship.Administrator);
        var historical = await seed.LegacyRelationshipAsync(Login.Alice, mentorship.Administrator, mentorship.Protege, mentorship.Mentor, sourceRow: 8);
        await db.ExecuteAsync(Login.Alice, """
            INSERT INTO mentor.audit_entry (id, actor_employee_id, identity_scheme, action, record_type, record_id, provenance, source_file, source_row)
            VALUES (gen_random_uuid(), $1, 'DATABASE', 'LEGACY_ENTRY', 'RELATIONSHIP', $2, 'LEGACY_SOURCE', $3, 8)
            """, mentorship.Administrator, historical, Seed.SourceFile);
        await using var api = Start(db, mode.ToString());

        var digital = (await GetAsync(api, RecordRoute(AuditRecordType.Relationship, mentorship.Relationship), As(callers, Profile.Administrator))).EnumerateArray().ToList();
        var claims = (await GetAsync(api, RecordRoute(AuditRecordType.Relationship, historical), As(callers, Profile.Administrator))).EnumerateArray().ToList();

        Assert.Equal(2, digital.Count);
        var changedAt = await db.ScalarAsync<DateTime>(Login.App, "SELECT updated_at FROM mentor.mentorship_relationship WHERE id = $1", mentorship.Relationship);
        foreach (var entry in digital)
        {
            Assert.Equal(mentorship.Administrator, entry.GetProperty("actor").GetProperty("employeeId").GetGuid());
            Assert.Equal("Talent Management", entry.GetProperty("actor").GetProperty("displayName").GetString());
            Assert.Equal(scheme.ToString(), entry.GetProperty("actor").GetProperty("identityScheme").GetString());
            Assert.Equal("AttributeChanged", entry.GetProperty("action").GetString());
            Assert.Equal("Relationship", entry.GetProperty("recordType").GetString());
            Assert.Equal(mentorship.Relationship, entry.GetProperty("recordId").GetGuid());
            Assert.Equal("Application", entry.GetProperty("provenance").GetString());
            Assert.Equal(JsonValueKind.Null, entry.GetProperty("sourceFile").ValueKind);
            Assert.Equal(changedAt, entry.GetProperty("occurredAt").GetDateTimeOffset().UtcDateTime);
        }

        var state = Assert.Single(digital, entry => entry.GetProperty("newValues").TryGetProperty("state", out _));
        Assert.Equal(("IN_PROGRESS", "ON_HOLD"), (state.GetProperty("priorValues").GetProperty("state").GetString(), state.GetProperty("newValues").GetProperty("state").GetString()));
        var active = Assert.Single(digital, entry => entry.GetProperty("newValues").TryGetProperty("isActive", out _));
        Assert.Equal(("N", "Y"), (active.GetProperty("priorValues").GetProperty("isActive").GetString(), active.GetProperty("newValues").GetProperty("isActive").GetString()));

        var claim = Assert.Single(claims);
        Assert.Equal("LegacySource", claim.GetProperty("provenance").GetString());
        Assert.Equal("Database", claim.GetProperty("actor").GetProperty("identityScheme").GetString());
        Assert.Equal("LegacyEntry", claim.GetProperty("action").GetString());
        Assert.Equal((Seed.SourceFile, 8), (claim.GetProperty("sourceFile").GetString(), claim.GetProperty("sourceRow").GetInt32()));
    }

    // AC-FR-028-01, AC-FR-028-02, AC-FR-069-01: an administrator tells the current acceptance of the active draft from
    // the retained acceptances and decision of the draft it replaced, each with actor, time and exact content reference,
    // and reads the reset that made them inapplicable without any of them becoming effective again.
    [Fact]
    public async Task A_candidates_evidence_tells_current_acceptances_and_decisions_from_superseded_ones()
    {
        await using var db = await server.CreateDatabaseAsync();
        var callers = await TestCallers.RecordAsync(db, IdentityMode.Entra);
        var seed = new Seed(db);
        var protege = await seed.EmployeeAsync("Protégé");
        var mentor = await seed.EmployeeAsync("Mentor");
        var administrator = await seed.EmployeeAsync("Talent Management");
        var candidate = await seed.CandidateAsync(await seed.RequestAsync(protege), protege, mentor);
        var first = await seed.RevisionAsync(candidate, protege);
        var protegeAccepted = await seed.AcceptanceAsync(first, protege);
        var mentorAccepted = await seed.AcceptanceAsync(first, mentor);
        var rejected = await seed.FormationDecisionAsync(candidate, first, administrator, "REJECTED");
        var second = await seed.RevisionAsync(candidate, mentor, number: 2, parent: first.Id);
        await using (var context = db.Context(Login.App))
        {
            var trail = new AuditTrail(context);
            trail.Append(Actors.Employee(mentor), new AuditEvent(AuditAction.RevisionCreated, AuditRecordType.AgreementRevision, second.Id)
            {
                CandidateId = candidate,
                Revision = new RevisionReference(second.Id, second.ContentHash),
                Outcome = AuditOutcome.ResetToStep1,
                Invalidated = await trail.InapplicableEvidenceAsync(first.Id, Cancellation),
            });
            await context.SaveChangesAsync(Cancellation);
        }

        var fresh = await seed.AcceptanceAsync(second, protege);
        await using var api = Start(db);

        var evidence = await GetAsync(api, CandidateRoute(candidate), As(callers, Profile.Administrator));

        Assert.Equal("Drafting", evidence.GetProperty("status").GetString());
        Assert.Equal(second.Id, evidence.GetProperty("currentRevisionId").GetGuid());
        Assert.Collection(evidence.GetProperty("revisions").EnumerateArray(),
            revision => Assert.Equal((first.Id, 1, first.ContentHash, false), Revision(revision)),
            revision => Assert.Equal((second.Id, 2, second.ContentHash, true), Revision(revision)));
        Assert.Collection(evidence.GetProperty("acceptances").EnumerateArray(),
            acceptance => Assert.Equal((protegeAccepted, first.Id, 1, first.ContentHash, protege, "Superseded"), Acceptance(acceptance)),
            acceptance => Assert.Equal((mentorAccepted, first.Id, 1, first.ContentHash, mentor, "Superseded"), Acceptance(acceptance)),
            acceptance => Assert.Equal((fresh, second.Id, 2, second.ContentHash, protege, "Applicable"), Acceptance(acceptance)));
        Assert.All(evidence.GetProperty("acceptances").EnumerateArray(), acceptance => Assert.NotEqual(default, acceptance.GetProperty("acceptedAt").GetDateTimeOffset()));
        var decision = Assert.Single(evidence.GetProperty("decisions").EnumerateArray());
        Assert.Equal((rejected, first.Id, 1, "Rejected", administrator, "Superseded"),
            (decision.GetProperty("id").GetGuid(), decision.GetProperty("revisionId").GetGuid(), decision.GetProperty("revisionNumber").GetInt32(),
                decision.GetProperty("outcome").GetString(), decision.GetProperty("administratorEmployeeId").GetGuid(), decision.GetProperty("applicability").GetString()));
        var reset = Assert.Single(evidence.GetProperty("entries").EnumerateArray());
        Assert.Equal(("RevisionCreated", "RESET_TO_STEP_1", mentor, second.Id, second.ContentHash),
            (reset.GetProperty("action").GetString(), reset.GetProperty("outcome").GetString(), reset.GetProperty("actor").GetProperty("employeeId").GetGuid(),
                reset.GetProperty("revisionId").GetGuid(), reset.GetProperty("contentHash").GetString()));
        Assert.Equal([protegeAccepted, mentorAccepted], reset.GetProperty("invalidatedAcceptanceIds").EnumerateArray().Select(id => id.GetGuid()));
        Assert.Equal([rejected], reset.GetProperty("invalidatedDecisionIds").EnumerateArray().Select(id => id.GetGuid()));
        // The content itself is the draft's to show; the trail names it by its hash.
        Assert.DoesNotContain("\"content\"", evidence.GetRawText());
    }

    // AC-FR-028-02: applicability is derived from the head the view was read with. A replacement committed while the
    // evidence is being read (the mentor's second revision made current, accepted by them and rejected by the
    // administrator, in one transaction) is seen whole or not at all: never a head that still names the first revision
    // beside evidence of the second, and never an acceptance of a revision the view lacks.
    [Theory]
    [InlineData("mentor_selection")] // committed once the head is read, before the revisions
    [InlineData("agreement_revision")] // committed once the revisions are read, before the acceptances and decisions
    public async Task A_replacement_committed_while_the_evidence_is_read_is_seen_whole_or_not_at_all(string afterReading)
    {
        await using var db = await server.CreateDatabaseAsync();
        var callers = await TestCallers.RecordAsync(db, IdentityMode.Entra);
        var seed = new Seed(db);
        var protege = await seed.EmployeeAsync("Protégé");
        var mentor = await seed.EmployeeAsync("Mentor");
        var administrator = await seed.EmployeeAsync("Talent Management");
        var candidate = await seed.CandidateAsync(await seed.RequestAsync(protege), protege, mentor);
        var first = await seed.RevisionAsync(candidate, protege);
        var protegeAccepted = await seed.AcceptanceAsync(first, protege);
        var replacement = new ConcurrentReplacement(db, candidate, first, mentor, administrator, afterReading);
        await using var api = Start(db, testServices: services =>
            services.ConfigureDbContext<MentorDbContext>(options => options.AddInterceptors(replacement)));

        var during = await GetAsync(api, CandidateRoute(candidate), As(callers, Profile.Administrator));
        var second = replacement.Second;
        var after = await GetAsync(api, CandidateRoute(candidate), As(callers, Profile.Administrator));

        // The view read while the replacement committed is the state before it, whole.
        Assert.Equal(first.Id, during.GetProperty("currentRevisionId").GetGuid());
        Assert.Equal((first.Id, 1, first.ContentHash, true), Revision(Assert.Single(during.GetProperty("revisions").EnumerateArray())));
        Assert.Equal((protegeAccepted, first.Id, 1, first.ContentHash, protege, "Applicable"), Acceptance(Assert.Single(during.GetProperty("acceptances").EnumerateArray())));
        Assert.Empty(during.GetProperty("decisions").EnumerateArray());

        // The next view is the state after it, whole: the replacement was committed, not lost.
        Assert.Equal(second.Id, after.GetProperty("currentRevisionId").GetGuid());
        Assert.Collection(after.GetProperty("revisions").EnumerateArray(),
            revision => Assert.Equal((first.Id, 1, first.ContentHash, false), Revision(revision)),
            revision => Assert.Equal((second.Id, 2, second.ContentHash, true), Revision(revision)));
        Assert.Collection(after.GetProperty("acceptances").EnumerateArray(),
            acceptance => Assert.Equal((protegeAccepted, first.Id, 1, first.ContentHash, protege, "Superseded"), Acceptance(acceptance)),
            acceptance => Assert.Equal((replacement.MentorAccepted, second.Id, 2, second.ContentHash, mentor, "Applicable"), Acceptance(acceptance)));
        var rejection = Assert.Single(after.GetProperty("decisions").EnumerateArray());
        Assert.Equal((replacement.Rejected, second.Id, 2, "Rejected", "Applicable"),
            (rejection.GetProperty("id").GetGuid(), rejection.GetProperty("revisionId").GetGuid(), rejection.GetProperty("revisionNumber").GetInt32(),
                rejection.GetProperty("outcome").GetString(), rejection.GetProperty("applicability").GetString()));
    }

    // AC-FR-033-01, AC-FR-065-01: formation evidence stays as it was after the relationship's attributes change, and
    // a historical row has a legacy-source claim and no digital formation evidence.
    [Fact]
    public async Task Formation_evidence_outlives_later_attribute_changes_and_a_legacy_row_has_none()
    {
        await using var db = await server.CreateDatabaseAsync();
        var callers = await TestCallers.RecordAsync(db, IdentityMode.Entra);
        var seed = new Seed(db);
        var mentorship = await seed.FormedMentorshipAsync();
        await using (var context = db.Context(Login.App))
        {
            var relationship = await context.MentorshipRelationships.SingleAsync(row => row.Id == mentorship.Relationship, Cancellation);
            relationship.State = RelationshipState.Complete;
            relationship.Version++;
            new AuditTrail(context).Append(Actors.Administrator(mentorship.Administrator), new AuditEvent(AuditAction.AttributeChanged, AuditRecordType.Relationship, relationship.Id)
            {
                PriorValues = new AuditValues().Set("state", RelationshipState.InProgress),
                NewValues = new AuditValues().Set("state", RelationshipState.Complete),
            });
            await context.SaveChangesAsync(Cancellation);
        }

        await seed.AuthoriseLegacyEntryAsync(Login.Alice, mentorship.Administrator);
        var historical = await seed.LegacyRelationshipAsync(Login.Alice, mentorship.Administrator, mentorship.Protege, mentorship.Mentor, sourceRow: 8);
        await db.ExecuteAsync(Login.Alice, """
            INSERT INTO mentor.audit_entry (id, actor_employee_id, identity_scheme, action, record_type, record_id, provenance, source_file, source_row)
            VALUES (gen_random_uuid(), $1, 'DATABASE', 'LEGACY_ENTRY', 'RELATIONSHIP', $2, 'LEGACY_SOURCE', $3, 8)
            """, mentorship.Administrator, historical, Seed.SourceFile);
        await using var api = Start(db);

        var evidence = await GetAsync(api, CandidateRoute(mentorship.Candidate), As(callers, Profile.Administrator));
        var claims = (await GetAsync(api, RecordRoute(AuditRecordType.Relationship, historical), As(callers, Profile.Administrator))).EnumerateArray().ToList();

        Assert.Equal("Formed", evidence.GetProperty("status").GetString());
        var approval = Assert.Single(evidence.GetProperty("decisions").EnumerateArray());
        Assert.Equal(("Approved", "Formed", mentorship.Revision.ContentHash),
            (approval.GetProperty("outcome").GetString(), approval.GetProperty("applicability").GetString(), approval.GetProperty("contentHash").GetString()));
        Assert.Equal(2, evidence.GetProperty("acceptances").GetArrayLength());
        Assert.All(evidence.GetProperty("acceptances").EnumerateArray(), acceptance => Assert.Equal("Formed", acceptance.GetProperty("applicability").GetString()));
        Assert.Equal(mentorship.Revision.Content, await db.ScalarAsync<string>(Login.App, "SELECT content FROM mentor.formed_agreement WHERE relationship_id = $1", mentorship.Relationship));
        Assert.Equal("COMPLETE", await db.ScalarAsync<string>(Login.App, "SELECT state FROM mentor.mentorship_relationship WHERE id = $1", mentorship.Relationship));

        var claim = Assert.Single(claims);
        Assert.Equal(("LegacySource", "LegacyEntry"), (claim.GetProperty("provenance").GetString(), claim.GetProperty("action").GetString()));
        Assert.Equal(JsonValueKind.Null, claim.GetProperty("candidateId").ValueKind);
        Assert.Equal(0L, await db.ScalarAsync<long>(Login.App, "SELECT count(*) FROM mentor.formation_decision WHERE candidate_id IN (SELECT candidate_id FROM mentor.mentorship_relationship WHERE id = $1)", historical));
    }

    [Fact]
    public async Task Entries_of_a_record_type_are_listed_when_the_action_names_no_single_record()
    {
        await using var db = await server.CreateDatabaseAsync();
        var callers = await TestCallers.RecordAsync(db, IdentityMode.Entra);
        var mentorship = await new Seed(db).FormedMentorshipAsync();
        await using (var context = db.Context(Login.App))
        {
            new AuditTrail(context).Append(Actors.Administrator(mentorship.Administrator), new AuditEvent(AuditAction.RegisterExported, AuditRecordType.Register, null)
            {
                NewValues = new AuditValues().Set("format", "CSV"),
            });
            await context.SaveChangesAsync(Cancellation);
        }

        await using var api = Start(db);

        var export = Assert.Single((await GetAsync(api, RecordTypeRoute(AuditRecordType.Register), As(callers, Profile.Administrator))).EnumerateArray());
        Assert.Equal(("RegisterExported", "Register", JsonValueKind.Null), (export.GetProperty("action").GetString(), export.GetProperty("recordType").GetString(), export.GetProperty("recordId").ValueKind));
        Assert.Empty((await GetAsync(api, RecordTypeRoute(AuditRecordType.Goal), As(callers, Profile.Administrator))).EnumerateArray());
    }

    [Theory]
    [InlineData("Employee")]
    [InlineData("99")]
    public async Task A_record_type_the_trail_does_not_hold_is_refused(string recordType)
    {
        await using var db = await server.CreateDatabaseAsync();
        var callers = await TestCallers.RecordAsync(db, IdentityMode.Entra);
        await using var api = Start(db);

        await ProblemAssert.IsProblem(HttpStatusCode.BadRequest, await SendAsync(api, $"/api/audit-trail/records/{recordType}", As(callers, Profile.Administrator)));
        await ProblemAssert.IsProblem(HttpStatusCode.BadRequest, await SendAsync(api, $"/api/audit-trail/records/{recordType}/{Guid.NewGuid()}", As(callers, Profile.Administrator)));
    }

    [Fact]
    public async Task A_candidate_that_does_not_exist_is_not_found()
    {
        await using var db = await server.CreateDatabaseAsync();
        var callers = await TestCallers.RecordAsync(db, IdentityMode.Entra);
        await using var api = Start(db);

        await ProblemAssert.IsProblem(HttpStatusCode.NotFound, await SendAsync(api, CandidateRoute(Guid.NewGuid()), As(callers, Profile.Administrator)));
    }

    private static string RecordTypeRoute(AuditRecordType recordType) => $"/api/audit-trail/records/{recordType}";

    private static string RecordRoute(AuditRecordType recordType, Guid recordId) => $"/api/audit-trail/records/{recordType}/{recordId}";

    private static string CandidateRoute(Guid candidateId) => $"/api/audit-trail/candidates/{candidateId}";

    private static async Task<JsonElement> GetAsync(ApiFactory api, string route, Action<HttpRequestMessage> signIn)
    {
        var response = await SendAsync(api, route, signIn);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
    }

    private static (Guid Id, int Number, string Hash, bool Current) Revision(JsonElement revision) =>
        (revision.GetProperty("id").GetGuid(), revision.GetProperty("revisionNumber").GetInt32(), revision.GetProperty("contentHash").GetString()!, revision.GetProperty("isCurrent").GetBoolean());

    private static (Guid Id, Guid Revision, int Number, string Hash, Guid Participant, string Applicability) Acceptance(JsonElement acceptance) =>
        (acceptance.GetProperty("id").GetGuid(), acceptance.GetProperty("revisionId").GetGuid(), acceptance.GetProperty("revisionNumber").GetInt32(),
            acceptance.GetProperty("contentHash").GetString()!, acceptance.GetProperty("participantEmployeeId").GetGuid(), acceptance.GetProperty("applicability").GetString()!);
}

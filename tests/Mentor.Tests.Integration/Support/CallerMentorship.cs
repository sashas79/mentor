using Mentor.Core.Agreements;
using Mentor.Tests.Integration.Persistence;

namespace Mentor.Tests.Integration.Support;

/// <summary>
/// Candidates and relationships between the callers of <see cref="TestCallers"/>: the Employee caller as protégé, the
/// Mentor caller as mentor, the Administrator caller as Talent Management. The Applicant and Newcomer callers have no
/// part in them.
/// </summary>
internal static class CallerMentorship
{
    public const int AgreedFrequencyWeeks = 2;
    public const int AgreedDurationMinutes = 45;

    /// <summary>A content document whose terms carry the meeting interval and duration (CONTRACT-AGREEMENT-CONTENT).</summary>
    public static string Content(int revision = 1) =>
        $$"""
        {"parties":{"protege":"P","mentor":"M"},"terms":{"durationCategory":"SIX_MONTHS","{{AgreementMeetingTerms.MeetingIntervalWeeksProperty}}":{{AgreedFrequencyWeeks}},"{{AgreementMeetingTerms.ApproximateMinutesPerMeetingProperty}}":{{AgreedDurationMinutes}}},"goals":["Lead a design review"],"revision":{{revision}}}
        """;

    /// <summary>A candidate in <paramref name="status"/>, on a request of its own.</summary>
    public static async Task<Guid> CandidateAsync(TestDatabase db, TestCallers callers, string status = "ACCEPTED")
    {
        var seed = new Seed(db);
        var protege = callers.EmployeeId(Profile.Employee)!.Value;
        var request = await seed.RequestAsync(protege);
        return await seed.CandidateAsync(request, protege, callers.EmployeeId(Profile.Mentor)!.Value, status);
    }

    /// <summary>A relationship formed on an agreement with meeting terms, with one goal, exactly as formation leaves it (ADR-005).</summary>
    public static async Task<FormedMentorship> FormedAsync(TestDatabase db, TestCallers callers)
    {
        var seed = new Seed(db);
        var protege = callers.EmployeeId(Profile.Employee)!.Value;
        var mentor = callers.EmployeeId(Profile.Mentor)!.Value;
        var administrator = callers.EmployeeId(Profile.Administrator)!.Value;
        var request = await seed.RequestAsync(protege);
        var candidate = await seed.CandidateAsync(request, protege, mentor, "DRAFTING");
        var content = Content();
        var revisionId = await db.ScalarAsync<Guid>(Login.Owner, """
            INSERT INTO mentor.agreement_revision (id, candidate_id, revision_number, parent_revision_id, content, content_hash, created_by_employee_id)
            VALUES (gen_random_uuid(), $1, 1, NULL, $2, $3, $4)
            RETURNING id
            """, candidate, content, AgreementContentHash.Compute(content), protege);
        await db.ExecuteAsync(Login.Owner, "UPDATE mentor.mentor_selection SET current_revision_id = $1 WHERE id = $2", revisionId, candidate);
        var revision = new Revision(revisionId, content, AgreementContentHash.Compute(content));
        await seed.AcceptanceAsync(revision, protege);
        await seed.AcceptanceAsync(revision, mentor);
        await seed.FormationDecisionAsync(candidate, revision, administrator);
        await db.ExecuteAsync(Login.Owner, "UPDATE mentor.mentor_selection SET status = 'FORMED' WHERE id = $1", candidate);
        var relationship = await seed.DigitalRelationshipAsync(candidate, protege, mentor);
        var formedAgreement = await seed.FormedAgreementAsync(relationship, candidate, revision);
        var goal = await seed.GoalAsync(relationship, formedAgreement);
        return new FormedMentorship(protege, mentor, administrator, request, candidate, revision, relationship, formedAgreement, goal);
    }

    /// <summary>A historical relationship of the same two callers, entered by the Administrator caller under the legacy entry role; it has no formed agreement.</summary>
    public static async Task<Guid> LegacyAsync(TestDatabase db, TestCallers callers)
    {
        var seed = new Seed(db);
        var administrator = callers.EmployeeId(Profile.Administrator)!.Value;
        await seed.AuthoriseLegacyEntryAsync(Login.Alice, administrator);
        return await seed.LegacyRelationshipAsync(Login.Alice, administrator,
            callers.EmployeeId(Profile.Employee)!.Value, callers.EmployeeId(Profile.Mentor)!.Value, sourceRow: 3, state: "IN_PROGRESS");
    }
}

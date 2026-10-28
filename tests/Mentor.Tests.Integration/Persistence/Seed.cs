using Mentor.Core.Agreements;

namespace Mentor.Tests.Integration.Persistence;

internal sealed record Revision(Guid Id, string Content, string ContentHash);

/// <summary>A digitally formed mentorship with one goal, exactly as formation leaves it (ADR-005).</summary>
internal sealed record FormedMentorship(
    Guid Protege,
    Guid Mentor,
    Guid Administrator,
    Guid Request,
    Guid Candidate,
    Revision Revision,
    Guid Relationship,
    Guid FormedAgreement,
    Guid Goal);

/// <summary>
/// Writes valid rows, as the owner unless told otherwise, so that a test can then probe one rule at a time. The owner
/// is bound by every rule except privileges.
/// </summary>
internal sealed class Seed(TestDatabase db)
{
    public const string SourceFile = "Mentorship Tracker 2026.xlsx";

    public static string Content(int revision = 1) =>
        $$"""{"parties":{"protege":"P","mentor":"M"},"terms":{"durationCategory":"SIX_MONTHS"},"goals":["Lead a design review"],"revision":{{revision}}}""";

    public Task<Guid> EmployeeAsync(string displayName = "Employee") =>
        db.ScalarAsync<Guid>(Login.Owner,
            "INSERT INTO mentor.employee (id, entra_object_id, display_name) VALUES (gen_random_uuid(), gen_random_uuid(), $1) RETURNING id",
            displayName);

    public Task<Guid> RequestAsync(Guid protege) =>
        db.ScalarAsync<Guid>(Login.Owner, """
            INSERT INTO mentor.mentorship_request (id, protege_employee_id, motivation, development_goals, nontechnical_expectations, disposition)
            VALUES (gen_random_uuid(), $1, 'To grow as an engineer', 'Lead a design review', 'Confidence', 'OPEN')
            RETURNING id
            """, protege);

    /// <summary>A candidate in <paramref name="status"/>, with the decisions that status requires (ADR-015).</summary>
    public Task<Guid> CandidateAsync(Guid request, Guid protege, Guid mentor, string status = "DRAFTING")
    {
        var (response, responded, willing) = status switch
        {
            "INVITED" => ("PENDING", false, false),
            "DECLINED" => ("DECLINED", true, false),
            "ACCEPTED" => ("ACCEPTED", true, false),
            "DRAFTING" => ("ACCEPTED", true, true),
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
        return db.ScalarAsync<Guid>(Login.Owner, """
            INSERT INTO mentor.mentor_selection (id, request_id, protege_employee_id, mentor_employee_id, status, mentor_response,
                mentor_responded_at, protege_willing_at, mentor_willing_at)
            VALUES (gen_random_uuid(), $1, $2, $3, $4, $5, CASE WHEN $6 THEN now() END, CASE WHEN $7 THEN now() END, CASE WHEN $7 THEN now() END)
            RETURNING id
            """, request, protege, mentor, status, response, responded, willing);
    }

    public async Task<Revision> RevisionAsync(Guid candidate, Guid author, int number = 1, Guid? parent = null, bool makeCurrent = true)
    {
        var content = Content(number);
        var hash = AgreementContentHash.Compute(content);
        var id = await db.ScalarAsync<Guid>(Login.Owner, """
            INSERT INTO mentor.agreement_revision (id, candidate_id, revision_number, parent_revision_id, content, content_hash, created_by_employee_id)
            VALUES (gen_random_uuid(), $1, $2, $3, $4, $5, $6)
            RETURNING id
            """, candidate, number, parent, content, hash, author);
        if (makeCurrent)
        {
            await db.ExecuteAsync(Login.Owner, "UPDATE mentor.mentor_selection SET current_revision_id = $1 WHERE id = $2", id, candidate);
        }

        return new Revision(id, content, hash);
    }

    public Task<Guid> AcceptanceAsync(Revision revision, Guid participant) =>
        db.ScalarAsync<Guid>(Login.Owner, """
            INSERT INTO mentor.agreement_acceptance (id, revision_id, content_hash, participant_employee_id)
            VALUES (gen_random_uuid(), $1, $2, $3)
            RETURNING id
            """, revision.Id, revision.ContentHash, participant);

    public Task<Guid> FormationDecisionAsync(Guid candidate, Revision revision, Guid administrator, string outcome = "APPROVED") =>
        db.ScalarAsync<Guid>(Login.Owner, """
            INSERT INTO mentor.formation_decision (id, candidate_id, revision_id, content_hash, outcome, administrator_employee_id)
            VALUES (gen_random_uuid(), $1, $2, $3, $4, $5)
            RETURNING id
            """, candidate, revision.Id, revision.ContentHash, outcome, administrator);

    public Task<Guid> DigitalRelationshipAsync(Guid candidate, Guid protege, Guid mentor) =>
        db.ScalarAsync<Guid>(Login.Owner, """
            INSERT INTO mentor.mentorship_relationship (id, provenance, candidate_id, protege_employee_id, mentor_employee_id,
                state, status, is_active, duration_category)
            VALUES (gen_random_uuid(), 'DIGITAL', $1, $2, $3, 'IN_PROGRESS', 'BLANK', 'N', 'SIX_MONTHS')
            RETURNING id
            """, candidate, protege, mentor);

    public Task<Guid> FormedAgreementAsync(Guid relationship, Guid candidate, Revision revision) =>
        db.ScalarAsync<Guid>(Login.Owner, """
            INSERT INTO mentor.formed_agreement (id, relationship_id, candidate_id, revision_id, content, content_hash)
            VALUES (gen_random_uuid(), $1, $2, $3, $4, $5)
            RETURNING id
            """, relationship, candidate, revision.Id, revision.Content, revision.ContentHash);

    public Task<Guid> GoalAsync(Guid relationship, Guid formedAgreement, int position = 1) =>
        db.ScalarAsync<Guid>(Login.Owner, """
            INSERT INTO mentor.development_goal (id, relationship_id, formed_agreement_id, position, agreed_description, completion_status)
            VALUES (gen_random_uuid(), $1, $2, $3, 'Lead a design review', 'INCOMPLETE')
            RETURNING id
            """, relationship, formedAgreement, position);

    public Task<Guid> ProgressAsync(Guid goal, Guid relationship, Guid protege, bool completionRequested = true) =>
        db.ScalarAsync<Guid>(Login.Owner, """
            INSERT INTO mentor.goal_progress (id, goal_id, relationship_id, protege_employee_id, progress_update, completion_requested)
            VALUES (gen_random_uuid(), $1, $2, $3, 'Ran the first design review', $4)
            RETURNING id
            """, goal, relationship, protege, completionRequested);

    public Task<Guid> ApprovalAsync(Guid progress, Guid relationship, Guid mentor, string decision = "APPROVED") =>
        db.ScalarAsync<Guid>(Login.Owner, """
            INSERT INTO mentor.goal_approval (id, progress_id, relationship_id, completion_requested, mentor_employee_id, decision)
            VALUES (gen_random_uuid(), $1, $2, true, $3, $4)
            RETURNING id
            """, progress, relationship, mentor, decision);

    public async Task<FormedMentorship> FormedMentorshipAsync()
    {
        var protege = await EmployeeAsync("Protégé");
        var mentor = await EmployeeAsync("Mentor");
        var administrator = await EmployeeAsync("Talent Management");
        var request = await RequestAsync(protege);
        var candidate = await CandidateAsync(request, protege, mentor);
        var revision = await RevisionAsync(candidate, protege);
        await AcceptanceAsync(revision, protege);
        await AcceptanceAsync(revision, mentor);
        await FormationDecisionAsync(candidate, revision, administrator);
        await db.ExecuteAsync(Login.Owner, "UPDATE mentor.mentor_selection SET status = 'FORMED' WHERE id = $1", candidate);
        var relationship = await DigitalRelationshipAsync(candidate, protege, mentor);
        var formedAgreement = await FormedAgreementAsync(relationship, candidate, revision);
        var goal = await GoalAsync(relationship, formedAgreement);
        return new FormedMentorship(protege, mentor, administrator, request, candidate, revision, relationship, formedAgreement, goal);
    }

    /// <summary>Maps the account of <paramref name="login"/> to <paramref name="employee"/>, as that account (ADR-008).</summary>
    public Task<Guid> AuthoriseLegacyEntryAsync(Login login, Guid employee) =>
        db.ScalarAsync<Guid>(login,
            "INSERT INTO mentor.legacy_entry_authorisation (id, employee_id, database_account) VALUES (gen_random_uuid(), $1, session_user) RETURNING id",
            employee);

    /// <summary>A historical relationship with State, Status and IsActive unresolved unless <paramref name="state"/> is given.</summary>
    public Task<Guid> LegacyRelationshipAsync(Login login, Guid enteredBy, Guid protege, Guid mentor, int sourceRow, string? state = null) =>
        db.ScalarAsync<Guid>(login, """
            INSERT INTO mentor.mentorship_relationship (id, provenance, protege_employee_id, mentor_employee_id, state, status, is_active,
                source_file, source_row, source_mentor_text, source_protege_text, source_active_text, source_duration_text,
                source_start_text, source_end_text, entered_by_employee_id)
            VALUES (gen_random_uuid(), 'LEGACY', $1, $2, $3, NULL, NULL, $4, $5, 'M. Mentor', 'P. Protégé', 'Successfully complete',
                'ongoing', '10/1/2026', 'ongoing', $6)
            RETURNING id
            """, protege, mentor, state, SourceFile, sourceRow, enteredBy);

    public Task<Guid> LegacyReviewExceptionAsync(Login login, Guid enteredBy, int sourceRow, string reason, Guid? relationship = null, string? attribute = null) =>
        db.ScalarAsync<Guid>(login, """
            INSERT INTO mentor.legacy_review_exception (id, source_file, source_row, reason, relationship_id, attribute,
                source_mentor_text, source_active_text, entered_by_employee_id)
            VALUES (gen_random_uuid(), $1, $2, $3, $4, $5, 'J. Unknown', 'Successfully complete', $6)
            RETURNING id
            """, SourceFile, sourceRow, reason, relationship, attribute, enteredBy);

    /// <summary>At least one row in every table of the schema.</summary>
    public async Task<FormedMentorship> EveryTableAsync()
    {
        var mentorship = await FormedMentorshipAsync();
        await db.ExecuteAsync(Login.Owner,
            "INSERT INTO mentor.mentor_profile (id, employee_id, biography, approval_status) VALUES (gen_random_uuid(), $1, 'Ten years of delivery', 'PENDING')",
            mentorship.Mentor);
        await db.ExecuteAsync(Login.Owner, """
            INSERT INTO mentor.permitted_change (id, candidate_id, actor_employee_id, basis, field, prior_value, new_value)
            VALUES (gen_random_uuid(), $1, $2, 'ADMINISTRATIVE_REFERENCE', 'ADMIN_REFERENCE', NULL, 'TM-2026-001')
            """, mentorship.Candidate, mentorship.Administrator);
        var progress = await ProgressAsync(mentorship.Goal, mentorship.Relationship, mentorship.Protege);
        await ApprovalAsync(progress, mentorship.Relationship, mentorship.Mentor);
        await db.ExecuteAsync(Login.Owner, """
            INSERT INTO mentor.meeting (id, kind, relationship_id, arranged_outside_application, frequency_weeks, duration_minutes, created_by_employee_id)
            VALUES (gen_random_uuid(), 'RECURRING_SESSION', $1, false, 2, 60, $2)
            """, mentorship.Relationship, mentorship.Protege);
        await db.ExecuteAsync(Login.Owner, """
            INSERT INTO mentor.notification_event (id, event_type, subject_type, subject_id, recipient_employee_id, recipient_role, required_action, delivery)
            VALUES (gen_random_uuid(), 'FORMATION_DECIDED', 'CANDIDATE', $1, $2, 'PROTEGE', 'NONE', 'SIMULATED')
            """, mentorship.Candidate, mentorship.Protege);
        await db.ExecuteAsync(Login.Owner, """
            INSERT INTO mentor.audit_entry (id, actor_employee_id, identity_scheme, action, record_type, record_id, candidate_id,
                revision_id, content_hash, outcome, provenance)
            VALUES (gen_random_uuid(), $1, 'SIMULATED', 'FORMATION_DECIDED', 'CANDIDATE', $2, $2, $3, $4, 'APPROVED', 'APPLICATION')
            """, mentorship.Administrator, mentorship.Candidate, mentorship.Revision.Id, mentorship.Revision.ContentHash);
        await AuthoriseLegacyEntryAsync(Login.Owner, mentorship.Administrator);
        var legacy = await LegacyRelationshipAsync(Login.Owner, mentorship.Administrator, mentorship.Protege, mentorship.Mentor, sourceRow: 8);
        await LegacyReviewExceptionAsync(Login.Owner, mentorship.Administrator, 8, "UNRESOLVED_VALUE", legacy, "STATE");
        return mentorship;
    }
}

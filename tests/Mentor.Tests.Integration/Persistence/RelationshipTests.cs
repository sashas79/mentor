using Npgsql;

namespace Mentor.Tests.Integration.Persistence;

// Formed relationships and their attributes (ADR-005, ADR-006; DR-005, FR-043 to FR-046, FR-054).
public sealed class RelationshipTests(PostgresServer postgres)
{
    [Theory]
    [InlineData("state")]
    [InlineData("status")]
    [InlineData("is_active")]
    public async Task A_digital_relationship_always_holds_state_status_and_is_active(string attribute)
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var mentorship = await new Seed(db).FormedMentorshipAsync();

        await Rejected.WithAsync(PostgresErrorCodes.CheckViolation,
            () => db.ExecuteAsync(Login.App, $"UPDATE mentor.mentorship_relationship SET {attribute} = NULL WHERE id = $1", mentorship.Relationship),
            "ck_mentorship_relationship_digital_attributes");
    }

    [Theory]
    [InlineData("is_active", "'Yes'")]
    [InlineData("is_active", "'y'")]
    [InlineData("state", "'In Progress'")]
    [InlineData("state", "'CLOSED'")]
    [InlineData("status", "''")]
    [InlineData("status", "'REASSIGNED'")]
    public async Task Each_attribute_holds_only_its_business_values(string attribute, string value)
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var mentorship = await new Seed(db).FormedMentorshipAsync();

        await Rejected.WithAsync(PostgresErrorCodes.CheckViolation,
            () => db.ExecuteAsync(Login.App, $"UPDATE mentor.mentorship_relationship SET {attribute} = {value} WHERE id = $1", mentorship.Relationship),
            $"ck_mentorship_relationship_{attribute}");
    }

    // FR-047 to FR-049, BR-009: the attributes are independent, and no combination is refused.
    [Fact]
    public async Task Any_combination_of_the_attributes_is_accepted()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var mentorship = await new Seed(db).FormedMentorshipAsync();

        await db.ExecuteAsync(Login.App,
            "UPDATE mentor.mentorship_relationship SET state = 'COMPLETE', status = 'NEEDS_REASSIGNING', is_active = 'Y', version = version + 1 WHERE id = $1",
            mentorship.Relationship);
        await db.ExecuteAsync(Login.App,
            "UPDATE mentor.mentorship_relationship SET state = 'IN_PROGRESS', status = 'BLANK', is_active = 'N', version = version + 1 WHERE id = $1",
            mentorship.Relationship);
    }

    [Fact]
    public async Task A_digital_relationship_has_exactly_its_candidates_participants()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var seed = new Seed(db);
        var protege = await seed.EmployeeAsync();
        var mentor = await seed.EmployeeAsync();
        var someoneElse = await seed.EmployeeAsync();
        var candidate = await seed.CandidateAsync(await seed.RequestAsync(protege), protege, mentor);

        await Rejected.WithAsync(PostgresErrorCodes.ForeignKeyViolation, () => seed.DigitalRelationshipAsync(candidate, protege, someoneElse),
            "fk_mentorship_relationship_candidate_participants");
        await Rejected.WithAsync(PostgresErrorCodes.ForeignKeyViolation, () => seed.DigitalRelationshipAsync(candidate, mentor, protege),
            "fk_mentorship_relationship_candidate_participants");
    }

    [Fact]
    public async Task A_candidate_forms_at_most_one_relationship()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var seed = new Seed(db);
        var mentorship = await seed.FormedMentorshipAsync();

        await Rejected.WithAsync(PostgresErrorCodes.UniqueViolation,
            () => seed.DigitalRelationshipAsync(mentorship.Candidate, mentorship.Protege, mentorship.Mentor),
            "ix_mentorship_relationship_candidate_id");
    }

    [Fact]
    public async Task A_digital_relationship_comes_from_a_candidate_and_carries_no_legacy_source()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var seed = new Seed(db);
        var protege = await seed.EmployeeAsync();
        var mentor = await seed.EmployeeAsync();

        await Rejected.WithAsync(PostgresErrorCodes.CheckViolation, () => db.ExecuteAsync(Login.App, """
            INSERT INTO mentor.mentorship_relationship (id, provenance, protege_employee_id, mentor_employee_id, state, status, is_active)
            VALUES (gen_random_uuid(), 'DIGITAL', $1, $2, 'IN_PROGRESS', 'BLANK', 'N')
            """, protege, mentor), "ck_mentorship_relationship_origin");
    }

    // FR-054, AC-FR-054-01: any number of relationships involving the same person, each separately identifiable.
    [Fact]
    public async Task The_same_people_can_share_several_relationships_over_time()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var seed = new Seed(db);
        var mentorship = await seed.FormedMentorshipAsync();
        var secondRequest = await seed.RequestAsync(mentorship.Protege);
        var secondCandidate = await seed.CandidateAsync(secondRequest, mentorship.Protege, mentorship.Mentor);
        await seed.RevisionAsync(secondCandidate, mentorship.Protege);
        await db.ExecuteAsync(Login.Owner, "UPDATE mentor.mentor_selection SET status = 'FORMED' WHERE id = $1", secondCandidate);

        var second = await seed.DigitalRelationshipAsync(secondCandidate, mentorship.Protege, mentorship.Mentor);
        await seed.AuthoriseLegacyEntryAsync(Login.Owner, mentorship.Administrator);
        var historic = await seed.LegacyRelationshipAsync(Login.Owner, mentorship.Administrator, mentorship.Protege, mentorship.Mentor, sourceRow: 3);

        Assert.Equal(3, new[] { mentorship.Relationship, second, historic }.Distinct().Count());
        Assert.Equal(3, await db.ScalarAsync<long>(Login.App,
            "SELECT count(*) FROM mentor.mentorship_relationship WHERE protege_employee_id = $1 AND mentor_employee_id = $2",
            mentorship.Protege, mentorship.Mentor));
    }

    [Fact]
    public async Task A_relationship_has_one_formed_agreement_identical_to_the_revision_it_was_formed_on()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var seed = new Seed(db);
        var mentorship = await seed.FormedMentorshipAsync();
        var altered = Seed.Content(2);

        await Rejected.WithAsync(PostgresErrorCodes.UniqueViolation,
            () => seed.FormedAgreementAsync(mentorship.Relationship, mentorship.Candidate, mentorship.Revision),
            "ix_formed_agreement_relationship_id");
        await Rejected.WithAsync(PostgresErrorCodes.CheckViolation, () => db.ExecuteAsync(Login.App, """
            INSERT INTO mentor.formed_agreement (id, relationship_id, candidate_id, revision_id, content, content_hash)
            VALUES (gen_random_uuid(), $1, $2, $3, $4, $5)
            """, mentorship.Relationship, mentorship.Candidate, mentorship.Revision.Id, altered, mentorship.Revision.ContentHash),
            "ck_formed_agreement_content_hash");
    }

    // ADR-008, AC-DR-008-01: a legacy relationship gains no digital formation evidence.
    [Fact]
    public async Task A_legacy_relationship_cannot_have_a_formed_agreement_or_goals()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var seed = new Seed(db);
        var mentorship = await seed.FormedMentorshipAsync();
        await seed.AuthoriseLegacyEntryAsync(Login.Owner, mentorship.Administrator);
        var legacy = await seed.LegacyRelationshipAsync(Login.Owner, mentorship.Administrator, mentorship.Protege, mentorship.Mentor, sourceRow: 4);

        await Rejected.WithAsync(PostgresErrorCodes.ForeignKeyViolation,
            () => seed.FormedAgreementAsync(legacy, mentorship.Candidate, mentorship.Revision), "fk_formed_agreement_relationship_of_candidate");
        await Rejected.WithAsync(PostgresErrorCodes.ForeignKeyViolation, () => seed.GoalAsync(legacy, mentorship.FormedAgreement, position: 2),
            "fk_development_goal_agreement_of_relationship");
    }
}

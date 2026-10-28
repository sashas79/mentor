using Npgsql;

namespace Mentor.Tests.Integration.Persistence;

// Goals, progress and completion decisions (BR-005; DR-009, DR-010, DR-011).
public sealed class GoalTests(PostgresServer postgres)
{
    [Fact]
    public async Task Only_the_relationships_protege_submits_progress()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var seed = new Seed(db);
        var mentorship = await seed.FormedMentorshipAsync();

        await Rejected.WithAsync(PostgresErrorCodes.ForeignKeyViolation,
            () => seed.ProgressAsync(mentorship.Goal, mentorship.Relationship, mentorship.Mentor), "fk_goal_progress_relationship_protege");
        await Rejected.WithAsync(PostgresErrorCodes.ForeignKeyViolation,
            () => seed.ProgressAsync(mentorship.Goal, mentorship.Relationship, mentorship.Administrator), "fk_goal_progress_relationship_protege");
    }

    [Fact]
    public async Task Progress_is_submitted_against_a_goal_of_the_same_relationship()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var seed = new Seed(db);
        var mentorship = await seed.FormedMentorshipAsync();
        var other = await seed.FormedMentorshipAsync();

        await Rejected.WithAsync(PostgresErrorCodes.ForeignKeyViolation,
            () => seed.ProgressAsync(other.Goal, mentorship.Relationship, mentorship.Protege), "fk_goal_progress_goal_of_relationship");
    }

    // AC-DR-010-01: submitting progress, even with a completion request, does not complete the goal.
    [Fact]
    public async Task A_completion_request_leaves_the_goal_incomplete()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var seed = new Seed(db);
        var mentorship = await seed.FormedMentorshipAsync();

        await seed.ProgressAsync(mentorship.Goal, mentorship.Relationship, mentorship.Protege);

        Assert.Equal("INCOMPLETE", await StatusAsync(db, mentorship.Goal));
    }

    [Fact]
    public async Task Only_the_relationships_mentor_decides_a_completion_request()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var seed = new Seed(db);
        var mentorship = await seed.FormedMentorshipAsync();
        var request = await seed.ProgressAsync(mentorship.Goal, mentorship.Relationship, mentorship.Protege);

        await Rejected.WithAsync(PostgresErrorCodes.ForeignKeyViolation,
            () => seed.ApprovalAsync(request, mentorship.Relationship, mentorship.Protege), "fk_goal_approval_relationship_mentor");
        await Rejected.WithAsync(PostgresErrorCodes.ForeignKeyViolation,
            () => seed.ApprovalAsync(request, mentorship.Relationship, mentorship.Administrator), "fk_goal_approval_relationship_mentor");
    }

    [Fact]
    public async Task A_decision_is_recorded_only_on_a_completion_request_and_only_once()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var seed = new Seed(db);
        var mentorship = await seed.FormedMentorshipAsync();
        var ordinaryProgress = await seed.ProgressAsync(mentorship.Goal, mentorship.Relationship, mentorship.Protege, completionRequested: false);
        var request = await seed.ProgressAsync(mentorship.Goal, mentorship.Relationship, mentorship.Protege);

        await Rejected.WithAsync(PostgresErrorCodes.ForeignKeyViolation,
            () => seed.ApprovalAsync(ordinaryProgress, mentorship.Relationship, mentorship.Mentor), "fk_goal_approval_completion_request");
        await seed.ApprovalAsync(request, mentorship.Relationship, mentorship.Mentor, "REJECTED");
        await Rejected.WithAsync(PostgresErrorCodes.UniqueViolation,
            () => seed.ApprovalAsync(request, mentorship.Relationship, mentorship.Mentor), "ix_goal_approval_progress_id");
    }

    // AC-DR-011-01: a pending or rejected request does not complete the goal; the mentor's approval does.
    [Fact]
    public async Task A_goal_is_complete_only_with_the_mentors_approval()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var seed = new Seed(db);
        var mentorship = await seed.FormedMentorshipAsync();
        const string complete = "UPDATE mentor.development_goal SET completion_status = 'COMPLETE' WHERE id = $1";

        var rejected = await seed.ProgressAsync(mentorship.Goal, mentorship.Relationship, mentorship.Protege);
        await Rejected.WithAsync(PostgresErrorCodes.CheckViolation, () => db.ExecuteAsync(Login.App, complete, mentorship.Goal));
        await seed.ApprovalAsync(rejected, mentorship.Relationship, mentorship.Mentor, "REJECTED");
        await Rejected.WithAsync(PostgresErrorCodes.CheckViolation, () => db.ExecuteAsync(Login.App, complete, mentorship.Goal));

        var approved = await seed.ProgressAsync(mentorship.Goal, mentorship.Relationship, mentorship.Protege);
        await seed.ApprovalAsync(approved, mentorship.Relationship, mentorship.Mentor);
        await db.ExecuteAsync(Login.App, complete, mentorship.Goal);

        Assert.Equal("COMPLETE", await StatusAsync(db, mentorship.Goal));
    }

    [Fact]
    public async Task A_goal_starts_incomplete()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var mentorship = await new Seed(db).FormedMentorshipAsync();

        await Rejected.WithAsync(PostgresErrorCodes.CheckViolation, () => db.ExecuteAsync(Login.App, """
            INSERT INTO mentor.development_goal (id, relationship_id, formed_agreement_id, position, agreed_description, completion_status)
            VALUES (gen_random_uuid(), $1, $2, 2, 'Mentor a junior engineer', 'COMPLETE')
            """, mentorship.Relationship, mentorship.FormedAgreement));
    }

    private static Task<string> StatusAsync(TestDatabase db, Guid goal) =>
        db.ScalarAsync<string>(Login.App, "SELECT completion_status FROM mentor.development_goal WHERE id = $1", goal);
}

using Npgsql;

namespace Mentor.Tests.Integration.Persistence;

// The candidate lifecycle of ADR-015 as the database enforces it (DR-004, FR-020, FR-021).
public sealed class CandidateTests(PostgresServer postgres)
{
    private const string MutualAcceptance = "ck_mentor_selection_mutual_acceptance";

    // Where a change breaks more than the invariant, any of the broken constraints may be the one reported.
    [Theory]
    [InlineData("protege_willing_at = NULL", MutualAcceptance)]
    [InlineData("mentor_willing_at = NULL", MutualAcceptance)]
    [InlineData("mentor_response = 'DECLINED'", null)]
    [InlineData("withdrawn_at = now(), withdrawn_by_employee_id = protege_employee_id", null)]
    public async Task A_drafting_candidate_keeps_the_mentors_acceptance_and_both_willingness_decisions(string change, string? constraint)
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var (candidate, _, _, _) = await CandidateAsync(db, "DRAFTING");

        await Rejected.WithAsync(PostgresErrorCodes.CheckViolation,
            () => db.ExecuteAsync(Login.App, $"UPDATE mentor.mentor_selection SET {change} WHERE id = $1", candidate), constraint);
    }

    [Theory]
    [InlineData("INVITED")]
    [InlineData("ACCEPTED")]
    public async Task A_candidate_without_mutual_willingness_cannot_reach_drafting(string status)
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var (candidate, _, _, _) = await CandidateAsync(db, status);

        await Rejected.WithAsync(PostgresErrorCodes.CheckViolation,
            () => db.ExecuteAsync(Login.App, "UPDATE mentor.mentor_selection SET status = 'DRAFTING' WHERE id = $1", candidate),
            MutualAcceptance);
    }

    [Fact]
    public async Task The_second_willingness_moves_an_accepted_candidate_on_to_drafting()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var (candidate, _, _, _) = await CandidateAsync(db, "ACCEPTED");

        await Rejected.WithAsync(PostgresErrorCodes.CheckViolation, () => db.ExecuteAsync(Login.App,
            "UPDATE mentor.mentor_selection SET protege_willing_at = now(), mentor_willing_at = now() WHERE id = $1", candidate),
            "ck_mentor_selection_status_response");
        await db.ExecuteAsync(Login.App,
            "UPDATE mentor.mentor_selection SET protege_willing_at = now(), mentor_willing_at = now(), status = 'DRAFTING', version = version + 1 WHERE id = $1",
            candidate);
    }

    [Theory]
    [InlineData("status = 'DECLINED'")]
    [InlineData("mentor_response = 'ACCEPTED'")]
    [InlineData("protege_willing_at = now()")]
    [InlineData("status = 'WITHDRAWN'")]
    [InlineData("current_revision_id = gen_random_uuid()")]
    public async Task An_invited_candidate_has_no_decision_the_mentor_has_not_made(string change)
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var (candidate, _, _, _) = await CandidateAsync(db, "INVITED");

        await Rejected.WithAsync(PostgresErrorCodes.CheckViolation,
            () => db.ExecuteAsync(Login.App, $"UPDATE mentor.mentor_selection SET {change} WHERE id = $1", candidate));
    }

    [Fact]
    public async Task Only_a_participant_withdraws_a_candidate()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var (candidate, _, _, outsider) = await CandidateAsync(db, "ACCEPTED");

        await Rejected.WithAsync(PostgresErrorCodes.CheckViolation, () => db.ExecuteAsync(Login.App,
            "UPDATE mentor.mentor_selection SET status = 'WITHDRAWN', withdrawn_at = now(), withdrawn_by_employee_id = $2 WHERE id = $1",
            candidate, outsider), "ck_mentor_selection_withdrawal");
        await db.ExecuteAsync(Login.App,
            "UPDATE mentor.mentor_selection SET status = 'WITHDRAWN', withdrawn_at = now(), withdrawn_by_employee_id = mentor_employee_id WHERE id = $1",
            candidate);
    }

    [Fact]
    public async Task A_candidates_protege_is_the_owner_of_its_request()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var seed = new Seed(db);
        var owner = await seed.EmployeeAsync();
        var someoneElse = await seed.EmployeeAsync();
        var mentor = await seed.EmployeeAsync();
        var request = await seed.RequestAsync(owner);

        await Rejected.WithAsync(PostgresErrorCodes.ForeignKeyViolation, () => seed.CandidateAsync(request, someoneElse, mentor, "INVITED"),
            "fk_mentor_selection_request_of_protege");
    }

    [Fact]
    public async Task A_protege_cannot_be_their_own_mentor()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var seed = new Seed(db);
        var employee = await seed.EmployeeAsync();
        var request = await seed.RequestAsync(employee);

        await Rejected.WithAsync(PostgresErrorCodes.CheckViolation, () => seed.CandidateAsync(request, employee, employee, "INVITED"),
            "ck_mentor_selection_distinct_participants");
    }

    [Fact]
    public async Task A_candidates_current_revision_is_one_of_its_own()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var seed = new Seed(db);
        var (candidate, protege, _, _) = await CandidateAsync(db, "DRAFTING");
        var (other, otherProtege, _, _) = await CandidateAsync(db, "DRAFTING");
        var foreignRevision = await seed.RevisionAsync(other, otherProtege);

        await Rejected.WithAsync(PostgresErrorCodes.ForeignKeyViolation, () => db.ExecuteAsync(Login.App,
            "UPDATE mentor.mentor_selection SET current_revision_id = $2 WHERE id = $1", candidate, foreignRevision.Id),
            "fk_mentor_selection_current_revision_of_candidate");
        var ownRevision = await seed.RevisionAsync(candidate, protege);
        Assert.Equal(ownRevision.Id, await db.ScalarAsync<Guid>(Login.App,
            "SELECT current_revision_id FROM mentor.mentor_selection WHERE id = $1", candidate));
    }

    // AC-DR-004-01: a declined selection keeps its disposition and never becomes a relationship.
    [Fact]
    public async Task A_declined_candidate_cannot_be_formed()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var (candidate, _, _, _) = await CandidateAsync(db, "DECLINED");

        await Rejected.WithAsync(PostgresErrorCodes.CheckViolation,
            () => db.ExecuteAsync(Login.App, "UPDATE mentor.mentor_selection SET status = 'FORMED' WHERE id = $1", candidate));
    }

    private static async Task<(Guid Candidate, Guid Protege, Guid Mentor, Guid Outsider)> CandidateAsync(TestDatabase db, string status)
    {
        var seed = new Seed(db);
        var protege = await seed.EmployeeAsync();
        var mentor = await seed.EmployeeAsync();
        var outsider = await seed.EmployeeAsync();
        var request = await seed.RequestAsync(protege);
        return (await seed.CandidateAsync(request, protege, mentor, status), protege, mentor, outsider);
    }
}

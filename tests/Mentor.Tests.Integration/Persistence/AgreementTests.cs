using Mentor.Core.Agreements;
using Npgsql;

namespace Mentor.Tests.Integration.Persistence;

// Content-addressed revisions, acceptances and formation decisions (ADR-004, ADR-005; DR-006, DR-007, DR-008, DR-015).
public sealed class AgreementTests(PostgresServer postgres)
{
    [Fact]
    public async Task A_revisions_content_is_a_json_object()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var (candidate, protege, _, _) = await DraftingCandidateAsync(db);

        await Rejected.WithAsync(PostgresErrorCodes.CheckViolation,
            () => InsertRevisionAsync(db, candidate, protege, "[1, 2]", AgreementContentHash.Compute("[1, 2]")), "ck_agreement_revision_content_json");
    }

    [Fact]
    public async Task A_revisions_hash_is_the_hash_of_its_content()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var (candidate, protege, _, _) = await DraftingCandidateAsync(db);

        await Rejected.WithAsync(PostgresErrorCodes.CheckViolation,
            () => InsertRevisionAsync(db, candidate, protege, Seed.Content(), AgreementContentHash.Compute(Seed.Content(2))),
            "ck_agreement_revision_content_hash");
        await InsertRevisionAsync(db, candidate, protege, Seed.Content(), AgreementContentHash.Compute(Seed.Content()));
    }

    [Fact]
    public async Task Revisions_are_numbered_once_per_candidate_and_replace_a_revision_of_the_same_candidate()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var seed = new Seed(db);
        var (candidate, protege, _, _) = await DraftingCandidateAsync(db);
        var (other, otherProtege, _, _) = await DraftingCandidateAsync(db);
        var first = await seed.RevisionAsync(candidate, protege);
        var foreign = await seed.RevisionAsync(other, otherProtege);

        await Rejected.WithAsync(PostgresErrorCodes.UniqueViolation,
            () => seed.RevisionAsync(candidate, protege, number: 1, makeCurrent: false), "ix_agreement_revision_candidate_id_revision_number");
        await Rejected.WithAsync(PostgresErrorCodes.CheckViolation,
            () => seed.RevisionAsync(candidate, protege, number: 2, makeCurrent: false), "ck_agreement_revision_parent");
        await Rejected.WithAsync(PostgresErrorCodes.ForeignKeyViolation,
            () => seed.RevisionAsync(candidate, protege, number: 2, parent: foreign.Id, makeCurrent: false), "fk_agreement_revision_parent_of_candidate");
        await seed.RevisionAsync(candidate, protege, number: 2, parent: first.Id);
    }

    [Fact]
    public async Task An_acceptance_names_exactly_the_hash_of_the_revision_it_accepts()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var seed = new Seed(db);
        var (candidate, protege, _, _) = await DraftingCandidateAsync(db);
        var revision = await seed.RevisionAsync(candidate, protege);

        await Rejected.WithAsync(PostgresErrorCodes.ForeignKeyViolation,
            () => seed.AcceptanceAsync(revision with { ContentHash = AgreementContentHash.Compute(Seed.Content(2)) }, protege),
            "fk_agreement_acceptance_revision_content");
    }

    [Fact]
    public async Task A_participant_accepts_a_revision_once()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var seed = new Seed(db);
        var (candidate, protege, _, _) = await DraftingCandidateAsync(db);
        var revision = await seed.RevisionAsync(candidate, protege);
        await seed.AcceptanceAsync(revision, protege);

        await Rejected.WithAsync(PostgresErrorCodes.UniqueViolation, () => seed.AcceptanceAsync(revision, protege),
            "ix_agreement_acceptance_revision_id_participant_employee_id");
    }

    [Fact]
    public async Task A_formation_decision_is_bound_to_a_revision_of_its_candidate_and_that_revisions_hash()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var seed = new Seed(db);
        var administrator = await seed.EmployeeAsync();
        var (candidate, protege, _, _) = await DraftingCandidateAsync(db);
        var (other, otherProtege, _, _) = await DraftingCandidateAsync(db);
        var revision = await seed.RevisionAsync(candidate, protege);
        var foreign = await seed.RevisionAsync(other, otherProtege);

        await Rejected.WithAsync(PostgresErrorCodes.ForeignKeyViolation, () => seed.FormationDecisionAsync(candidate, foreign, administrator),
            "fk_formation_decision_revision_of_candidate");
        await Rejected.WithAsync(PostgresErrorCodes.ForeignKeyViolation,
            () => seed.FormationDecisionAsync(candidate, revision with { ContentHash = AgreementContentHash.Compute(Seed.Content(2)) }, administrator),
            "fk_formation_decision_revision_content");
    }

    [Fact]
    public async Task A_candidate_is_approved_for_formation_at_most_once_while_rejections_may_repeat()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var seed = new Seed(db);
        var administrator = await seed.EmployeeAsync();
        var (candidate, protege, _, _) = await DraftingCandidateAsync(db);
        var revision = await seed.RevisionAsync(candidate, protege);

        await seed.FormationDecisionAsync(candidate, revision, administrator, "REJECTED");
        await seed.FormationDecisionAsync(candidate, revision, administrator, "REJECTED");
        await seed.FormationDecisionAsync(candidate, revision, administrator);
        await Rejected.WithAsync(PostgresErrorCodes.UniqueViolation, () => seed.FormationDecisionAsync(candidate, revision, administrator),
            "ux_formation_decision_one_approval_per_candidate");
    }

    // AC-NFR-005-01, AC-DR-006-01, AC-DR-007-01: a replacement leaves the accepted revision and its acceptances exactly as
    // they were; they stop applying because the candidate's current revision moved, so fresh acceptances are needed.
    [Fact]
    public async Task A_replacement_revision_preserves_the_accepted_one_and_leaves_its_acceptances_inapplicable()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var seed = new Seed(db);
        var (candidate, protege, mentor, _) = await DraftingCandidateAsync(db);
        var accepted = await seed.RevisionAsync(candidate, protege);
        await seed.AcceptanceAsync(accepted, protege);
        await seed.AcceptanceAsync(accepted, mentor);

        var replacement = await seed.RevisionAsync(candidate, mentor, number: 2, parent: accepted.Id);

        Assert.Equal(accepted.Content, await db.ScalarAsync<string>(Login.App, "SELECT content FROM mentor.agreement_revision WHERE id = $1", accepted.Id));
        Assert.NotEqual(accepted.ContentHash, replacement.ContentHash);
        Assert.Equal(2, await db.ScalarAsync<long>(Login.App, "SELECT count(*) FROM mentor.agreement_acceptance WHERE revision_id = $1", accepted.Id));
        Assert.Equal(0, await db.ScalarAsync<long>(Login.App, """
            SELECT count(*) FROM mentor.agreement_acceptance AS acceptance
            JOIN mentor.mentor_selection AS candidate ON candidate.current_revision_id = acceptance.revision_id
            WHERE candidate.id = $1 AND candidate.status = 'DRAFTING'
            """, candidate));
    }

    // AC-DR-015-01: the one no-reset change is recorded apart from the content, and each basis allows one annex field.
    [Fact]
    public async Task A_permitted_change_names_the_annex_field_its_basis_allows()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var seed = new Seed(db);
        var administrator = await seed.EmployeeAsync();
        var (candidate, _, _, _) = await DraftingCandidateAsync(db);
        const string insert = """
            INSERT INTO mentor.permitted_change (id, candidate_id, actor_employee_id, basis, field, new_value)
            VALUES (gen_random_uuid(), $1, $2, $3, $4, 'TM-2026-001')
            """;

        await Rejected.WithAsync(PostgresErrorCodes.CheckViolation,
            () => db.ExecuteAsync(Login.App, insert, candidate, administrator, "ADMINISTRATIVE_REFERENCE", "ADMIN_REMARKS"),
            "ck_permitted_change_basis_field");
        await db.ExecuteAsync(Login.App, insert, candidate, administrator, "ADMINISTRATIVE_REMARK", "ADMIN_REMARKS");
    }

    private static async Task<(Guid Candidate, Guid Protege, Guid Mentor, Guid Request)> DraftingCandidateAsync(TestDatabase db)
    {
        var seed = new Seed(db);
        var protege = await seed.EmployeeAsync();
        var mentor = await seed.EmployeeAsync();
        var request = await seed.RequestAsync(protege);
        return (await seed.CandidateAsync(request, protege, mentor), protege, mentor, request);
    }

    private static Task<int> InsertRevisionAsync(TestDatabase db, Guid candidate, Guid author, string content, string hash) =>
        db.ExecuteAsync(Login.App, """
            INSERT INTO mentor.agreement_revision (id, candidate_id, revision_number, content, content_hash, created_by_employee_id)
            VALUES (gen_random_uuid(), $1, 1, $2, $3, $4)
            """, candidate, content, hash, author);
}

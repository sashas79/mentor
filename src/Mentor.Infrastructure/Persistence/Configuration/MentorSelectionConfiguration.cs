using Mentor.Core.Agreements;
using Mentor.Core.Employees;
using Mentor.Core.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mentor.Infrastructure.Persistence.Configuration;

internal sealed class MentorSelectionConfiguration : IEntityTypeConfiguration<MentorSelection>
{
    public void Configure(EntityTypeBuilder<MentorSelection> builder)
    {
        builder.ToTable("mentor_selection", table =>
        {
            table.HasCheckConstraint("ck_mentor_selection_status", EnumColumn.Check<CandidateStatus>("status"));
            table.HasCheckConstraint("ck_mentor_selection_mentor_response", EnumColumn.Check<MentorResponse>("mentor_response"));
            table.HasCheckConstraint("ck_mentor_selection_distinct_participants", "mentor_employee_id <> protege_employee_id");
            // ADR-015: drafting and formation require the mentor's acceptance, both participants' willingness and no withdrawal.
            table.HasCheckConstraint("ck_mentor_selection_mutual_acceptance", """
                status NOT IN ('DRAFTING', 'FORMED')
                OR (mentor_response = 'ACCEPTED' AND protege_willing_at IS NOT NULL AND mentor_willing_at IS NOT NULL AND withdrawn_at IS NULL)
                """);
            // The status agrees with the mentor's response, and the second willingness moves an accepted candidate on to DRAFTING.
            table.HasCheckConstraint("ck_mentor_selection_status_response", """
                CASE status
                    WHEN 'INVITED' THEN mentor_response = 'PENDING'
                    WHEN 'DECLINED' THEN mentor_response = 'DECLINED'
                    WHEN 'ACCEPTED' THEN mentor_response = 'ACCEPTED' AND (protege_willing_at IS NULL OR mentor_willing_at IS NULL)
                    WHEN 'WITHDRAWN' THEN mentor_response <> 'DECLINED'
                    ELSE true
                END
                """);
            table.HasCheckConstraint("ck_mentor_selection_response_time", "(mentor_response = 'PENDING') = (mentor_responded_at IS NULL)");
            table.HasCheckConstraint("ck_mentor_selection_willingness",
                "mentor_response = 'ACCEPTED' OR (protege_willing_at IS NULL AND mentor_willing_at IS NULL)");
            // Only a participant withdraws, and a withdrawn candidate records who and when.
            table.HasCheckConstraint("ck_mentor_selection_withdrawal", """
                (status = 'WITHDRAWN') = (withdrawn_at IS NOT NULL)
                AND (withdrawn_at IS NULL) = (withdrawn_by_employee_id IS NULL)
                AND (withdrawn_by_employee_id IS NULL OR withdrawn_by_employee_id IN (protege_employee_id, mentor_employee_id))
                """);
            // Revisions exist only from DRAFTING on (ADR-004), and a formed candidate keeps the revision it was formed on.
            table.HasCheckConstraint("ck_mentor_selection_current_revision", """
                (current_revision_id IS NULL OR status IN ('DRAFTING', 'FORMED', 'WITHDRAWN'))
                AND (status <> 'FORMED' OR current_revision_id IS NOT NULL)
                """);
            table.HasCheckConstraint("ck_mentor_selection_version", "version >= 1");
        });

        // Referenced by a digital relationship together with both participants, so they cannot differ from the candidate's.
        builder.HasAlternateKey(selection => new { selection.Id, selection.ProtegeEmployeeId, selection.MentorEmployeeId });
        builder.HasOne<MentorshipRequest>().WithMany()
            .HasForeignKey(selection => new { selection.RequestId, selection.ProtegeEmployeeId })
            .HasPrincipalKey(request => new { request.Id, request.ProtegeEmployeeId })
            .HasConstraintName("fk_mentor_selection_request_of_protege");
        builder.HasOne<Employee>().WithMany().HasForeignKey(selection => selection.ProtegeEmployeeId);
        builder.HasOne<Employee>().WithMany().HasForeignKey(selection => selection.MentorEmployeeId);
        builder.HasOne<Employee>().WithMany().HasForeignKey(selection => selection.WithdrawnByEmployeeId);
        // The migration also binds the current revision to this candidate: (current_revision_id, id) references
        // agreement_revision (id, candidate_id). EF Core cannot express a foreign key that includes the primary key.
        builder.HasOne<AgreementRevision>().WithMany().HasForeignKey(selection => selection.CurrentRevisionId);

        builder.Property(selection => selection.Status).StoredAsText();
        builder.Property(selection => selection.MentorResponse).StoredAsText();
        builder.Property(selection => selection.Version).HasDefaultValue(1L);
        builder.Property(selection => selection.InvitedAt).IsInsertTime();
        builder.Property(selection => selection.UpdatedAt).IsUpdateTime();
    }
}

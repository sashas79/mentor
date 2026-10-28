using Mentor.Core.Employees;
using Mentor.Core.Relationships;
using Mentor.Core.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mentor.Infrastructure.Persistence.Configuration;

internal sealed class MentorshipRelationshipConfiguration : IEntityTypeConfiguration<MentorshipRelationship>
{
    public void Configure(EntityTypeBuilder<MentorshipRelationship> builder)
    {
        builder.ToTable("mentorship_relationship", table =>
        {
            table.HasCheckConstraint("ck_mentorship_relationship_provenance", EnumColumn.Check<Provenance>("provenance"));
            table.HasCheckConstraint("ck_mentorship_relationship_state", EnumColumn.Check<RelationshipState>("state"));
            table.HasCheckConstraint("ck_mentorship_relationship_status", EnumColumn.Check<RelationshipStatus>("status"));
            table.HasCheckConstraint("ck_mentorship_relationship_is_active", EnumColumn.Check<ActiveFlag>("is_active"));
            table.HasCheckConstraint("ck_mentorship_relationship_duration_category", EnumColumn.Check<DurationCategory>("duration_category"));
            table.HasCheckConstraint("ck_mentorship_relationship_distinct_participants", "mentor_employee_id <> protege_employee_id");
            // NULL is never a business value: only a legacy row may leave State, Status or IsActive unresolved (ADR-006).
            table.HasCheckConstraint("ck_mentorship_relationship_digital_attributes",
                "provenance <> 'DIGITAL' OR (state IS NOT NULL AND status IS NOT NULL AND is_active IS NOT NULL)");
            // A digital row comes from a candidate; a legacy row has none and carries its source and entrant instead (ADR-008).
            table.HasCheckConstraint("ck_mentorship_relationship_origin", """
                CASE provenance
                    WHEN 'DIGITAL' THEN candidate_id IS NOT NULL AND entered_by_employee_id IS NULL
                        AND source_file IS NULL AND source_row IS NULL
                        AND source_mentor_text IS NULL AND source_protege_text IS NULL AND source_active_text IS NULL
                        AND source_duration_text IS NULL AND source_start_text IS NULL AND source_end_text IS NULL
                    ELSE candidate_id IS NULL AND entered_by_employee_id IS NOT NULL
                        AND source_file IS NOT NULL AND source_row IS NOT NULL
                END
                """);
            table.HasCheckConstraint("ck_mentorship_relationship_version", "version >= 1");
        });

        // At most one relationship per candidate (ADR-005); legacy rows have none.
        builder.HasIndex(relationship => relationship.CandidateId).IsUnique();
        // A historical source row is entered at most once.
        builder.HasIndex(relationship => new { relationship.SourceFile, relationship.SourceRow }).IsUnique();
        // Referenced by progress with its protégé and by approvals with its mentor, so only they can submit or decide.
        builder.HasAlternateKey(relationship => new { relationship.Id, relationship.ProtegeEmployeeId });
        builder.HasAlternateKey(relationship => new { relationship.Id, relationship.MentorEmployeeId });
        // A digital relationship's participants are exactly its candidate's.
        builder.HasOne<MentorSelection>().WithMany()
            .HasForeignKey(relationship => new { relationship.CandidateId, relationship.ProtegeEmployeeId, relationship.MentorEmployeeId })
            .HasPrincipalKey(selection => new { selection.Id, selection.ProtegeEmployeeId, selection.MentorEmployeeId })
            .HasConstraintName("fk_mentorship_relationship_candidate_participants");
        builder.HasOne<Employee>().WithMany().HasForeignKey(relationship => relationship.ProtegeEmployeeId);
        builder.HasOne<Employee>().WithMany().HasForeignKey(relationship => relationship.MentorEmployeeId);
        builder.HasOne<Employee>().WithMany().HasForeignKey(relationship => relationship.EnteredByEmployeeId);

        builder.Property(relationship => relationship.Provenance).StoredAsText();
        builder.Property(relationship => relationship.State).StoredAsText();
        builder.Property(relationship => relationship.Status).StoredAsText();
        builder.Property(relationship => relationship.IsActive).StoredAsText();
        builder.Property(relationship => relationship.DurationCategory).StoredAsText();
        builder.Property(relationship => relationship.Version).HasDefaultValue(1L);
        builder.Property(relationship => relationship.CreatedAt).IsInsertTime();
        builder.Property(relationship => relationship.UpdatedAt).IsUpdateTime();
    }
}

using Mentor.Core.Agreements;
using Mentor.Core.Audit;
using Mentor.Core.Employees;
using Mentor.Core.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mentor.Infrastructure.Persistence.Configuration;

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_entry", table =>
        {
            table.HasCheckConstraint("ck_audit_entry_action", EnumColumn.Check<AuditAction>("action"));
            table.HasCheckConstraint("ck_audit_entry_identity_scheme", EnumColumn.Check<IdentityScheme>("identity_scheme"));
            table.HasCheckConstraint("ck_audit_entry_provenance", EnumColumn.Check<AuditProvenance>("provenance"));
            // An application event was authenticated in the application; a legacy-source claim was entered under a
            // database account and names the source row it came from (ADR-007, ADR-008).
            table.HasCheckConstraint("ck_audit_entry_origin", """
                CASE provenance
                    WHEN 'APPLICATION' THEN identity_scheme IN ('ENTRA', 'SIMULATED') AND source_file IS NULL AND source_row IS NULL
                    ELSE identity_scheme = 'DATABASE' AND source_file IS NOT NULL AND source_row IS NOT NULL
                END
                """);
            table.HasCheckConstraint("ck_audit_entry_decision_context", "(revision_id IS NULL) = (content_hash IS NULL)");
        });

        // Administrators query the trail by record (ADR-007).
        builder.HasIndex(entry => new { entry.RecordType, entry.RecordId });
        builder.HasOne<Employee>().WithMany().HasForeignKey(entry => entry.ActorEmployeeId);
        builder.HasOne<MentorSelection>().WithMany().HasForeignKey(entry => entry.CandidateId);
        builder.HasOne<AgreementRevision>().WithMany()
            .HasForeignKey(entry => new { entry.RevisionId, entry.ContentHash })
            .HasPrincipalKey(revision => new { revision.Id, revision.ContentHash })
            .HasConstraintName("fk_audit_entry_revision_content");

        builder.Property(entry => entry.Action).StoredAsText();
        builder.Property(entry => entry.IdentityScheme).StoredAsText();
        builder.Property(entry => entry.Provenance).StoredAsText();
        builder.Property(entry => entry.PriorValues).HasColumnType("jsonb");
        builder.Property(entry => entry.NewValues).HasColumnType("jsonb");
        builder.Property(entry => entry.OccurredAt).IsInsertTime();
    }
}

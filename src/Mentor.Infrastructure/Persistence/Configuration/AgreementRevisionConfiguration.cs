using Mentor.Core.Agreements;
using Mentor.Core.Employees;
using Mentor.Core.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mentor.Infrastructure.Persistence.Configuration;

internal sealed class AgreementRevisionConfiguration : IEntityTypeConfiguration<AgreementRevision>
{
    public void Configure(EntityTypeBuilder<AgreementRevision> builder)
    {
        builder.ToTable("agreement_revision", table =>
        {
            table.HasCheckConstraint("ck_agreement_revision_number", "revision_number >= 1");
            table.HasCheckConstraint("ck_agreement_revision_parent", "(revision_number = 1) = (parent_revision_id IS NULL)");
            table.HasCheckConstraint("ck_agreement_revision_content_json", "content IS JSON OBJECT");
            // The content identity is provably the hash of the content (CONTRACT-AGREEMENT-CONTENT).
            table.HasCheckConstraint("ck_agreement_revision_content_hash", ContentHashRule.Check);
        });

        // Every stored copy of a content hash references (id, content_hash), so it is provably the revision's own (ADR-013).
        builder.HasAlternateKey(revision => new { revision.Id, revision.ContentHash });
        // Decisions and child revisions reference (id, candidate_id), so they stay within one candidate.
        builder.HasAlternateKey(revision => new { revision.Id, revision.CandidateId });
        builder.HasIndex(revision => new { revision.CandidateId, revision.RevisionNumber }).IsUnique();
        builder.HasOne<MentorSelection>().WithMany().HasForeignKey(revision => revision.CandidateId);
        builder.HasOne<AgreementRevision>().WithMany()
            .HasForeignKey(revision => new { revision.ParentRevisionId, revision.CandidateId })
            .HasPrincipalKey(parent => new { parent.Id, parent.CandidateId })
            .HasConstraintName("fk_agreement_revision_parent_of_candidate");
        builder.HasOne<Employee>().WithMany().HasForeignKey(revision => revision.CreatedByEmployeeId);

        builder.Property(revision => revision.CreatedAt).IsInsertTime();
    }
}

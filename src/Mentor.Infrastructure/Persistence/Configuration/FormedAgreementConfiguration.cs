using Mentor.Core.Agreements;
using Mentor.Core.Formation;
using Mentor.Core.Relationships;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mentor.Infrastructure.Persistence.Configuration;

internal sealed class FormedAgreementConfiguration : IEntityTypeConfiguration<FormedAgreement>
{
    public void Configure(EntityTypeBuilder<FormedAgreement> builder)
    {
        // Its hash is the hash of its content and also the approved revision's hash, so the content is byte-identical
        // to the content the participants accepted.
        builder.ToTable("formed_agreement", table =>
            table.HasCheckConstraint("ck_formed_agreement_content_hash", ContentHashRule.Check));

        // At most one formed agreement per relationship (ADR-005).
        builder.HasIndex(agreement => agreement.RelationshipId).IsUnique();
        // Referenced by each goal together with its relationship.
        builder.HasAlternateKey(agreement => new { agreement.Id, agreement.RelationshipId });
        // The migration also binds the relationship to the same candidate: (relationship_id, candidate_id) references
        // mentorship_relationship (id, candidate_id), which no legacy relationship can satisfy (ADR-008). EF Core keys
        // cannot include the nullable candidate_id.
        builder.HasOne<MentorshipRelationship>().WithMany().HasForeignKey(agreement => agreement.RelationshipId);
        builder.HasOne<AgreementRevision>().WithMany()
            .HasForeignKey(agreement => new { agreement.RevisionId, agreement.CandidateId })
            .HasPrincipalKey(revision => new { revision.Id, revision.CandidateId })
            .HasConstraintName("fk_formed_agreement_revision_of_candidate");
        builder.HasOne<AgreementRevision>().WithMany()
            .HasForeignKey(agreement => new { agreement.RevisionId, agreement.ContentHash })
            .HasPrincipalKey(revision => new { revision.Id, revision.ContentHash })
            .HasConstraintName("fk_formed_agreement_revision_content");

        builder.Property(agreement => agreement.FormedAt).IsInsertTime();
    }
}

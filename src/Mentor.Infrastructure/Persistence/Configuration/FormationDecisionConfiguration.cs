using Mentor.Core.Agreements;
using Mentor.Core.Employees;
using Mentor.Core.Formation;
using Mentor.Core.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mentor.Infrastructure.Persistence.Configuration;

internal sealed class FormationDecisionConfiguration : IEntityTypeConfiguration<FormationDecision>
{
    public void Configure(EntityTypeBuilder<FormationDecision> builder)
    {
        builder.ToTable("formation_decision", table =>
            table.HasCheckConstraint("ck_formation_decision_outcome", EnumColumn.Check<FormationOutcome>("outcome")));

        // At most one approval per candidate (ADR-005); rejections are evidence only and may repeat.
        builder.HasIndex(decision => decision.CandidateId)
            .IsUnique()
            .HasFilter("outcome = 'APPROVED'")
            .HasDatabaseName("ux_formation_decision_one_approval_per_candidate");
        builder.HasOne<MentorSelection>().WithMany().HasForeignKey(decision => decision.CandidateId);
        // The reviewed revision belongs to the decided candidate and carries exactly the hash the decision names.
        builder.HasOne<AgreementRevision>().WithMany()
            .HasForeignKey(decision => new { decision.RevisionId, decision.CandidateId })
            .HasPrincipalKey(revision => new { revision.Id, revision.CandidateId })
            .HasConstraintName("fk_formation_decision_revision_of_candidate");
        builder.HasOne<AgreementRevision>().WithMany()
            .HasForeignKey(decision => new { decision.RevisionId, decision.ContentHash })
            .HasPrincipalKey(revision => new { revision.Id, revision.ContentHash })
            .HasConstraintName("fk_formation_decision_revision_content");
        builder.HasOne<Employee>().WithMany().HasForeignKey(decision => decision.AdministratorEmployeeId);

        builder.Property(decision => decision.Outcome).StoredAsText();
        builder.Property(decision => decision.DecidedAt).IsInsertTime();
    }
}

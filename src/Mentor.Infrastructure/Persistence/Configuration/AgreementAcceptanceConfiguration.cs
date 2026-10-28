using Mentor.Core.Agreements;
using Mentor.Core.Employees;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mentor.Infrastructure.Persistence.Configuration;

internal sealed class AgreementAcceptanceConfiguration : IEntityTypeConfiguration<AgreementAcceptance>
{
    public void Configure(EntityTypeBuilder<AgreementAcceptance> builder)
    {
        builder.ToTable("agreement_acceptance");

        // At most one acceptance per participant and revision (ADR-005).
        builder.HasIndex(acceptance => new { acceptance.RevisionId, acceptance.ParticipantEmployeeId }).IsUnique();
        builder.HasOne<AgreementRevision>().WithMany()
            .HasForeignKey(acceptance => new { acceptance.RevisionId, acceptance.ContentHash })
            .HasPrincipalKey(revision => new { revision.Id, revision.ContentHash })
            .HasConstraintName("fk_agreement_acceptance_revision_content");
        builder.HasOne<Employee>().WithMany().HasForeignKey(acceptance => acceptance.ParticipantEmployeeId);

        builder.Property(acceptance => acceptance.AcceptedAt).IsInsertTime();
    }
}

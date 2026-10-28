using Mentor.Core.Agreements;
using Mentor.Core.Employees;
using Mentor.Core.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mentor.Infrastructure.Persistence.Configuration;

internal sealed class PermittedChangeConfiguration : IEntityTypeConfiguration<PermittedChange>
{
    public void Configure(EntityTypeBuilder<PermittedChange> builder)
    {
        builder.ToTable("permitted_change", table =>
        {
            table.HasCheckConstraint("ck_permitted_change_basis", EnumColumn.Check<PermittedChangeBasis>("basis"));
            table.HasCheckConstraint("ck_permitted_change_field", EnumColumn.Check<AnnexField>("field"));
            // Each permission basis allows exactly one annex field (ADR-004).
            table.HasCheckConstraint("ck_permitted_change_basis_field",
                "(basis = 'ADMINISTRATIVE_REFERENCE') = (field = 'ADMIN_REFERENCE')");
        });

        builder.HasOne<MentorSelection>().WithMany().HasForeignKey(change => change.CandidateId);
        builder.HasOne<Employee>().WithMany().HasForeignKey(change => change.ActorEmployeeId);

        builder.Property(change => change.Basis).StoredAsText();
        builder.Property(change => change.Field).StoredAsText();
        builder.Property(change => change.ChangedAt).IsInsertTime();
    }
}

using Mentor.Core.Employees;
using Mentor.Core.Legacy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mentor.Infrastructure.Persistence.Configuration;

internal sealed class LegacyEntryAuthorisationConfiguration : IEntityTypeConfiguration<LegacyEntryAuthorisation>
{
    public void Configure(EntityTypeBuilder<LegacyEntryAuthorisation> builder)
    {
        builder.ToTable("legacy_entry_authorisation");

        builder.HasIndex(authorisation => authorisation.DatabaseAccount).IsUnique();
        builder.HasOne<Employee>().WithMany().HasForeignKey(authorisation => authorisation.EmployeeId);

        builder.Property(authorisation => authorisation.AuthorisedAt).IsInsertTime();
    }
}

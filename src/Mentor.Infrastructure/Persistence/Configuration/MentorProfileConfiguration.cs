using Mentor.Core.Employees;
using Mentor.Core.MentorRegistry;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mentor.Infrastructure.Persistence.Configuration;

internal sealed class MentorProfileConfiguration : IEntityTypeConfiguration<MentorProfile>
{
    public void Configure(EntityTypeBuilder<MentorProfile> builder)
    {
        builder.ToTable("mentor_profile", table =>
        {
            table.HasCheckConstraint("ck_mentor_profile_approval_status", EnumColumn.Check<MentorApprovalStatus>("approval_status"));
            // A decided application names who decided it and when; a pending one names nobody.
            table.HasCheckConstraint("ck_mentor_profile_approval_decision", """
                CASE approval_status
                    WHEN 'PENDING' THEN approval_decided_by_employee_id IS NULL AND approval_decided_at IS NULL
                    ELSE approval_decided_by_employee_id IS NOT NULL AND approval_decided_at IS NOT NULL
                END
                """);
        });

        // At most one profile per employee (ADR-005).
        builder.HasIndex(profile => profile.EmployeeId).IsUnique();
        builder.HasOne<Employee>().WithMany().HasForeignKey(profile => profile.EmployeeId);
        builder.HasOne<Employee>().WithMany().HasForeignKey(profile => profile.ApprovalDecidedByEmployeeId);

        builder.Property(profile => profile.ApprovalStatus).StoredAsText();
        builder.Property(profile => profile.CreatedAt).IsInsertTime();
        builder.Property(profile => profile.UpdatedAt).IsUpdateTime();
    }
}

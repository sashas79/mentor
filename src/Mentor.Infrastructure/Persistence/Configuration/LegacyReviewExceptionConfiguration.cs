using Mentor.Core.Employees;
using Mentor.Core.Legacy;
using Mentor.Core.Relationships;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mentor.Infrastructure.Persistence.Configuration;

internal sealed class LegacyReviewExceptionConfiguration : IEntityTypeConfiguration<LegacyReviewException>
{
    public void Configure(EntityTypeBuilder<LegacyReviewException> builder)
    {
        builder.ToTable("legacy_review_exception", table =>
        {
            table.HasCheckConstraint("ck_legacy_review_exception_reason", EnumColumn.Check<LegacyReviewReason>("reason"));
            table.HasCheckConstraint("ck_legacy_review_exception_attribute", EnumColumn.Check<RelationshipAttribute>("attribute"));
            // An unresolved value belongs to an entered relationship; an unresolved person or a missing mentor keeps the
            // source row out of the relationship table (ADR-008).
            table.HasCheckConstraint("ck_legacy_review_exception_subject", """
                CASE reason
                    WHEN 'UNRESOLVED_VALUE' THEN relationship_id IS NOT NULL AND attribute IS NOT NULL
                    ELSE relationship_id IS NULL AND attribute IS NULL
                END
                """);
        });

        builder.HasIndex(exception => new { exception.RelationshipId, exception.Attribute }).IsUnique();
        builder.HasOne<MentorshipRelationship>().WithMany().HasForeignKey(exception => exception.RelationshipId)
            .HasConstraintName("fk_legacy_review_exception_relationship");
        builder.HasOne<Employee>().WithMany().HasForeignKey(exception => exception.EnteredByEmployeeId);

        builder.Property(exception => exception.Reason).StoredAsText();
        builder.Property(exception => exception.Attribute).StoredAsText();
        builder.Property(exception => exception.CreatedAt).IsInsertTime();
    }
}

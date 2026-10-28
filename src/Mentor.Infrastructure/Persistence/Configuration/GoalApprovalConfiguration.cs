using Mentor.Core.Employees;
using Mentor.Core.Goals;
using Mentor.Core.Relationships;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mentor.Infrastructure.Persistence.Configuration;

internal sealed class GoalApprovalConfiguration : IEntityTypeConfiguration<GoalApproval>
{
    public void Configure(EntityTypeBuilder<GoalApproval> builder)
    {
        builder.ToTable("goal_approval", table =>
        {
            table.HasCheckConstraint("ck_goal_approval_decision", EnumColumn.Check<GoalDecision>("decision"));
            // With the reference below, a decision can only be recorded on a completion request.
            table.HasCheckConstraint("ck_goal_approval_completion_requested", "completion_requested");
        });

        // At most one decision per completion request (ADR-005).
        builder.HasIndex(approval => approval.ProgressId).IsUnique();
        builder.HasOne<GoalProgress>().WithMany()
            .HasForeignKey(approval => new { approval.ProgressId, approval.RelationshipId, approval.CompletionRequested })
            .HasPrincipalKey(progress => new { progress.Id, progress.RelationshipId, progress.CompletionRequested })
            .HasConstraintName("fk_goal_approval_completion_request");
        // Only the relationship's mentor decides (BR-005).
        builder.HasOne<MentorshipRelationship>().WithMany()
            .HasForeignKey(approval => new { approval.RelationshipId, approval.MentorEmployeeId })
            .HasPrincipalKey(relationship => new { relationship.Id, relationship.MentorEmployeeId })
            .HasConstraintName("fk_goal_approval_relationship_mentor");
        builder.HasOne<Employee>().WithMany().HasForeignKey(approval => approval.MentorEmployeeId);

        builder.Property(approval => approval.Decision).StoredAsText();
        builder.Property(approval => approval.DecidedAt).IsInsertTime();
    }
}

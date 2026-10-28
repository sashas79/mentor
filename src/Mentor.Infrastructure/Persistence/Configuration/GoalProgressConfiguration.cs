using Mentor.Core.Employees;
using Mentor.Core.Goals;
using Mentor.Core.Relationships;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mentor.Infrastructure.Persistence.Configuration;

internal sealed class GoalProgressConfiguration : IEntityTypeConfiguration<GoalProgress>
{
    public void Configure(EntityTypeBuilder<GoalProgress> builder)
    {
        builder.ToTable("goal_progress");

        // Referenced by an approval with its relationship and the completion flag.
        builder.HasAlternateKey(progress => new { progress.Id, progress.RelationshipId, progress.CompletionRequested });
        builder.HasOne<DevelopmentGoal>().WithMany()
            .HasForeignKey(progress => new { progress.GoalId, progress.RelationshipId })
            .HasPrincipalKey(goal => new { goal.Id, goal.RelationshipId })
            .HasConstraintName("fk_goal_progress_goal_of_relationship");
        // Only the relationship's protégé submits progress (BR-005).
        builder.HasOne<MentorshipRelationship>().WithMany()
            .HasForeignKey(progress => new { progress.RelationshipId, progress.ProtegeEmployeeId })
            .HasPrincipalKey(relationship => new { relationship.Id, relationship.ProtegeEmployeeId })
            .HasConstraintName("fk_goal_progress_relationship_protege");
        builder.HasOne<Employee>().WithMany().HasForeignKey(progress => progress.ProtegeEmployeeId);

        builder.Property(progress => progress.SubmittedAt).IsInsertTime();
    }
}

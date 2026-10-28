using Mentor.Core.Formation;
using Mentor.Core.Goals;
using Mentor.Core.Relationships;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mentor.Infrastructure.Persistence.Configuration;

internal sealed class DevelopmentGoalConfiguration : IEntityTypeConfiguration<DevelopmentGoal>
{
    public void Configure(EntityTypeBuilder<DevelopmentGoal> builder)
    {
        builder.ToTable("development_goal", table =>
        {
            table.HasCheckConstraint("ck_development_goal_completion_status", EnumColumn.Check<GoalCompletionStatus>("completion_status"));
            table.HasCheckConstraint("ck_development_goal_position", "position >= 1");
        });

        builder.HasIndex(goal => new { goal.FormedAgreementId, goal.Position }).IsUnique();
        // Referenced by progress together with its relationship.
        builder.HasAlternateKey(goal => new { goal.Id, goal.RelationshipId });
        // A goal comes from its own relationship's formed agreement; a legacy relationship has none, so no goals (ADR-008).
        builder.HasOne<FormedAgreement>().WithMany()
            .HasForeignKey(goal => new { goal.FormedAgreementId, goal.RelationshipId })
            .HasPrincipalKey(agreement => new { agreement.Id, agreement.RelationshipId })
            .HasConstraintName("fk_development_goal_agreement_of_relationship");
        builder.HasOne<MentorshipRelationship>().WithMany().HasForeignKey(goal => goal.RelationshipId);

        builder.Property(goal => goal.CompletionStatus).StoredAsText();
        builder.Property(goal => goal.CreatedAt).IsInsertTime();
        builder.Property(goal => goal.UpdatedAt).IsUpdateTime();
    }
}

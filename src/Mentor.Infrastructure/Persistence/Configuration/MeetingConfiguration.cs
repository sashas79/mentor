using Mentor.Core.Employees;
using Mentor.Core.Meetings;
using Mentor.Core.Relationships;
using Mentor.Core.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mentor.Infrastructure.Persistence.Configuration;

internal sealed class MeetingConfiguration : IEntityTypeConfiguration<Meeting>
{
    public void Configure(EntityTypeBuilder<Meeting> builder)
    {
        builder.ToTable("meeting", table =>
        {
            table.HasCheckConstraint("ck_meeting_kind", EnumColumn.Check<MeetingKind>("kind"));
            // An introduction belongs to a candidate, a recurring session to a formed relationship (ADR-010).
            table.HasCheckConstraint("ck_meeting_owner", """
                CASE kind
                    WHEN 'INTRODUCTORY' THEN candidate_id IS NOT NULL AND relationship_id IS NULL
                    ELSE relationship_id IS NOT NULL AND candidate_id IS NULL
                END
                """);
            table.HasCheckConstraint("ck_meeting_frequency_weeks", "frequency_weeks > 0");
            table.HasCheckConstraint("ck_meeting_duration_minutes", "duration_minutes > 0");
        });

        builder.HasOne<MentorSelection>().WithMany().HasForeignKey(meeting => meeting.CandidateId);
        builder.HasOne<MentorshipRelationship>().WithMany().HasForeignKey(meeting => meeting.RelationshipId);
        builder.HasOne<Employee>().WithMany().HasForeignKey(meeting => meeting.CreatedByEmployeeId);

        builder.Property(meeting => meeting.Kind).StoredAsText();
        builder.Property(meeting => meeting.CreatedAt).IsInsertTime();
        builder.Property(meeting => meeting.UpdatedAt).IsUpdateTime();
    }
}

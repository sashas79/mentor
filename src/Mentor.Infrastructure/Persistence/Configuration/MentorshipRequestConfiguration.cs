using Mentor.Core.Employees;
using Mentor.Core.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mentor.Infrastructure.Persistence.Configuration;

internal sealed class MentorshipRequestConfiguration : IEntityTypeConfiguration<MentorshipRequest>
{
    public void Configure(EntityTypeBuilder<MentorshipRequest> builder)
    {
        builder.ToTable("mentorship_request", table =>
            table.HasCheckConstraint("ck_mentorship_request_disposition", EnumColumn.Check<RequestDisposition>("disposition")));

        // Referenced by a candidate together with its protégé, so a candidate's protégé is the request's owner.
        builder.HasAlternateKey(request => new { request.Id, request.ProtegeEmployeeId });
        builder.HasOne<Employee>().WithMany().HasForeignKey(request => request.ProtegeEmployeeId);

        builder.Property(request => request.Disposition).StoredAsText();
        builder.Property(request => request.SubmittedAt).IsInsertTime();
        builder.Property(request => request.UpdatedAt).IsUpdateTime();
    }
}

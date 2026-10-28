using Mentor.Core.Employees;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mentor.Infrastructure.Persistence.Configuration;

internal sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("employee");
        builder.HasIndex(employee => employee.EntraObjectId).IsUnique();
        builder.Property(employee => employee.CreatedAt).IsInsertTime();
        builder.Property(employee => employee.UpdatedAt).IsUpdateTime();
    }
}

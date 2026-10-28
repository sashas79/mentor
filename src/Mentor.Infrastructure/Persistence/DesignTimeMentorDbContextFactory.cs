using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Mentor.Infrastructure.Persistence;

/// <summary>
/// Lets the EF Core tools build the model without the API host or a database. Migrations are the only way the schema
/// changes (ADR-013); add one from src/ with:
/// <code>
/// dotnet tool restore
/// dotnet ef migrations add &lt;Name&gt; --project Mentor.Infrastructure --startup-project Mentor.Api --output-dir Persistence/Migrations
/// </code>
/// </summary>
public sealed class DesignTimeMentorDbContextFactory : IDesignTimeDbContextFactory<MentorDbContext>
{
    public MentorDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<MentorDbContext>().UseMentorDatabase("Host=design-time-only").Options);
}

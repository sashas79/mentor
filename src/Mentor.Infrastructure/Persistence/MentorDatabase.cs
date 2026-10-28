using Microsoft.EntityFrameworkCore;

namespace Mentor.Infrastructure.Persistence;

/// <summary>The authoritative PostgreSQL store (COMP-DATA-STORE, ADR-013).</summary>
public static class MentorDatabase
{
    /// <summary>The schema that holds every table, function and migration record of the application.</summary>
    public const string Schema = "mentor";

    /// <summary>The ConnectionStrings entry the API connects with.</summary>
    public const string ConnectionStringName = "Mentor";

    /// <summary>The group role of the API: column-level privileges, no DELETE anywhere and no UPDATE on evidence.</summary>
    public const string ApplicationRole = "mentor_app";

    /// <summary>The group role of Talent Management administrators entering historical records (ADR-008).</summary>
    public const string LegacyEntryRole = "mentor_legacy_entry";

    /// <summary>The provider options shared by the API, the EF Core tools and the tests.</summary>
    public static TBuilder UseMentorDatabase<TBuilder>(this TBuilder options, string connectionString)
        where TBuilder : DbContextOptionsBuilder
    {
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("ef_migrations_history", Schema))
            .UseSnakeCaseNamingConvention();
        return options;
    }
}

using Mentor.Infrastructure.Persistence;
using Mentor.Tests.Integration.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(PostgresServer))]

namespace Mentor.Tests.Integration.Persistence;

/// <summary>The accounts a test connects with.</summary>
public enum Login
{
    /// <summary>The superuser that owns the schema; database rules that are not privileges still bind it.</summary>
    Owner,

    /// <summary>The API's account, a member of mentor_app.</summary>
    App,

    /// <summary>A Talent Management administrator's account, a member of mentor_legacy_entry.</summary>
    Alice,

    /// <summary>A second Talent Management administrator's account, a member of mentor_legacy_entry.</summary>
    Bob,
}

/// <summary>
/// One PostgreSQL 17 container for the test run (ADR-001, ADR-013). The migrations run once, as the owner, into a
/// template database; every test clones it into a database of its own.
/// </summary>
public sealed class PostgresServer : IAsyncLifetime
{
    private const string Template = "mentor_template";

    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly SemaphoreSlim cloning = new(1, 1);

    public async ValueTask InitializeAsync()
    {
        await container.StartAsync();
        await ExecuteAsync(ConnectionString("postgres", Login.Owner), $"CREATE DATABASE {Template}");
        await using (var context = new MentorDbContext(new DbContextOptionsBuilder<MentorDbContext>()
                         .UseMentorDatabase(ConnectionString(Template, Login.Owner)).Options))
        {
            await context.Database.MigrateAsync();
        }

        // A template database cannot be cloned while anyone is connected to it.
        NpgsqlConnection.ClearAllPools();
        await ExecuteAsync(ConnectionString("postgres", Login.Owner), """
            CREATE ROLE mentor_api LOGIN PASSWORD 'mentor_api' IN ROLE mentor_app;
            CREATE ROLE tm_alice LOGIN PASSWORD 'tm_alice' IN ROLE mentor_legacy_entry;
            CREATE ROLE tm_bob LOGIN PASSWORD 'tm_bob' IN ROLE mentor_legacy_entry;
            """);
    }

    public async Task<TestDatabase> CreateDatabaseAsync()
    {
        var name = $"test_{Guid.NewGuid():N}";
        await cloning.WaitAsync();
        try
        {
            await ExecuteAsync(ConnectionString("postgres", Login.Owner), $"CREATE DATABASE {name} TEMPLATE {Template}");
        }
        finally
        {
            cloning.Release();
        }

        return new TestDatabase(this, name);
    }

    public string ConnectionString(string database, Login login)
    {
        var connection = new NpgsqlConnectionStringBuilder(container.GetConnectionString()) { Database = database };
        (connection.Username, connection.Password) = login switch
        {
            Login.Owner => (connection.Username, connection.Password),
            Login.App => ("mentor_api", "mentor_api"),
            Login.Alice => ("tm_alice", "tm_alice"),
            Login.Bob => ("tm_bob", "tm_bob"),
            _ => throw new ArgumentOutOfRangeException(nameof(login)),
        };
        return connection.ConnectionString;
    }

    public static string AccountOf(Login login) => login switch
    {
        Login.App => "mentor_api",
        Login.Alice => "tm_alice",
        Login.Bob => "tm_bob",
        _ => throw new ArgumentOutOfRangeException(nameof(login)),
    };

    internal async Task DropDatabaseAsync(string name) =>
        await ExecuteAsync(ConnectionString("postgres", Login.Owner), $"DROP DATABASE {name} WITH (FORCE)");

    public async ValueTask DisposeAsync()
    {
        await container.DisposeAsync();
        cloning.Dispose();
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}

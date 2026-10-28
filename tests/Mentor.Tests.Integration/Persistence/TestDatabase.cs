using Mentor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Mentor.Tests.Integration.Persistence;

/// <summary>A migrated database of one test's own, reachable under each <see cref="Login"/>.</summary>
public sealed class TestDatabase(PostgresServer server, string name) : IAsyncDisposable
{
    public string ConnectionString(Login login) => server.ConnectionString(name, login);

    public MentorDbContext Context(Login login) =>
        new(new DbContextOptionsBuilder<MentorDbContext>().UseMentorDatabase(ConnectionString(login)).Options);

    /// <summary>Runs a statement with positional parameters ($1, $2, ...); null stands for SQL NULL.</summary>
    public async Task<int> ExecuteAsync(Login login, string sql, params object?[] parameters)
    {
        await using var connection = await OpenAsync(login);
        await using var command = Command(connection, sql, parameters);
        return await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>The first column of the first row; SQL NULL comes back as the default of <typeparamref name="T"/>.</summary>
    public async Task<T> ScalarAsync<T>(Login login, string sql, params object?[] parameters)
    {
        await using var connection = await OpenAsync(login);
        await using var command = Command(connection, sql, parameters);
        var value = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return value is null or DBNull ? default! : (T)value;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var login in Enum.GetValues<Login>())
        {
            await using var connection = new NpgsqlConnection(ConnectionString(login));
            NpgsqlConnection.ClearPool(connection);
        }

        await server.DropDatabaseAsync(name);
    }

    private async Task<NpgsqlConnection> OpenAsync(Login login)
    {
        var connection = new NpgsqlConnection(ConnectionString(login));
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return connection;
    }

    private static NpgsqlCommand Command(NpgsqlConnection connection, string sql, object?[] parameters)
    {
        var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = parameter ?? DBNull.Value });
        }

        return command;
    }
}

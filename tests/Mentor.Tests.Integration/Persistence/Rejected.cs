using Npgsql;

namespace Mentor.Tests.Integration.Persistence;

/// <summary>Asserts that the database refused an operation with a given SQLSTATE and, when named, by a given constraint.</summary>
internal static class Rejected
{
    public static async Task<PostgresException> WithAsync(string sqlState, Func<Task> operation, string? constraint = null)
    {
        var exception = await Record.ExceptionAsync(operation);
        var refusal = exception as PostgresException ?? exception?.InnerException as PostgresException;
        Assert.True(refusal is not null,
            $"Expected SQLSTATE {sqlState}, but got {exception?.GetType().Name ?? "no exception"}: {exception?.Message}");
        Assert.True(sqlState == refusal.SqlState, $"Expected SQLSTATE {sqlState}, but got {refusal.SqlState}: {refusal.MessageText}");
        if (constraint is not null)
        {
            Assert.True(constraint == refusal.ConstraintName,
                $"Expected constraint {constraint} to refuse it, but {refusal.ConstraintName ?? "no constraint"} did: {refusal.MessageText}");
        }

        return refusal;
    }
}

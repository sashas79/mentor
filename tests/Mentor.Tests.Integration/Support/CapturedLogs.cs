using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Mentor.Tests.Integration.Support;

/// <summary>Records every log entry, with its structured state and active scopes, as the providers would see it.</summary>
internal sealed class CapturedLogs : ILoggerProvider, ISupportExternalScope
{
    private readonly ConcurrentQueue<CapturedEntry> entries = new();
    private IExternalScopeProvider scopes = new LoggerExternalScopeProvider();

    public IReadOnlyList<CapturedEntry> Entries => [.. entries];

    public IReadOnlyList<CapturedEntry> From<TCategory>() =>
        [.. entries.Where(entry => entry.Category == typeof(TCategory).FullName)];

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => scopes = scopeProvider;

    public void Dispose()
    {
    }

    private static string Render(object? value) =>
        value is IEnumerable<KeyValuePair<string, object?>> pairs
            ? string.Join("; ", pairs.Select(pair => $"{pair.Key}={pair.Value}"))
            : value?.ToString() ?? string.Empty;

    private sealed class Logger(CapturedLogs owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => owner.scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var activeScopes = new List<string>();
            owner.scopes.ForEachScope((scope, list) => list.Add(Render(scope)), activeScopes);
            owner.entries.Enqueue(new CapturedEntry(category, logLevel, formatter(state, exception), Render(state), activeScopes, exception?.ToString()));
        }
    }
}

internal sealed record CapturedEntry(string Category, LogLevel Level, string Message, string State, IReadOnlyList<string> Scopes, string? Exception)
{
    public bool Mentions(string text) =>
        Message.Contains(text, StringComparison.OrdinalIgnoreCase)
        || State.Contains(text, StringComparison.OrdinalIgnoreCase)
        || Scopes.Any(scope => scope.Contains(text, StringComparison.OrdinalIgnoreCase))
        || (Exception?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false);
}

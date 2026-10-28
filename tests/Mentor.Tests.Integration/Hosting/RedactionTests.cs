using System.Net;
using System.Net.Http.Json;
using Mentor.Api.Hosting;
using Mentor.Tests.Integration.Support;
using Microsoft.Extensions.Logging;

namespace Mentor.Tests.Integration.Hosting;

// No log or error response carries request content or exception messages (AC-NFR-002-01, AC-NFR-009-01).
public sealed class RedactionTests
{
    // An operator turning every category up to Trace must not switch the redaction off.
    private static readonly Dictionary<string, string?> VerboseLogging = new()
    {
        ["Logging:LogLevel:Default"] = "Trace",
        ["Logging:LogLevel:Microsoft.AspNetCore"] = "Trace",
    };

    [Fact]
    public async Task Request_content_never_reaches_the_logs_even_when_logging_is_verbose()
    {
        await using var probe = await ProbeHost.StartAsync(settings: VerboseLogging);

        var note = ProbeHost.Request(HttpMethod.Post, $"/probe/notes/{Guid.NewGuid()}?search={ProbeHost.Note}");
        note.Content = JsonContent.Create(new ProbeNote(ProbeHost.Note));
        note.Headers.Add("X-Note", ProbeHost.Note);
        Assert.Equal(HttpStatusCode.NoContent, (await probe.SendAsync(note)).StatusCode);
        var unparsable = await ProblemAssert.IsProblem(HttpStatusCode.BadRequest, await probe.SendAsync(ProbeHost.Request(HttpMethod.Get, $"/probe/count?count={ProbeHost.Note}")));
        Assert.DoesNotContain(ProbeHost.Note, unparsable.GetRawText());
        await probe.SendAsync(ProbeHost.Request(HttpMethod.Get, $"/probe/{ProbeHost.Note}"));
        await probe.SendAsync(ProbeHost.Request(new HttpMethod(ProbeHost.Note), "/probe/public"));

        Assert.DoesNotContain(probe.Logs.Entries, entry => entry.Mentions(ProbeHost.Note));
        var requests = probe.Logs.From<RequestLoggingMiddleware>().Select(entry => entry.Message).ToList();
        Assert.Contains(requests, line => line.StartsWith("POST /probe/notes/{id:guid} answered 204", StringComparison.Ordinal));
        Assert.Contains(requests, line => line.StartsWith("GET /probe/count answered 400", StringComparison.Ordinal));
        Assert.Contains(requests, line => line.StartsWith("GET (no route) answered 401", StringComparison.Ordinal));
        Assert.Contains(requests, line => line.StartsWith("OTHER (no route) answered 401", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_unhandled_exception_is_answered_as_a_bare_problem_and_logged_without_its_message()
    {
        await using var probe = await ProbeHost.StartAsync(settings: VerboseLogging);

        var problem = await ProblemAssert.IsProblem(HttpStatusCode.InternalServerError, await probe.SendAsync(ProbeHost.Request(HttpMethod.Get, "/probe/throws")));

        Assert.DoesNotContain(ProbeHost.Note, problem.GetRawText());
        Assert.DoesNotContain(probe.Logs.Entries, entry => entry.Mentions(ProbeHost.Note));
        var logged = Assert.Single(probe.Logs.From<RedactingExceptionHandler>());
        Assert.Equal(LogLevel.Error, logged.Level);
        Assert.Contains(typeof(InvalidOperationException).FullName!, logged.Message);
        Assert.Contains(typeof(FormatException).FullName!, logged.Message);
        // The traceId the caller receives finds the request's log entries.
        Assert.Contains(logged.Scopes, scope => scope.Contains($"TraceId={problem.GetProperty("traceId").GetString()}", StringComparison.Ordinal));
        Assert.Contains(probe.Logs.From<RequestLoggingMiddleware>(), entry => entry.Message.StartsWith("GET /probe/throws answered 500", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_exception_after_the_response_started_is_logged_without_its_message()
    {
        await using var probe = await ProbeHost.StartAsync(settings: VerboseLogging);

        await Record.ExceptionAsync(() => probe.SendAsync(ProbeHost.Request(HttpMethod.Get, "/probe/fails-after-start")));

        Assert.DoesNotContain(probe.Logs.Entries, entry => entry.Mentions(ProbeHost.Note));
        var logged = Assert.Single(probe.Logs.From<RequestLoggingMiddleware>(), entry => entry.Level == LogLevel.Error);
        Assert.Contains(typeof(InvalidOperationException).FullName!, logged.Message);
    }
}

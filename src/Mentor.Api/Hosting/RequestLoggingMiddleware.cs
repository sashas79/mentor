using System.Collections.Frozen;
using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;

namespace Mentor.Api.Hosting;

/// <summary>
/// The outermost middleware of the redaction policy (<see cref="LogRedaction"/>). It writes one line per request
/// that names the matched route template instead of the path; it opens the TraceId scope carried by every log written
/// during the request, the same id the problem details report; and it logs, redacted, an exception that the exception
/// handler rethrows because the response had already started.
/// </summary>
internal sealed partial class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    private static readonly FrozenSet<string> StandardMethods = new[]
    {
        HttpMethods.Get, HttpMethods.Head, HttpMethods.Post, HttpMethods.Put, HttpMethods.Patch,
        HttpMethods.Delete, HttpMethods.Options, HttpMethods.Trace, HttpMethods.Connect,
    }.ToFrozenSet();

    private static readonly Func<ILogger, string, IDisposable?> TraceScope = LoggerMessage.DefineScope<string>("TraceId:{TraceId}");

    public async Task InvokeAsync(HttpContext context)
    {
        using var scope = TraceScope(logger, Activity.Current?.Id ?? context.TraceIdentifier);
        var started = Stopwatch.GetTimestamp();
        try
        {
            await next(context);
        }
        catch (Exception exception) when (!context.RequestAborted.IsCancellationRequested)
        {
            LogEscaped(logger, ExceptionRedaction.Describe(exception));
            if (context.Response.HasStarted)
            {
                context.Abort();
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            }
        }
        finally
        {
            LogRequest(logger, MethodOf(context.Request), RouteOf(context), context.Response.StatusCode, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    // Any other method is a token the client chose.
    private static string MethodOf(HttpRequest request) =>
        StandardMethods.Contains(request.Method) ? request.Method : "OTHER";

    // Path values and query strings are client-supplied; the route template is not. The exception handler clears the
    // endpoint before it answers, so the endpoint that failed is read from its feature.
    private static string RouteOf(HttpContext context) =>
        (context.GetEndpoint() ?? context.Features.Get<IExceptionHandlerFeature>()?.Endpoint) is RouteEndpoint endpoint
            ? endpoint.RoutePattern.RawText ?? "(unnamed route)"
            : "(no route)";

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "{Method} {Route} answered {StatusCode} in {ElapsedMilliseconds:0.0} ms")]
    private static partial void LogRequest(ILogger logger, string method, string route, int statusCode, double elapsedMilliseconds);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Exception after the response started: {ExceptionDescription}")]
    private static partial void LogEscaped(ILogger logger, string exceptionDescription);
}

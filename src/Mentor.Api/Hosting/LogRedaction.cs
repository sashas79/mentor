namespace Mentor.Api.Hosting;

/// <summary>
/// The logging redaction policy of the API host (NFR-002, NFR-009). Logs are structured JSON and never carry
/// request or response bodies, query strings, raw paths, header values or exception messages, because any of them
/// can hold personally attributable development content or free text about a meeting. A request is identified by
/// its method, matched route template, status and trace id (<see cref="RequestLoggingMiddleware"/>), and an
/// exception by its types and stack trace (<see cref="ExceptionRedaction"/>). Modules log identifiers and outcomes
/// only.
/// </summary>
internal static class LogRedaction
{
    public static ILoggingBuilder AddRedactedLogging(this ILoggingBuilder logging)
    {
        logging.ClearProviders();
        logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.UseUtcTimestamp = true;
            options.TimestampFormat = "O";
        });

        // Below Warning, ASP.NET Core logs request URLs with their query strings, raw paths and bound parameter values.
        logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        // Hosting also opens a scope holding the raw path of every request; it opens none while its category is off.
        logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.None);
        // The exception middlewares log exception messages; RedactingExceptionHandler logs the redacted form instead.
        logging.AddFilter("Microsoft.AspNetCore.Diagnostics", LogLevel.None);
        return logging;
    }
}

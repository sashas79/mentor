using Microsoft.AspNetCore.Diagnostics;

namespace Mentor.Api.Hosting;

/// <summary>
/// Answers an unhandled exception with an RFC 9457 problem that carries no exception detail, and logs a server
/// fault in its redacted form (<see cref="ExceptionRedaction"/>).
/// </summary>
internal sealed partial class RedactingExceptionHandler(IProblemDetailsService problemDetails, ILogger<RedactingExceptionHandler> logger)
    : IExceptionHandler
{
    public ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var status = exception is BadHttpRequestException badRequest ? badRequest.StatusCode : StatusCodes.Status500InternalServerError;
        if (status >= StatusCodes.Status500InternalServerError)
        {
            LogUnhandled(logger, ExceptionRedaction.Describe(exception));
        }

        httpContext.Response.StatusCode = status;
        return problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = { Status = status } });
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Unhandled exception: {ExceptionDescription}")]
    private static partial void LogUnhandled(ILogger logger, string exceptionDescription);
}

using System.Text;

namespace Mentor.Api.Hosting;

/// <summary>
/// The loggable form of an exception: the type and stack trace of every exception in its chain. Messages are left
/// out because they can quote request values; a FormatException, for one, repeats the text it could not parse.
/// </summary>
internal static class ExceptionRedaction
{
    public static string Describe(Exception exception)
    {
        var description = new StringBuilder();
        Append(description, exception);
        return description.ToString();
    }

    private static void Append(StringBuilder description, Exception exception)
    {
        description.Append(exception.GetType().FullName);
        if (exception.StackTrace is { } stackTrace)
        {
            description.AppendLine().Append(stackTrace);
        }

        IEnumerable<Exception> innerExceptions = exception switch
        {
            AggregateException aggregate => aggregate.InnerExceptions,
            { InnerException: { } inner } => [inner],
            _ => [],
        };
        foreach (var inner in innerExceptions)
        {
            description.AppendLine().Append(" ---> ");
            Append(description, inner);
        }
    }
}

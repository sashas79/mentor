using Mentor.Api.Hosting;

namespace Mentor.Tests.Unit.Hosting;

public sealed class ExceptionRedactionTests
{
    // Stands for free text a participant could type; an exception message may repeat it.
    private const string Note = "meeting-note-5f1c9e";

    [Fact]
    public void Describe_keeps_every_type_and_stack_trace_and_drops_every_message()
    {
        var exception = Thrown(() => throw new InvalidOperationException(Note, Thrown(() => throw new FormatException(Note))));

        var description = ExceptionRedaction.Describe(exception);

        Assert.DoesNotContain(Note, description);
        Assert.Contains(typeof(InvalidOperationException).FullName!, description);
        Assert.Contains(typeof(FormatException).FullName!, description);
        Assert.Contains(nameof(Describe_keeps_every_type_and_stack_trace_and_drops_every_message), description);
    }

    [Fact]
    public void Describe_walks_every_exception_of_an_aggregate()
    {
        var exception = new AggregateException(Note, new ArgumentException(Note), new TimeoutException(Note));

        var description = ExceptionRedaction.Describe(exception);

        Assert.DoesNotContain(Note, description);
        Assert.Contains(typeof(ArgumentException).FullName!, description);
        Assert.Contains(typeof(TimeoutException).FullName!, description);
    }

    private static Exception Thrown(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            return exception;
        }

        throw new InvalidOperationException("The action did not throw.");
    }
}

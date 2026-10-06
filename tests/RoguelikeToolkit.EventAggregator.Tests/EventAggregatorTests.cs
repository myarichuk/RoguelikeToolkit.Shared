using RoguelikeToolkit.EventAggregator;
using Xunit;

namespace RoguelikeToolkit.EventAggregator.Tests;

public sealed class EventAggregatorTests
{
    private sealed record TestEvent(string Message);

    [Fact]
    public void Publish_DeliversEventToSubscriber()
    {
        var aggregator = new EventAggregator();
        string? received = null;
        using var _ = aggregator.Subscribe<TestEvent>(e => received = e.Message);

        aggregator.Publish(new TestEvent("hello"));

        Assert.Equal("hello", received);
    }

    [Fact]
    public void Dispose_UnsubscribesHandler()
    {
        var aggregator = new EventAggregator();
        var count = 0;
        var subscription = aggregator.Subscribe<TestEvent>(_ => count++);

        aggregator.Publish(new TestEvent("one"));
        subscription.Dispose();
        aggregator.Publish(new TestEvent("two"));

        Assert.Equal(1, count);
    }

    [Fact]
    public void Publish_WithNoSubscribers_DoesNotThrow()
    {
        var aggregator = new EventAggregator();

        var exception = Record.Exception(() => aggregator.Publish(new TestEvent("nobody")));

        Assert.Null(exception);
    }
}

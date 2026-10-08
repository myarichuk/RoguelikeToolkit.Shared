using RoguelikeToolkit.EventAggregator;
using Xunit;

namespace RoguelikeToolkit.EventAggregator.Tests;

public sealed class EventAggregatorTests
{
    private sealed record TestEvent(string Message);

    private sealed record OtherEvent(int Value);

    private readonly record struct StructEvent(int Value);

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

    [Fact]
    public void Publish_OnlyNotifiesMatchingEventType()
    {
        var aggregator = new EventAggregator();
        var testCount = 0;
        var otherCount = 0;
        using var _1 = aggregator.Subscribe<TestEvent>(_ => testCount++);
        using var _2 = aggregator.Subscribe<OtherEvent>(_ => otherCount++);

        aggregator.Publish(new TestEvent("hello"));

        Assert.Equal(1, testCount);
        Assert.Equal(0, otherCount);
    }

    [Fact]
    public void Publish_DeliversStructEventToSubscriber()
    {
        var aggregator = new EventAggregator();
        var received = new List<int>();
        using var _ = aggregator.Subscribe<StructEvent>(e => received.Add(e.Value));

        aggregator.Publish(new StructEvent(41));
        aggregator.Publish(new StructEvent(1));

        Assert.Equal(new[] { 41, 1 }, received);
    }

    [Fact]
    public void Publish_DoesNotConfuseStructAndClassEvents()
    {
        var aggregator = new EventAggregator();
        var structCount = 0;
        var classCount = 0;
        using var _1 = aggregator.Subscribe<StructEvent>(_ => structCount++);
        using var _2 = aggregator.Subscribe<TestEvent>(_ => classCount++);

        aggregator.Publish(new StructEvent(7));

        Assert.Equal(1, structCount);
        Assert.Equal(0, classCount);
    }

    [Fact]
    public void Dispose_CalledTwice_RemainsUnsubscribed()
    {
        var aggregator = new EventAggregator();
        var count = 0;
        var subscription = aggregator.Subscribe<TestEvent>(_ => count++);

        aggregator.Publish(new TestEvent("one"));
        subscription.Dispose();

        var exception = Record.Exception(() => subscription.Dispose());
        aggregator.Publish(new TestEvent("two"));

        Assert.Null(exception);
        Assert.Equal(1, count);
    }

    [Fact]
    public void Subscribe_NullHandler_ThrowsArgumentNullException()
    {
        var aggregator = new EventAggregator();

        var ex = Assert.Throws<ArgumentNullException>(() => aggregator.Subscribe<TestEvent>(null!));

        Assert.Equal("handler", ex.ParamName);
    }

    [Fact]
    public void Dispose_OneOfMultipleSubscriptions_LeavesOthersSubscribed()
    {
        var aggregator = new EventAggregator();
        var firstCount = 0;
        var secondCount = 0;
        using var _ = aggregator.Subscribe<TestEvent>(_ => firstCount++);
        var second = aggregator.Subscribe<TestEvent>(_ => secondCount++);

        aggregator.Publish(new TestEvent("one"));
        second.Dispose();
        aggregator.Publish(new TestEvent("two"));

        Assert.Equal(2, firstCount);
        Assert.Equal(1, secondCount);
    }
}

public sealed class SubscriberManagerTests
{
    private sealed record TestEvent(string Message);

    private sealed record OtherEvent(int Value);

    [Fact]
    public void TryGetChannel_ReturnsFalseBeforeAnySubscription()
    {
        var manager = new SubscriberManager();

        Assert.False(manager.TryGetChannel<TestEvent>(out _));
        Assert.Equal(0, manager.SubscriberCount<TestEvent>());
    }

    [Fact]
    public void Subscribe_CreatesChannelKeyedByEventType()
    {
        var manager = new SubscriberManager();
        using var _ = manager.Subscribe<TestEvent>(_ => { });

        Assert.True(manager.TryGetChannel<TestEvent>(out var channel));
        Assert.NotNull(channel);
        Assert.Equal(1, channel!.SubscriberCount);
        Assert.False(manager.TryGetChannel<OtherEvent>(out var otherChannel));
        Assert.Null(otherChannel);
    }

    [Fact]
    public void GetOrCreateChannel_ReturnsSameInstanceForSameType()
    {
        var manager = new SubscriberManager();

        var first = manager.GetOrCreateChannel<TestEvent>();
        var second = manager.GetOrCreateChannel<TestEvent>();

        Assert.Same(first, second);
    }

    [Fact]
    public void Publish_DispatchesToSubscribersOfExactType()
    {
        var manager = new SubscriberManager();
        string? received = null;
        var otherCount = 0;
        using var _1 = manager.Subscribe<TestEvent>(e => received = e.Message);
        using var _2 = manager.Subscribe<OtherEvent>(_ => otherCount++);

        manager.Publish(new TestEvent("hello"));

        Assert.Equal("hello", received);
        Assert.Equal(0, otherCount);
    }

    [Fact]
    public void Unsubscribe_RemovesHandlerFromTypeChannel()
    {
        var manager = new SubscriberManager();
        var count = 0;
        void Handler(TestEvent _) => count++;
        manager.Subscribe<TestEvent>(Handler);

        Assert.True(manager.Unsubscribe<TestEvent>(Handler));
        manager.Publish(new TestEvent("hello"));

        Assert.Equal(0, count);
        Assert.Equal(0, manager.SubscriberCount<TestEvent>());
    }

    [Fact]
    public void Unsubscribe_UnknownHandler_ReturnsFalse()
    {
        var manager = new SubscriberManager();
        void Handler(TestEvent _) { }
        using var _ = manager.Subscribe<TestEvent>(_ => { });

        Assert.False(manager.Unsubscribe<TestEvent>(Handler));
        Assert.Equal(1, manager.SubscriberCount<TestEvent>());
    }

    [Fact]
    public void Unsubscribe_NoChannel_ReturnsFalse()
    {
        var manager = new SubscriberManager();
        void Handler(TestEvent _) { }

        Assert.False(manager.Unsubscribe<TestEvent>(Handler));
    }

    [Fact]
    public void Unsubscribe_Twice_SecondReturnsFalse()
    {
        var manager = new SubscriberManager();
        void Handler(TestEvent _) { }
        manager.Subscribe<TestEvent>(Handler);

        Assert.True(manager.Unsubscribe<TestEvent>(Handler));
        Assert.False(manager.Unsubscribe<TestEvent>(Handler));
        Assert.Equal(0, manager.SubscriberCount<TestEvent>());
    }

    [Fact]
    public void Unsubscribe_DoesNotAffectOtherEventType()
    {
        var manager = new SubscriberManager();
        var testCount = 0;
        var otherCount = 0;
        void TestHandler(TestEvent _) => testCount++;
        using var _ = manager.Subscribe<TestEvent>(TestHandler);
        using var _2 = manager.Subscribe<OtherEvent>(_ => otherCount++);

        Assert.True(manager.Unsubscribe<TestEvent>(TestHandler));
        manager.Publish(new TestEvent("hello"));
        manager.Publish(new OtherEvent(1));

        Assert.Equal(0, testCount);
        Assert.Equal(1, otherCount);
        Assert.Equal(0, manager.SubscriberCount<TestEvent>());
        Assert.Equal(1, manager.SubscriberCount<OtherEvent>());
    }

    [Fact]
    public void Subscribe_NullHandler_ThrowsArgumentNullException()
    {
        var manager = new SubscriberManager();

        var ex = Assert.Throws<ArgumentNullException>(() => manager.Subscribe<TestEvent>(null!));

        Assert.Equal("handler", ex.ParamName);
    }

    [Fact]
    public void Unsubscribe_NullHandler_ThrowsArgumentNullException()
    {
        var manager = new SubscriberManager();

        var ex = Assert.Throws<ArgumentNullException>(() => manager.Unsubscribe<TestEvent>(null!));

        Assert.Equal("handler", ex.ParamName);
    }
}

public sealed class EventChannelTests
{
    private sealed record TestEvent(string Message);

    [Fact]
    public void Publish_RunsAllHandlersAndThrowsAggregateException()
    {
        var channel = new EventChannel<TestEvent>();
        var calls = new List<string>();
        var first = new InvalidOperationException("first");
        var second = new ArgumentException("second");
        channel.Subscribe(_ => { calls.Add("first"); throw first; });
        channel.Subscribe(_ => { calls.Add("second"); throw second; });
        channel.Subscribe(_ => calls.Add("third"));

        var ex = Assert.Throws<AggregateException>(() => channel.Publish(new TestEvent("hello")));

        Assert.Equal(new[] { "first", "second", "third" }, calls);
        Assert.Equal(new Exception[] { first, second }, ex.InnerExceptions);
    }

    [Fact]
    public void Publish_WrapsSingleFailureInAggregateException()
    {
        var channel = new EventChannel<TestEvent>();
        var failure = new InvalidOperationException("boom");
        channel.Subscribe(_ => throw failure);

        var ex = Assert.Throws<AggregateException>(() => channel.Publish(new TestEvent("hello")));

        Assert.Single(ex.InnerExceptions);
        Assert.Same(failure, ex.InnerExceptions[0]);
    }

    [Fact]
    public void Subscribe_NullHandler_ThrowsArgumentNullException()
    {
        var channel = new EventChannel<TestEvent>();

        var ex = Assert.Throws<ArgumentNullException>(() => channel.Subscribe(null!));

        Assert.Equal("handler", ex.ParamName);
    }

    [Fact]
    public void Unsubscribe_NullHandler_ThrowsArgumentNullException()
    {
        var channel = new EventChannel<TestEvent>();

        var ex = Assert.Throws<ArgumentNullException>(() => channel.Unsubscribe(null!));

        Assert.Equal("handler", ex.ParamName);
    }

    [Fact]
    public void Unsubscribe_RemovesHandler_StopsDelivery()
    {
        var channel = new EventChannel<TestEvent>();
        var count = 0;
        void Handler(TestEvent _) => count++;
        channel.Subscribe(Handler);

        Assert.True(channel.Unsubscribe(Handler));
        Assert.Equal(0, channel.SubscriberCount);
        channel.Publish(new TestEvent("hello"));

        Assert.Equal(0, count);
    }

    [Fact]
    public void Unsubscribe_UnknownHandler_ReturnsFalse()
    {
        var channel = new EventChannel<TestEvent>();
        void Handler(TestEvent _) { }
        channel.Subscribe(_ => { });

        Assert.False(channel.Unsubscribe(Handler));
        Assert.Equal(1, channel.SubscriberCount);
    }

    [Fact]
    public void Unsubscribe_Twice_SecondReturnsFalse()
    {
        var channel = new EventChannel<TestEvent>();
        void Handler(TestEvent _) { }
        channel.Subscribe(Handler);

        Assert.True(channel.Unsubscribe(Handler));
        Assert.False(channel.Unsubscribe(Handler));
    }

    [Fact]
    public void Unsubscribe_OneOfMultiple_LeavesOthersSubscribed()
    {
        var channel = new EventChannel<TestEvent>();
        var firstCount = 0;
        var secondCount = 0;
        void First(TestEvent _) => firstCount++;
        void Second(TestEvent _) => secondCount++;
        channel.Subscribe(First);
        channel.Subscribe(Second);

        Assert.True(channel.Unsubscribe(First));
        Assert.Equal(1, channel.SubscriberCount);
        channel.Publish(new TestEvent("hello"));

        Assert.Equal(0, firstCount);
        Assert.Equal(1, secondCount);
    }

    [Fact]
    public void Dispose_UnsubscribesHandler()
    {
        var channel = new EventChannel<TestEvent>();
        var count = 0;
        var subscription = channel.Subscribe(_ => count++);

        channel.Publish(new TestEvent("one"));
        subscription.Dispose();
        channel.Publish(new TestEvent("two"));

        Assert.Equal(1, count);
        Assert.Equal(0, channel.SubscriberCount);
    }

    [Fact]
    public void Dispose_CalledTwice_SecondDisposeIsNoOp()
    {
        var channel = new EventChannel<TestEvent>();
        var count = 0;
        var subscription = channel.Subscribe(_ => count++);

        subscription.Dispose();

        var exception = Record.Exception(() => subscription.Dispose());
        channel.Publish(new TestEvent("hello"));

        Assert.Null(exception);
        Assert.Equal(0, count);
        Assert.Equal(0, channel.SubscriberCount);
    }
}

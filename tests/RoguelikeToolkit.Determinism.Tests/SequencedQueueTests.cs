using RoguelikeToolkit.Determinism;

namespace RoguelikeToolkit.Determinism.Tests;

public sealed class SequencedQueueTests
{
    private sealed record Item(long Sequence, string Name);

    private static Item Make(SequencedQueue<Item> q, string name) => q.Enqueue(seq => new Item(seq, name));

    [Fact]
    public void Enqueue_AssignsConsecutiveSequences_StartingAtTheConfiguredOne()
    {
        var q = new SequencedQueue<Item>(firstSequence: 10);

        Assert.Equal(10, Make(q, "a").Sequence);
        Assert.Equal(11, Make(q, "b").Sequence);
        Assert.Equal(12, q.NextSequence);
        Assert.Equal(2, q.Count);
    }

    [Fact]
    public void Drain_DispatchesInOrder_AndReturnsTheCount()
    {
        var q = new SequencedQueue<Item>();
        Make(q, "a");
        Make(q, "b");
        Make(q, "c");
        var seen = new List<string>();

        var n = q.Drain(i => seen.Add(i.Name));

        Assert.Equal(3, n);
        Assert.Equal(new[] { "a", "b", "c" }, seen);
        Assert.Equal(0, q.Count);
        Assert.False(q.IsDraining);
    }

    [Fact]
    public void Drain_OnAnEmptyQueue_ReturnsZero()
    {
        Assert.Equal(0, new SequencedQueue<Item>().Drain(_ => { }));
    }

    [Fact]
    public void ItemsEnqueuedByAHandler_AreProcessedInTheSameDrain_AfterTheOnesAlreadyQueued()
    {
        var q = new SequencedQueue<Item>();
        Make(q, "a");
        Make(q, "b");
        var seen = new List<string>();

        q.Drain(i =>
        {
            seen.Add(i.Name);
            if (i.Name == "a")
            {
                Make(q, "from-a");
            }
        });

        Assert.Equal(new[] { "a", "b", "from-a" }, seen);
    }

    [Fact]
    public async Task ConcurrentEnqueue_SequenceOrderEqualsQueueOrder_AndNothingIsLost()
    {
        const int threads = 8;
        const int perThread = 2_000;
        var q = new SequencedQueue<Item>();

        await Task.WhenAll(Enumerable.Range(0, threads).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < perThread; i++)
            {
                Make(q, $"{t}:{i}");
            }
        }, TestContext.Current.CancellationToken)));

        var seen = new List<Item>();
        q.Drain(seen.Add, maxItems: threads * perThread);

        Assert.Equal(threads * perThread, seen.Count);
        Assert.Equal(Enumerable.Range(1, threads * perThread).Select(i => (long)i), seen.Select(i => i.Sequence));
        // Per producer, arrival order is preserved.
        for (var t = 0; t < threads; t++)
        {
            var mine = seen.Where(i => i.Name.StartsWith($"{t}:", StringComparison.Ordinal))
                .Select(i => int.Parse(i.Name.Split(':')[1])).ToList();
            Assert.Equal(Enumerable.Range(0, perThread), mine);
        }
    }

    [Fact]
    public async Task EnqueueFromOtherThreads_WhileDraining_IsSafe()
    {
        var q = new SequencedQueue<Item>();
        Make(q, "seed");
        using var started = new ManualResetEventSlim();
        using var done = new ManualResetEventSlim();
        var producer = Task.Run(() =>
        {
            started.Wait(TestContext.Current.CancellationToken);
            for (var i = 0; i < 1_000; i++)
            {
                Make(q, "p");
            }

            done.Set();
        }, TestContext.Current.CancellationToken);
        var total = 0;

        total += q.Drain(_ => started.Set());
        await producer;
        total += q.Drain(_ => { });

        Assert.Equal(1_001, total);
    }

    // ---- error policy ----------------------------------------------------------------------------------------------

    [Fact]
    public void ThrowingHandler_DoesNotStopTheDrain_AndFailuresSurfaceTogetherAfterwards()
    {
        var q = new SequencedQueue<Item>();
        Make(q, "a");
        Make(q, "bad1");
        Make(q, "c");
        Make(q, "bad2");
        var seen = new List<string>();

        var e = Assert.Throws<AggregateException>(() => q.Drain(i =>
        {
            seen.Add(i.Name);
            if (i.Name.StartsWith("bad", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(i.Name);
            }
        }));

        Assert.Equal(new[] { "a", "bad1", "c", "bad2" }, seen);
        Assert.Equal(new[] { "bad1", "bad2" }, e.InnerExceptions.Select(x => x.Message));
        Assert.False(q.IsDraining);
        Assert.Equal(0, q.Count);
    }

    [Fact]
    public void OnError_ReceivesEachFailure_AndNothingIsThrown()
    {
        var q = new SequencedQueue<Item>();
        Make(q, "a");
        Make(q, "bad");
        var errors = new List<(string, string)>();

        var n = q.Drain(
            i =>
            {
                if (i.Name == "bad")
                {
                    throw new InvalidOperationException("boom");
                }
            },
            onError: (item, ex) => errors.Add((item.Name, ex.Message)));

        Assert.Equal(2, n);
        Assert.Equal(new[] { ("bad", "boom") }, errors);
    }

    // ---- guards ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Drain_FromInsideAHandler_Throws_AndTheOuterDrainReportsIt()
    {
        var q = new SequencedQueue<Item>();
        Make(q, "a");

        var e = Assert.Throws<AggregateException>(() => q.Drain(_ => q.Drain(__ => { })));

        Assert.IsType<InvalidOperationException>(Assert.Single(e.InnerExceptions));
    }

    [Fact]
    public void RunawayHandler_IsStopped_AndTheRestStaysQueued()
    {
        var q = new SequencedQueue<Item>();
        Make(q, "loop");

        var e = Assert.Throws<InvalidOperationException>(() => q.Drain(_ => Make(q, "loop"), maxItems: 100));

        Assert.Contains("100", e.Message);
        Assert.Equal(1, q.Count);
        Assert.False(q.IsDraining);
        // The queue is usable again.
        Assert.Equal(1, q.Drain(_ => { }));
    }

    [Fact]
    public void RunawayAndHandlerFailures_ArriveTogether()
    {
        var q = new SequencedQueue<Item>();
        Make(q, "loop");

        var e = Assert.Throws<AggregateException>(() => q.Drain(
            _ =>
            {
                Make(q, "loop");
                throw new ArgumentException("handler");
            },
            maxItems: 3));

        Assert.Equal(4, e.InnerExceptions.Count);
        Assert.IsType<InvalidOperationException>(e.InnerExceptions[^1]);
    }

    [Fact]
    public void InvalidArguments_Throw()
    {
        var q = new SequencedQueue<Item>();

        Assert.Throws<ArgumentNullException>(() => q.Enqueue(null!));
        Assert.Throws<ArgumentNullException>(() => q.Drain(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => q.Drain(_ => { }, maxItems: 0));
    }

    // ---- snapshot --------------------------------------------------------------------------------------------------

    [Fact]
    public void CaptureAndRestore_RoundTripsPendingItemsAndTheSequenceCounter()
    {
        var q = new SequencedQueue<Item>();
        Make(q, "a");
        Make(q, "b");
        q.Drain(_ => { });
        Make(q, "c");
        Make(q, "d");

        var snap = q.Capture();
        var other = new SequencedQueue<Item>();
        other.Restore(snap);
        var seen = new List<Item>();
        other.Drain(seen.Add);

        Assert.Equal(5, snap.NextSequence);
        Assert.Equal(new[] { "c", "d" }, seen.Select(i => i.Name));
        Assert.Equal(new long[] { 3, 4 }, seen.Select(i => i.Sequence));
        Assert.Equal(5, Make(other, "e").Sequence);
    }

    [Fact]
    public void Capture_DoesNotConsumeOrAlias()
    {
        var q = new SequencedQueue<Item>();
        Make(q, "a");

        var snap = q.Capture();
        Make(q, "b");

        Assert.Single(snap.Pending);
        Assert.Equal(2, q.Count);
    }

    [Fact]
    public void Restore_ReplacesExistingContents()
    {
        var q = new SequencedQueue<Item>();
        Make(q, "old");

        q.Restore(new SequencedQueueSnapshot<Item>(50, new[] { new Item(49, "kept") }));

        Assert.Equal(50, q.NextSequence);
        var seen = new List<string>();
        q.Drain(i => seen.Add(i.Name));
        Assert.Equal(new[] { "kept" }, seen);
    }

    [Fact]
    public void Restore_OfADefaultSnapshot_EmptiesTheQueue()
    {
        var q = new SequencedQueue<Item>();
        Make(q, "a");

        q.Restore(default);

        Assert.Equal(0, q.Count);
        Assert.Equal(0, q.NextSequence);
    }

    [Fact]
    public void CaptureAndRestore_DuringADrain_AreRefused()
    {
        var q = new SequencedQueue<Item>();
        Make(q, "a");
        var captured = new List<Exception>();

        q.Drain(_ =>
        {
            captured.Add(Assert.Throws<InvalidOperationException>(() => q.Capture()));
            captured.Add(Assert.Throws<InvalidOperationException>(() => q.Restore(default)));
        });

        Assert.Equal(2, captured.Count);
    }
}

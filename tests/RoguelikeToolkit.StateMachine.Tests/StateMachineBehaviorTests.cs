using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RoguelikeToolkit.StateMachine;
using Xunit;

namespace RoguelikeToolkit.StateMachine.Tests;

public sealed class StateMachineBehaviorTests
{
    public enum S
    {
        Start,
        Rich,
        Poor,
        Middle,
        End,
    }

    public enum T
    {
        Go,
        Next,
        Other,
    }

    private sealed class Ctx
    {
        public int Gold;
        public readonly List<string> Log = new();
    }

    private sealed class Recorder : IStateTransitionSink<S, T, Ctx>
    {
        public readonly List<StateTransition<S, T>> Seen = new();

        public void OnTransition(StateTransition<S, T> transition, Ctx context) => Seen.Add(transition);
    }

    // ---- branching -------------------------------------------------------------------------

    [Theory]
    [InlineData(100, S.Rich)]
    [InlineData(10, S.Middle)]
    [InlineData(0, S.Poor)]
    public void Permit_OrderedBranches_FirstPassingGuardWins(int gold, S expected)
    {
        var machine = new StateMachine<S, T, Ctx>(S.Start, new Ctx { Gold = gold });
        machine.Configure(S.Start)
            .Permit(T.Go, S.Rich, c => c.Gold > 50)
            .Permit(T.Go, S.Middle, c => c.Gold > 0)
            .Permit(T.Go, S.Poor);

        machine.Fire(T.Go);

        Assert.Equal(expected, machine.State);
    }

    [Fact]
    public void Permit_AllBranchesGuarded_NoneMatches_ThrowsGuardBlocked_AndCanFireIsFalse()
    {
        var machine = new StateMachine<S, T, Ctx>(S.Start, new Ctx { Gold = 0 });
        machine.Configure(S.Start).Permit(T.Go, S.Rich, c => c.Gold > 50).Permit(T.Go, S.Middle, c => c.Gold > 10);

        Assert.False(machine.CanFire(T.Go));
        var e = Assert.Throws<InvalidOperationException>(() => machine.Fire(T.Go));
        Assert.Contains("Guard blocked", e.Message);
        Assert.Equal(S.Start, machine.State);
    }

    [Fact]
    public void Permit_SecondUnguardedBranch_ThrowsAndNamesTheExistingOne()
    {
        var machine = new StateMachine<S, T, Ctx>(S.Start, new Ctx());
        machine.Configure(S.Start).Permit(T.Go, S.Rich);

        var e = Assert.Throws<InvalidOperationException>(() => machine.Configure(S.Start).Permit(T.Go, S.Poor));

        Assert.Contains("Rich", e.Message);
    }

    [Fact]
    public void TwoArgumentFacade_ConfiguringTheSamePairTwice_Throws()
    {
        var machine = new StateMachine<S, T>(S.Start).Configure(S.Start, T.Go, S.Rich);

        Assert.Throws<InvalidOperationException>(() => machine.Configure(S.Start, T.Go, S.Poor));
    }

    [Fact]
    public void GetPermittedTriggers_ReflectsCurrentStateGuardsAndIgnores()
    {
        var ctx = new Ctx { Gold = 0 };
        var machine = new StateMachine<S, T, Ctx>(S.Start, ctx);
        machine.Configure(S.Start)
            .Permit(T.Go, S.Rich, c => c.Gold > 0)
            .Permit(T.Next, S.Middle)
            .Ignore(T.Other);
        machine.Configure(S.Middle).Permit(T.Go, S.End);

        Assert.Equal(new[] { T.Next }, machine.GetPermittedTriggers().ToArray());

        ctx.Gold = 5;
        Assert.Equal(new[] { T.Go, T.Next }, machine.GetPermittedTriggers().OrderBy(t => t).ToArray());
    }

    [Fact]
    public void IsIn_ComparesCurrentState()
    {
        var machine = new StateMachine<S, T, Ctx>(S.Start, new Ctx());

        Assert.True(machine.IsIn(S.Start));
        Assert.False(machine.IsIn(S.End));
    }

    // ---- commit semantics ----------------------------------------------------------------

    [Fact]
    public void ExitThrowing_CommitsNothing_AndSkipsSink()
    {
        var sink = new Recorder();
        var machine = new StateMachine<S, T, Ctx>(S.Start, new Ctx(), sink);
        machine.Configure(S.Start).Permit(T.Go, S.Rich).OnExit(_ => throw new InvalidOperationException("exit boom"));

        Assert.Throws<InvalidOperationException>(() => machine.Fire(T.Go));

        Assert.Equal(S.Start, machine.State);
        Assert.Equal(0, machine.Sequence);
        Assert.Empty(sink.Seen);
    }

    [Fact]
    public void GuardThrowing_CommitsNothing()
    {
        var machine = new StateMachine<S, T, Ctx>(S.Start, new Ctx());
        machine.Configure(S.Start).Permit(T.Go, S.Rich, _ => throw new InvalidOperationException("guard boom"));

        Assert.Throws<InvalidOperationException>(() => machine.Fire(T.Go));

        Assert.Equal(S.Start, machine.State);
    }

    // ---- start / restore -----------------------------------------------------------------

    [Fact]
    public void Start_RunsInitialStateEntryActionsOnce()
    {
        var ctx = new Ctx();
        var machine = new StateMachine<S, T, Ctx>(S.Start, ctx);
        machine.Configure(S.Start).OnEntry(c => c.Log.Add("entered start"));

        Assert.Empty(ctx.Log);
        machine.Start();

        Assert.Equal(new[] { "entered start" }, ctx.Log);
        Assert.Equal(0, machine.Sequence);
        Assert.Throws<InvalidOperationException>(() => machine.Start());
    }

    [Fact]
    public void Start_EntryActionThatFires_IsQueuedAndRuns()
    {
        StateMachine<S, T, Ctx>? machine = null;
        machine = new StateMachine<S, T, Ctx>(S.Start, new Ctx());
        machine.Configure(S.Start).Permit(T.Next, S.Middle).OnEntry(_ => machine!.Fire(T.Next));

        machine.Start();

        Assert.Equal(S.Middle, machine.State);
        Assert.Equal(1, machine.Sequence);
    }

    [Fact]
    public void Restore_SetsStateAndSequence_WithoutRunningActionsOrSink()
    {
        var sink = new Recorder();
        var ctx = new Ctx();
        var machine = new StateMachine<S, T, Ctx>(S.Start, ctx, sink);
        machine.Configure(S.End).OnEntry(c => c.Log.Add("entered end"));
        machine.Configure(S.End).Permit(T.Go, S.Start);

        machine.Restore(S.End, 41);

        Assert.Equal(S.End, machine.State);
        Assert.Equal(41, machine.Sequence);
        Assert.Empty(ctx.Log);
        Assert.Empty(sink.Seen);
        machine.Fire(T.Go);
        Assert.Equal(42, sink.Seen.Single().Sequence);
    }

    [Fact]
    public void Restore_FromInsideACallback_Throws()
    {
        StateMachine<S, T, Ctx>? machine = null;
        machine = new StateMachine<S, T, Ctx>(S.Start, new Ctx());
        machine.Configure(S.Start).Permit(T.Go, S.Rich);
        machine.Configure(S.Rich).OnEntry(_ => machine!.Restore(S.End, 0));

        Assert.Throws<InvalidOperationException>(() => machine.Fire(T.Go));
    }

    // ---- queue / re-entrancy -------------------------------------------------------------

    [Fact]
    public void QueuedTrigger_ThatIsNotPermitted_DoesNotStrandTheOnesBehindIt()
    {
        StateMachine<S, T, Ctx>? machine = null;
        machine = new StateMachine<S, T, Ctx>(S.Start, new Ctx());
        machine.Configure(S.Start).Permit(T.Go, S.Rich);
        machine.Configure(S.Rich).Permit(T.Next, S.End).OnEntry(_ =>
        {
            machine!.Fire(T.Other); // not permitted from Rich
            machine.Fire(T.Next);
        });

        var e = Assert.Throws<InvalidOperationException>(() => machine.Fire(T.Go));

        Assert.Contains("Cannot fire trigger 'Other'", e.Message);
        Assert.Equal(S.End, machine.State);
    }

    [Fact]
    public void SeveralFailures_SurfaceAsOneAggregate()
    {
        StateMachine<S, T, Ctx>? machine = null;
        machine = new StateMachine<S, T, Ctx>(S.Start, new Ctx());
        machine.Configure(S.Start).Permit(T.Go, S.Rich);
        machine.Configure(S.Rich).OnEntry(_ =>
        {
            machine!.Fire(T.Other);
            machine.Fire(T.Other);
        });

        var aggregate = Assert.Throws<AggregateException>(() => machine.Fire(T.Go));

        Assert.Equal(2, aggregate.InnerExceptions.Count);
    }

    [Fact]
    public void Configuring_FromInsideACallback_DoesNotDisturbTheRunningTransition()
    {
        StateMachine<S, T, Ctx>? machine = null;
        var ctx = new Ctx();
        machine = new StateMachine<S, T, Ctx>(S.Start, ctx);
        machine.Configure(S.Start).Permit(T.Go, S.Rich);
        machine.Configure(S.Rich).OnEntry(c =>
        {
            c.Log.Add("first");
            machine!.Configure(S.Rich).OnEntry(x => x.Log.Add("added mid-flight"));
        });

        machine.Fire(T.Go);

        Assert.Equal(new[] { "first" }, ctx.Log);
    }

    // ---- threading ---------------------------------------------------------------------

    [Fact]
    public void Post_DoesNothingUntilPumped_ThenRunsInPostingOrder()
    {
        var ctx = new Ctx();
        var machine = new StateMachine<S, T, Ctx>(S.Start, ctx);
        machine.Configure(S.Start).Permit(T.Go, S.Middle);
        machine.Configure(S.Middle).Permit(T.Next, S.End).OnEntry(c => c.Log.Add("middle"));
        machine.Configure(S.End).OnEntry(c => c.Log.Add("end"));

        machine.Post(T.Go);
        machine.Post(T.Next);

        Assert.Equal(S.Start, machine.State);
        machine.Pump();
        Assert.Equal(S.End, machine.State);
        Assert.Equal(new[] { "middle", "end" }, ctx.Log);
        machine.Pump(); // empty pump is a no-op
    }

    [Fact]
    public void Post_WakesTheOwnerOncePerBatch_AndAgainAfterAPump()
    {
        var wakes = 0;
        var machine = new StateMachine<S, T, Ctx>(S.Start, new Ctx(), onPosted: () => wakes++);
        machine.Configure(S.Start).Permit(T.Go, S.Middle);
        machine.Configure(S.Middle).Permit(T.Go, S.Start);

        machine.Post(T.Go);
        machine.Post(T.Go);
        machine.Post(T.Go);
        Assert.Equal(1, wakes);

        machine.Pump();
        machine.Post(T.Go);
        Assert.Equal(2, wakes);
    }

    [Fact]
    public void Fire_DrainsWhatWasPostedBeforeItReturns()
    {
        var machine = new StateMachine<S, T, Ctx>(S.Start, new Ctx());
        machine.Configure(S.Start).Permit(T.Go, S.Middle);
        machine.Configure(S.Middle).Permit(T.Next, S.End);

        machine.Post(T.Next);
        machine.Fire(T.Go);

        Assert.Equal(S.End, machine.State);
    }

    [Fact]
    public void Pump_FromInsideACallback_IsIgnored_AndOuterRunDrains()
    {
        StateMachine<S, T, Ctx>? machine = null;
        machine = new StateMachine<S, T, Ctx>(S.Start, new Ctx());
        machine.Configure(S.Start).Permit(T.Go, S.Middle);
        machine.Configure(S.Middle).Permit(T.Next, S.End).OnEntry(_ =>
        {
            machine!.Post(T.Next);
            machine.Pump();
        });

        machine.Fire(T.Go);

        Assert.Equal(S.End, machine.State);
    }

    [Fact]
    public async Task PostFromManyThreads_IsAppliedOnlyOnThePumpingThread()
    {
        const int posters = 8;
        const int perPoster = 500;
        var ctx = new Ctx();
        var machine = new StateMachine<S, T, Ctx>(S.Start, ctx);
        var applied = 0;
        var threads = new HashSet<int>();
        machine.Configure(S.Start).Permit(T.Go, S.Middle);
        machine.Configure(S.Middle).Permit(T.Go, S.Start);
        machine.Configure(S.Start).OnEntry(_ => { applied++; threads.Add(Environment.CurrentManagedThreadId); });
        machine.Configure(S.Middle).OnEntry(_ => { applied++; threads.Add(Environment.CurrentManagedThreadId); });

        await Task.WhenAll(Enumerable.Range(0, posters).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < perPoster; i++)
            {
                machine.Post(T.Go);
            }
        }, TestContext.Current.CancellationToken)));
        Assert.Equal(0, applied);

        machine.Pump();

        Assert.Equal(posters * perPoster, applied);
        Assert.Equal(posters * perPoster, machine.Sequence);
        Assert.Equal(new[] { Environment.CurrentManagedThreadId }, threads.ToArray());
    }

    [Fact]
    public async Task ConcurrentFire_IsSerialized_NoTransitionIsLostOrInterleaved()
    {
        const int threads = 8;
        const int perThread = 400;
        var inside = 0;
        var overlaps = 0;
        var machine = new StateMachine<S, T, Ctx>(S.Start, new Ctx());
        machine.Configure(S.Start).Permit(T.Go, S.Middle).OnExit(_ => Probe());
        machine.Configure(S.Middle).Permit(T.Go, S.Start).OnExit(_ => Probe());

        void Probe()
        {
            if (Interlocked.Increment(ref inside) != 1)
            {
                Interlocked.Increment(ref overlaps);
            }

            Thread.SpinWait(50);
            Interlocked.Decrement(ref inside);
        }

        await Task.WhenAll(Enumerable.Range(0, threads).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < perThread; i++)
            {
                machine.Fire(T.Go);
            }
        }, TestContext.Current.CancellationToken)));

        Assert.Equal(0, overlaps);
        Assert.Equal(threads * perThread, machine.Sequence);
    }

    [Fact]
    public async Task StateAndSequence_CanBeReadWhileATransitionBlocksInACallback()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var machine = new StateMachine<S, T, Ctx>(S.Start, new Ctx());
        machine.Configure(S.Start).Permit(T.Go, S.Middle);
        machine.Configure(S.Middle).Permit(T.Go, S.End).OnEntry(_ =>
        {
            entered.Set();
            release.Wait(TestContext.Current.CancellationToken);
        });

        var firing = Task.Run(() => machine.Fire(T.Go), TestContext.Current.CancellationToken);
        entered.Wait(TestContext.Current.CancellationToken);

        // The gate is held by the blocked entry action; lock-free members must still answer.
        Assert.Equal(S.Middle, machine.State);
        Assert.Equal(1, machine.Sequence);
        machine.Post(T.Go); // posted while the gate is held: must not block, and is drained by the running Fire

        release.Set();
        await firing;
        Assert.Equal(S.End, machine.State);
    }

    [Fact]
    public void PositionIsPublishedBeforeEntryActionsRun()
    {
        S? seen = null;
        StateMachine<S, T, Ctx>? machine = null;
        machine = new StateMachine<S, T, Ctx>(S.Start, new Ctx());
        machine.Configure(S.Start).Permit(T.Go, S.Middle);
        machine.Configure(S.Middle).OnEntry(_ => seen = machine!.State);

        machine.Fire(T.Go);

        Assert.Equal(S.Middle, seen);
    }

    [Fact]
    public void Fire_WithNoCallbacks_DoesNotAllocateBeyondThePositionRecord()
    {
        var machine = new StateMachine<S, T, Ctx>(S.Start, new Ctx());
        machine.Configure(S.Start).Permit(T.Go, S.Middle);
        machine.Configure(S.Middle).Permit(T.Go, S.Start);
        for (var i = 0; i < 1_000; i++)
        {
            machine.Fire(T.Go);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
        {
            machine.Fire(T.Go);
        }

        var perFire = (GC.GetAllocatedBytesForCurrentThread() - before) / 10_000;

        Assert.True(perFire <= 32, $"allocated {perFire} bytes per Fire");
    }

    // ---- contract edges --------------------------------------------------------------------------------------------

    [Fact]
    public void NullArguments_ToTheRemainingEntryPoints_Throw()
    {
        var machine = new StateMachine<string, string, Ctx>("a", new Ctx());

        Assert.Throws<ArgumentNullException>(() => new StateMachine<string, string, Ctx>(null!, new Ctx()));
        Assert.Throws<ArgumentNullException>(() => new StateMachine<string, string>(null!));
        Assert.Throws<ArgumentNullException>(() => machine.Post(null!));
        Assert.Throws<ArgumentNullException>(() => machine.Restore(null!, 0));
    }

    [Fact]
    public void Restore_NegativeSequence_Throws_AndChangesNothing()
    {
        var machine = new StateMachine<S, T, Ctx>(S.Start, new Ctx());

        Assert.Throws<ArgumentOutOfRangeException>(() => machine.Restore(S.End, -1));

        Assert.Equal(S.Start, machine.State);
        Assert.Equal(0, machine.Sequence);
    }

    [Fact]
    public void Start_FromInsideACallback_Throws()
    {
        StateMachine<S, T, Ctx>? machine = null;
        machine = new StateMachine<S, T, Ctx>(S.Start, new Ctx());
        machine.Configure(S.Start).Permit(T.Go, S.Middle);
        machine.Configure(S.Middle).OnEntry(_ => machine!.Start());

        Assert.Throws<InvalidOperationException>(() => machine.Fire(T.Go));
    }

    [Fact]
    public void Start_WhenAnInitialEntryActionThrows_SurfacesIt_AndStillDrainsQueuedTriggers()
    {
        var machine = new StateMachine<S, T, Ctx>(S.Start, new Ctx());
        machine.Configure(S.Start).Permit(T.Go, S.Middle).OnEntry(_ => throw new InvalidOperationException("initial entry boom"));
        machine.Post(T.Go);

        var e = Assert.Throws<InvalidOperationException>(() => machine.Start());

        Assert.Equal("initial entry boom", e.Message);
        Assert.Equal(S.Middle, machine.State);
        Assert.Throws<InvalidOperationException>(() => machine.Start()); // it did start
    }

    [Fact]
    public void StateTransition_ToString_ShowsTheTransition_AndAnEntryFailure()
    {
        var ok = new StateTransition<S, T>(S.Start, S.Middle, T.Go, 3);
        var failed = new StateTransition<S, T>(S.Start, S.Middle, T.Go, 4, new ArgumentException("x"));

        Assert.Equal("Start --Go--> Middle #3", ok.ToString());
        Assert.Equal("Start --Go--> Middle #4 (entry failed: ArgumentException)", failed.ToString());
        Assert.False(ok.HasFailed);
        Assert.True(failed.HasFailed);
    }
}

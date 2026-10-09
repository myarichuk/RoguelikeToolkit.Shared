using System;
using System.Collections.Generic;
using RoguelikeToolkit.StateMachine;
using Xunit;

namespace RoguelikeToolkit.StateMachine.Tests;

public sealed class ContextStateMachineTests
{
    private enum AiState
    {
        Idle,
        Chase,
        Attack,
        Dead,
    }

    private enum AiTrigger
    {
        PlayerSpotted,
        PlayerLost,
        InRange,
        Killed,
    }

    private sealed class EnemyCtx
    {
        public int Hp;
        public bool CanSee;
    }

    private sealed class RecordingSink : IStateTransitionSink<AiState, AiTrigger, EnemyCtx>
    {
        public readonly List<StateTransition<AiState, AiTrigger>> Transitions = new();
        public readonly List<EnemyCtx> Contexts = new();
        public readonly List<int> HpAtCall = new();

        public void OnTransition(StateTransition<AiState, AiTrigger> transition, EnemyCtx context)
        {
            Transitions.Add(transition);
            Contexts.Add(context);
            HpAtCall.Add(context.Hp);
        }
    }

    private static StateMachine<AiState, AiTrigger, EnemyCtx> CreateChaseMachine(
        EnemyCtx ctx,
        IStateTransitionSink<AiState, AiTrigger, EnemyCtx>? sink = null)
    {
        var machine = new StateMachine<AiState, AiTrigger, EnemyCtx>(AiState.Idle, ctx, sink);
        machine.Configure(AiState.Idle)
            .Permit(AiTrigger.PlayerSpotted, AiState.Chase, guard: c => c.CanSee);
        machine.Configure(AiState.Chase)
            .Permit(AiTrigger.InRange, AiState.Attack)
            .Permit(AiTrigger.PlayerLost, AiState.Idle)
            .Ignore(AiTrigger.PlayerSpotted);
        machine.Configure(AiState.Attack)
            .Permit(AiTrigger.Killed, AiState.Dead, guard: c => c.Hp <= 0);
        return machine;
    }

    [Fact]
    public void NewMachine_StartsInInitialState_WithSharedContext()
    {
        var ctx = new EnemyCtx { Hp = 30 };
        var machine = CreateChaseMachine(ctx);

        Assert.Equal(AiState.Idle, machine.State);
        Assert.Same(ctx, machine.Context);
        Assert.Equal(0, machine.Sequence);
    }

    [Fact]
    public void Fire_PermittedTrigger_TransitionsAndRunsExitThenEntry()
    {
        var order = new List<string>();
        var machine = CreateChaseMachine(new EnemyCtx { CanSee = true });
        machine.Configure(AiState.Idle).OnExit(_ => order.Add("exit-idle"));
        machine.Configure(AiState.Chase).OnEntry(_ => order.Add("entry-chase"));

        machine.Fire(AiTrigger.PlayerSpotted);

        Assert.Equal(AiState.Chase, machine.State);
        Assert.Equal(new[] { "exit-idle", "entry-chase" }, order);
    }

    [Fact]
    public void Guard_SeesLiveContext_MutatedAfterConfigure()
    {
        var ctx = new EnemyCtx { CanSee = false };
        var machine = CreateChaseMachine(ctx);

        Assert.False(machine.CanFire(AiTrigger.PlayerSpotted));

        ctx.CanSee = true;

        Assert.True(machine.CanFire(AiTrigger.PlayerSpotted));
        machine.Fire(AiTrigger.PlayerSpotted);
        Assert.Equal(AiState.Chase, machine.State);
    }

    [Fact]
    public void Ignore_Fire_IsNoOp()
    {
        var sink = new RecordingSink();
        var entered = false;
        var machine = CreateChaseMachine(new EnemyCtx { CanSee = true }, sink);
        machine.Configure(AiState.Chase).OnEntry(_ => entered = true);

        machine.Fire(AiTrigger.PlayerSpotted);
        machine.Fire(AiTrigger.PlayerSpotted); // ignored in Chase

        Assert.Equal(AiState.Chase, machine.State);
        Assert.False(machine.CanFire(AiTrigger.PlayerSpotted));
        Assert.Single(sink.Transitions);
        Assert.True(entered);
    }

    [Fact]
    public void Sink_ReceivesFromToTriggerSequence_AndSameContext()
    {
        var ctx = new EnemyCtx { CanSee = true, Hp = 10 };
        var sink = new RecordingSink();
        var machine = CreateChaseMachine(ctx, sink);

        machine.Fire(AiTrigger.PlayerSpotted);
        machine.Fire(AiTrigger.InRange);

        Assert.Equal(2, machine.Sequence);
        Assert.Equal(new StateTransition<AiState, AiTrigger>(AiState.Idle, AiState.Chase, AiTrigger.PlayerSpotted, 1), sink.Transitions[0]);
        Assert.Equal(new StateTransition<AiState, AiTrigger>(AiState.Chase, AiState.Attack, AiTrigger.InRange, 2), sink.Transitions[1]);
        Assert.Same(ctx, sink.Contexts[0]);
        Assert.Same(ctx, sink.Contexts[1]);
    }

    [Fact]
    public void EntryAction_MutatingContext_IsVisibleToSink()
    {
        var ctx = new EnemyCtx { CanSee = true, Hp = 10 };
        var sink = new RecordingSink();
        var machine = CreateChaseMachine(ctx, sink);
        machine.Configure(AiState.Chase).OnEntry(c => c.Hp = 99);

        machine.Fire(AiTrigger.PlayerSpotted);

        Assert.Single(sink.Transitions);
        Assert.Equal(99, sink.HpAtCall[0]);
    }

    [Fact]
    public void MultipleEntryActions_RunInRegistrationOrder()
    {
        var order = new List<int>();
        var machine = CreateChaseMachine(new EnemyCtx { CanSee = true });
        machine.Configure(AiState.Chase)
            .OnEntry(_ => order.Add(1))
            .OnEntry(_ => order.Add(2));

        machine.Fire(AiTrigger.PlayerSpotted);

        Assert.Equal(new[] { 1, 2 }, order);
    }

    [Fact]
    public void Permit_WithoutGuard_AllowsUnconditionally()
    {
        var machine = CreateChaseMachine(new EnemyCtx { CanSee = true });

        machine.Fire(AiTrigger.PlayerSpotted);

        Assert.True(machine.CanFire(AiTrigger.InRange));
    }

    [Fact]
    public void Permit_Twice_ReplacesDestination()
    {
        var machine = CreateChaseMachine(new EnemyCtx { CanSee = true });
        machine.Configure(AiState.Idle).Permit(AiTrigger.PlayerSpotted, AiState.Attack);

        machine.Fire(AiTrigger.PlayerSpotted);

        Assert.Equal(AiState.Attack, machine.State);
    }

    [Fact]
    public void Ctor_NullContext_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new StateMachine<AiState, AiTrigger, EnemyCtx>(AiState.Idle, null!));
    }

    [Fact]
    public void Fire_UnconfiguredTrigger_Throws_AndLeavesStateAndSinkUntouched()
    {
        var sink = new RecordingSink();
        var machine = CreateChaseMachine(new EnemyCtx(), sink);

        var ex = Assert.Throws<InvalidOperationException>(() => machine.Fire(AiTrigger.PlayerLost));
        Assert.Contains("Cannot fire", ex.Message);

        Assert.Equal(AiState.Idle, machine.State);
        Assert.Equal(0, machine.Sequence);
        Assert.Empty(sink.Transitions);
    }

    [Fact]
    public void Guard_False_BlocksFire_WithDedicatedMessage()
    {
        var sink = new RecordingSink();
        var ranExit = false;
        var ranEntry = false;
        var machine = CreateChaseMachine(new EnemyCtx { CanSee = false }, sink);
        machine.Configure(AiState.Idle).OnExit(_ => ranExit = true);
        machine.Configure(AiState.Chase).OnEntry(_ => ranEntry = true);

        Assert.False(machine.CanFire(AiTrigger.PlayerSpotted));
        var ex = Assert.Throws<InvalidOperationException>(() => machine.Fire(AiTrigger.PlayerSpotted));

        Assert.Contains("Guard blocked", ex.Message);
        Assert.Equal(AiState.Idle, machine.State);
        Assert.Equal(0, machine.Sequence);
        Assert.False(ranExit);
        Assert.False(ranEntry);
        Assert.Empty(sink.Transitions);
    }

    [Fact]
    public void Guard_Throwing_Propagates_WithoutStateChange()
    {
        var machine = new StateMachine<AiState, AiTrigger, EnemyCtx>(AiState.Idle, new EnemyCtx());
        var boom = new InvalidOperationException("guard boom");
        machine.Configure(AiState.Idle)
            .Permit(AiTrigger.PlayerSpotted, AiState.Chase, guard: _ => throw boom);

        Assert.Same(boom, Assert.Throws<InvalidOperationException>(() => machine.CanFire(AiTrigger.PlayerSpotted)));
        Assert.Same(boom, Assert.Throws<InvalidOperationException>(() => machine.Fire(AiTrigger.PlayerSpotted)));
        Assert.Equal(AiState.Idle, machine.State);
        Assert.Equal(0, machine.Sequence);
    }

    [Fact]
    public void NullArguments_ThrowArgumentNullException()
    {
        var machine = new StateMachine<string, string, EnemyCtx>("a", new EnemyCtx());
        machine.Configure("a").Permit("go", "b");

        Assert.Throws<ArgumentNullException>(() => machine.Configure(null!));
        Assert.Throws<ArgumentNullException>(() => machine.Configure("a").Permit(null!, "b"));
        Assert.Throws<ArgumentNullException>(() => machine.Configure("a").Permit("go", null!));
        Assert.Throws<ArgumentNullException>(() => machine.Configure("a").Ignore(null!));
        Assert.Throws<ArgumentNullException>(() => machine.Configure("a").OnEntry(null!));
        Assert.Throws<ArgumentNullException>(() => machine.Configure("a").OnExit(null!));
        Assert.Throws<ArgumentNullException>(() => machine.CanFire(null!));
        Assert.Throws<ArgumentNullException>(() => machine.Fire(null!));
    }

    [Fact]
    public void Permit_AfterIgnore_Throws()
    {
        var machine = CreateChaseMachine(new EnemyCtx());

        Assert.Throws<InvalidOperationException>(() =>
            machine.Configure(AiState.Chase).Permit(AiTrigger.PlayerSpotted, AiState.Dead));
    }

    [Fact]
    public void Ignore_AfterPermit_Throws()
    {
        var machine = CreateChaseMachine(new EnemyCtx());

        Assert.Throws<InvalidOperationException>(() =>
            machine.Configure(AiState.Idle).Ignore(AiTrigger.PlayerSpotted));
    }

    [Fact]
    public void Exit_Throwing_AbortsTransition_AndMachineStaysUsable()
    {
        var sink = new RecordingSink();
        var failExit = true;
        var entered = false;
        var machine = CreateChaseMachine(new EnemyCtx { CanSee = true }, sink);
        machine.Configure(AiState.Idle).OnExit(_ =>
        {
            if (failExit)
            {
                throw new InvalidOperationException("exit boom");
            }
        });
        machine.Configure(AiState.Chase).OnEntry(_ => entered = true);

        Assert.Throws<InvalidOperationException>(() => machine.Fire(AiTrigger.PlayerSpotted));
        Assert.Equal(AiState.Idle, machine.State);
        Assert.Equal(0, machine.Sequence);
        Assert.False(entered);
        Assert.Empty(sink.Transitions);

        failExit = false;
        machine.Fire(AiTrigger.PlayerSpotted);
        Assert.Equal(AiState.Chase, machine.State);
        Assert.Equal(1, machine.Sequence);
    }

    [Fact]
    public void Entry_Throwing_CommitsState_ButSkipsSink()
    {
        var sink = new RecordingSink();
        var machine = CreateChaseMachine(new EnemyCtx { CanSee = true }, sink);
        machine.Configure(AiState.Chase).OnEntry(_ => throw new InvalidOperationException("entry boom"));

        Assert.Throws<InvalidOperationException>(() => machine.Fire(AiTrigger.PlayerSpotted));

        Assert.Equal(AiState.Chase, machine.State);
        Assert.Equal(0, machine.Sequence);
        Assert.Empty(sink.Transitions);

        // Machine stays usable for onward transitions.
        machine.Fire(AiTrigger.InRange);
        Assert.Equal(AiState.Attack, machine.State);
        Assert.Equal(1, machine.Sequence);
    }

    [Fact]
    public void Sink_Throwing_KeepsCommittedStateAndSequence()
    {
        var ctx = new EnemyCtx { CanSee = true };
        var machine = CreateChaseMachine(ctx, new ThrowingSink());
        machine.Configure(AiState.Chase).Permit(AiTrigger.PlayerLost, AiState.Idle);

        Assert.Throws<InvalidOperationException>(() => machine.Fire(AiTrigger.PlayerSpotted));
        Assert.Equal(AiState.Chase, machine.State);
        Assert.Equal(1, machine.Sequence);
    }

    [Fact]
    public void ReentrantFire_FromEntry_Throws()
    {
        StateMachine<AiState, AiTrigger, EnemyCtx>? machine = null;
        var sink = new RecordingSink();
        machine = new StateMachine<AiState, AiTrigger, EnemyCtx>(AiState.Idle, new EnemyCtx { CanSee = true }, sink);
        machine.Configure(AiState.Idle).Permit(AiTrigger.PlayerSpotted, AiState.Chase);
        machine.Configure(AiState.Chase).OnEntry(_ => machine!.Fire(AiTrigger.PlayerLost));

        Assert.Throws<InvalidOperationException>(() => machine.Fire(AiTrigger.PlayerSpotted));
        Assert.Equal(AiState.Chase, machine.State);
        Assert.Empty(sink.Transitions);
    }

    [Fact]
    public void ReentrantFire_FromExit_Throws_AndStateStaysPut()
    {
        StateMachine<AiState, AiTrigger, EnemyCtx>? machine = null;
        var sink = new RecordingSink();
        machine = new StateMachine<AiState, AiTrigger, EnemyCtx>(AiState.Idle, new EnemyCtx { CanSee = true }, sink);
        machine.Configure(AiState.Idle).Permit(AiTrigger.PlayerSpotted, AiState.Chase);
        machine.Configure(AiState.Idle).OnExit(_ => machine!.Fire(AiTrigger.PlayerSpotted));

        Assert.Throws<InvalidOperationException>(() => machine.Fire(AiTrigger.PlayerSpotted));
        Assert.Equal(AiState.Idle, machine.State);
        Assert.Equal(0, machine.Sequence);
        Assert.Empty(sink.Transitions);
    }

    [Fact]
    public void ReentrantFire_FromSink_Throws()
    {
        StateMachine<AiState, AiTrigger, EnemyCtx>? machine = null;
        machine = new StateMachine<AiState, AiTrigger, EnemyCtx>(
            AiState.Idle, new EnemyCtx { CanSee = true }, new ReentrantSink(() => machine!.Fire(AiTrigger.PlayerLost)));
        machine.Configure(AiState.Idle).Permit(AiTrigger.PlayerSpotted, AiState.Chase);
        machine.Configure(AiState.Chase).Permit(AiTrigger.PlayerLost, AiState.Idle);

        Assert.Throws<InvalidOperationException>(() => machine.Fire(AiTrigger.PlayerSpotted));
        Assert.Equal(AiState.Chase, machine.State);
        Assert.Equal(1, machine.Sequence);
    }

    [Fact]
    public void EmptySink_Instance_IsSingleton_AndNoOp()
    {
        Assert.Same(
            EmptyTransitionSink<AiState, AiTrigger, EnemyCtx>.Instance,
            EmptyTransitionSink<AiState, AiTrigger, EnemyCtx>.Instance);

        var transition = new StateTransition<AiState, AiTrigger>(AiState.Idle, AiState.Chase, AiTrigger.PlayerSpotted, 1);
        var exception = Record.Exception(() =>
            EmptyTransitionSink<AiState, AiTrigger, EnemyCtx>.Instance.OnTransition(transition, new EnemyCtx()));
        Assert.Null(exception);
    }

    [Fact]
    public void Machine_WithoutSink_FiresNormally_UsingEmptyDefault()
    {
        var machine = CreateChaseMachine(new EnemyCtx { CanSee = true });

        machine.Fire(AiTrigger.PlayerSpotted);

        Assert.Equal(AiState.Chase, machine.State);
        Assert.Equal(1, machine.Sequence);
    }

    [Fact]
    public void Machine_WithExplicitEmptySink_FiresNormally()
    {
        var machine = CreateChaseMachine(
            new EnemyCtx { CanSee = true },
            EmptyTransitionSink<AiState, AiTrigger, EnemyCtx>.Instance);

        machine.Fire(AiTrigger.PlayerSpotted);

        Assert.Equal(AiState.Chase, machine.State);
        Assert.Equal(1, machine.Sequence);
    }

    private sealed class ThrowingSink : IStateTransitionSink<AiState, AiTrigger, EnemyCtx>
    {
        public void OnTransition(StateTransition<AiState, AiTrigger> transition, EnemyCtx context)
        {
            throw new InvalidOperationException("sink boom");
        }
    }

    private sealed class ReentrantSink : IStateTransitionSink<AiState, AiTrigger, EnemyCtx>
    {
        private readonly Action _fire;

        public ReentrantSink(Action fire)
        {
            _fire = fire;
        }

        public void OnTransition(StateTransition<AiState, AiTrigger> transition, EnemyCtx context)
        {
            _fire();
        }
    }
}

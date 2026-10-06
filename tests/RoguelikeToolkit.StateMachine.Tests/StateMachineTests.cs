using System;
using RoguelikeToolkit.StateMachine;
using Xunit;

namespace RoguelikeToolkit.StateMachine.Tests;

public sealed class StateMachineTests
{
    private enum State
    {
        Idle,
        Running,
        Done,
    }

    private enum Trigger
    {
        Start,
        Finish,
    }

    private static StateMachine<State, Trigger> CreateMachine() =>
        new StateMachine<State, Trigger>(State.Idle)
            .Configure(State.Idle, Trigger.Start, State.Running)
            .Configure(State.Running, Trigger.Finish, State.Done);

    [Fact]
    public void NewMachine_StartsInInitialState()
    {
        var machine = CreateMachine();

        Assert.Equal(State.Idle, machine.State);
    }

    [Fact]
    public void Fire_ConfiguredTrigger_TransitionsToDestination()
    {
        var machine = CreateMachine();

        machine.Fire(Trigger.Start);

        Assert.Equal(State.Running, machine.State);
    }

    [Fact]
    public void CanFire_ReturnsFalse_ForUnconfiguredTrigger()
    {
        var machine = CreateMachine();

        Assert.False(machine.CanFire(Trigger.Finish));
        Assert.True(machine.CanFire(Trigger.Start));
    }

    [Fact]
    public void Fire_UnconfiguredTrigger_ThrowsInvalidOperationException()
    {
        var machine = CreateMachine();

        Assert.Throws<InvalidOperationException>(() => machine.Fire(Trigger.Finish));
    }
}

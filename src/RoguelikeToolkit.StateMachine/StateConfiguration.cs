using System;

namespace RoguelikeToolkit.StateMachine
{
    /// <summary>Fluent configuration for a single state. Obtained via <see cref="StateMachine{TState, TTrigger, TContext}.Configure"/>.</summary>
    /// <typeparam name="TState">The state type.</typeparam>
    /// <typeparam name="TTrigger">The trigger type.</typeparam>
    /// <typeparam name="TContext">The shared mutable context type.</typeparam>
    public sealed class StateConfiguration<TState, TTrigger, TContext>
        where TState : notnull
        where TTrigger : notnull
        where TContext : class
    {
        private readonly StateMachine<TState, TTrigger, TContext> _machine;
        private readonly TState _state;

        internal StateConfiguration(StateMachine<TState, TTrigger, TContext> machine, TState state)
        {
            _machine = machine;
            _state = state;
        }

        /// <summary>Permits <paramref name="trigger"/> to move the machine from this state to <paramref name="destination"/>.</summary>
        /// <param name="trigger">The trigger.</param>
        /// <param name="destination">The destination state.</param>
        /// <param name="guard">Optional guard evaluated against the shared context. Null means unconditionally permitted.</param>
        /// <returns>This configuration, for chaining.</returns>
        public StateConfiguration<TState, TTrigger, TContext> Permit(TTrigger trigger, TState destination, Func<TContext, bool>? guard = null)
        {
            _machine.Permit(_state, trigger, destination, guard);
            return this;
        }

        /// <summary>Marks <paramref name="trigger"/> as explicitly ignored in this state. Firing it is a no-op.</summary>
        /// <param name="trigger">The trigger to ignore.</param>
        /// <returns>This configuration, for chaining.</returns>
        public StateConfiguration<TState, TTrigger, TContext> Ignore(TTrigger trigger)
        {
            _machine.Ignore(_state, trigger);
            return this;
        }

        /// <summary>Adds an action invoked with the shared context after entering this state.</summary>
        /// <param name="action">The entry action.</param>
        /// <returns>This configuration, for chaining.</returns>
        public StateConfiguration<TState, TTrigger, TContext> OnEntry(Action<TContext> action)
        {
            _machine.AddEntry(_state, action);
            return this;
        }

        /// <summary>Adds an action invoked with the shared context before leaving this state.</summary>
        /// <param name="action">The exit action.</param>
        /// <returns>This configuration, for chaining.</returns>
        public StateConfiguration<TState, TTrigger, TContext> OnExit(Action<TContext> action)
        {
            _machine.AddExit(_state, action);
            return this;
        }
    }
}

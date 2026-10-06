using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.StateMachine
{
    /// <summary>Minimal finite state machine with explicitly configured transitions.</summary>
    /// <typeparam name="TState">The state type.</typeparam>
    /// <typeparam name="TTrigger">The trigger type.</typeparam>
    public sealed class StateMachine<TState, TTrigger>
        where TState : notnull
        where TTrigger : notnull
    {
        private readonly Dictionary<(TState State, TTrigger Trigger), TState> _transitions =
            new Dictionary<(TState State, TTrigger Trigger), TState>();

        /// <summary>Creates a state machine in the given initial state.</summary>
        /// <param name="initialState">The initial state.</param>
        public StateMachine(TState initialState)
        {
            State = initialState;
        }

        /// <summary>Gets the current state.</summary>
        public TState State { get; private set; }

        /// <summary>Permits <paramref name="trigger"/> to move the machine from <paramref name="state"/> to <paramref name="destination"/>.</summary>
        /// <param name="state">The source state.</param>
        /// <param name="trigger">The trigger.</param>
        /// <param name="destination">The destination state.</param>
        /// <returns>This state machine, for chaining.</returns>
        public StateMachine<TState, TTrigger> Configure(TState state, TTrigger trigger, TState destination)
        {
            _transitions[(state, trigger)] = destination;
            return this;
        }

        /// <summary>Determines whether <paramref name="trigger"/> can fire from the current state.</summary>
        /// <param name="trigger">The trigger.</param>
        /// <returns><c>true</c> if the trigger is permitted from the current state.</returns>
        public bool CanFire(TTrigger trigger) => _transitions.ContainsKey((State, trigger));

        /// <summary>Fires <paramref name="trigger"/>, moving the machine to the configured destination state.</summary>
        /// <param name="trigger">The trigger.</param>
        /// <exception cref="InvalidOperationException">The trigger is not permitted from the current state.</exception>
        public void Fire(TTrigger trigger)
        {
            if (!_transitions.TryGetValue((State, trigger), out var destination))
            {
                throw new InvalidOperationException($"Cannot fire trigger '{trigger}' from state '{State}'.");
            }

            State = destination;
        }
    }
}

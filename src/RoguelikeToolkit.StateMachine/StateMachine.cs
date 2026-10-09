using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.StateMachine
{
    /// <summary>Finite state machine with shared mutable context, guards, entry/exit actions, and a transition sink.</summary>
    /// <typeparam name="TState">The state type.</typeparam>
    /// <typeparam name="TTrigger">The trigger type.</typeparam>
    /// <typeparam name="TContext">The shared mutable context type. One instance is shared by all states.</typeparam>
    public sealed class StateMachine<TState, TTrigger, TContext>
        where TState : notnull
        where TTrigger : notnull
        where TContext : class
    {
        private sealed class Transition
        {
            public Transition(TState destination, Func<TContext, bool>? guard)
            {
                Destination = destination;
                Guard = guard;
            }

            public TState Destination { get; set; }

            public Func<TContext, bool>? Guard { get; set; }
        }

        private readonly object _gate = new object();
        private readonly Dictionary<(TState State, TTrigger Trigger), Transition> _transitions =
            new Dictionary<(TState State, TTrigger Trigger), Transition>();
        private readonly HashSet<(TState State, TTrigger Trigger)> _ignored =
            new HashSet<(TState State, TTrigger Trigger)>();
        private readonly Dictionary<TState, List<Action<TContext>>> _entries =
            new Dictionary<TState, List<Action<TContext>>>();
        private readonly Dictionary<TState, List<Action<TContext>>> _exits =
            new Dictionary<TState, List<Action<TContext>>>();
        private readonly IStateTransitionSink<TState, TTrigger, TContext> _sink;
        private bool _isFiring;
        private long _sequence;

        /// <summary>Creates a state machine in the given initial state with the given shared context.</summary>
        /// <param name="initialState">The initial state.</param>
        /// <param name="context">The shared mutable context passed to every guard and action.</param>
        /// <param name="sink">Optional sink invoked once per committed transition.</param>
        public StateMachine(TState initialState, TContext context, IStateTransitionSink<TState, TTrigger, TContext>? sink = null)
        {
            if (initialState is null)
            {
                throw new ArgumentNullException(nameof(initialState));
            }

            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            State = initialState;
            Context = context;
            _sink = sink ?? EmptyTransitionSink<TState, TTrigger, TContext>.Instance;
        }

        /// <summary>Gets the current state.</summary>
        public TState State { get; private set; }

        /// <summary>Gets the shared mutable context.</summary>
        public TContext Context { get; }

        /// <summary>Gets the number of committed transitions.</summary>
        public long Sequence
        {
            get
            {
                lock (_gate)
                {
                    return _sequence;
                }
            }
        }

        /// <summary>Begins fluent configuration for <paramref name="state"/>.</summary>
        /// <param name="state">The state to configure.</param>
        /// <returns>The state configuration, for chaining.</returns>
        public StateConfiguration<TState, TTrigger, TContext> Configure(TState state)
        {
            if (state is null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            return new StateConfiguration<TState, TTrigger, TContext>(this, state);
        }

        /// <summary>Determines whether <paramref name="trigger"/> can fire from the current state with the current context.</summary>
        /// <param name="trigger">The trigger.</param>
        /// <returns><c>true</c> if the trigger is permitted and its guard (if any) passes.</returns>
        public bool CanFire(TTrigger trigger)
        {
            if (trigger is null)
            {
                throw new ArgumentNullException(nameof(trigger));
            }

            lock (_gate)
            {
                if (_ignored.Contains((State, trigger)))
                {
                    return false;
                }

                if (!_transitions.TryGetValue((State, trigger), out var transition))
                {
                    return false;
                }

                return transition.Guard is null || transition.Guard(Context);
            }
        }

        /// <summary>Fires <paramref name="trigger"/>, running exit actions, committing the state change, running entry actions, then notifying the sink.</summary>
        /// <param name="trigger">The trigger.</param>
        /// <exception cref="InvalidOperationException">The trigger is not permitted, its guard blocks it, or <c>Fire</c> was re-entered.</exception>
        public void Fire(TTrigger trigger)
        {
            if (trigger is null)
            {
                throw new ArgumentNullException(nameof(trigger));
            }

            lock (_gate)
            {
                if (_isFiring)
                {
                    throw new InvalidOperationException("Fire cannot be re-entered from an entry action, exit action, guard, or transition sink.");
                }

                if (_ignored.Contains((State, trigger)))
                {
                    return;
                }

                if (!_transitions.TryGetValue((State, trigger), out var transition))
                {
                    throw new InvalidOperationException($"Cannot fire trigger '{trigger}' from state '{State}'.");
                }

                if (!(transition.Guard is null) && !transition.Guard(Context))
                {
                    throw new InvalidOperationException($"Guard blocked trigger '{trigger}' from state '{State}'.");
                }

                var from = State;
                var destination = transition.Destination;
                var exits = Snapshot(_exits, from);
                var entries = Snapshot(_entries, destination);

                // The whole transition runs under the gate so concurrent Fire calls
                // serialize and recursive Fire calls are rejected via _isFiring.
                // Callbacks run under the lock: keep them short and non-blocking.
                _isFiring = true;
                try
                {
                    for (var i = 0; i < exits.Length; i++)
                    {
                        exits[i](Context);
                    }

                    State = destination;

                    try
                    {
                        for (var i = 0; i < entries.Length; i++)
                        {
                            entries[i](Context);
                        }
                    }
                    catch
                    {
                        // State already reflects the destination, but without a
                        // recorded transition: entry is part of the transition.
                        throw;
                    }

                    _sequence++;
                    var record = new StateTransition<TState, TTrigger>(from, destination, trigger, _sequence);

                    _sink.OnTransition(record, Context);
                }
                finally
                {
                    _isFiring = false;
                }
            }
        }

        internal void Permit(TState state, TTrigger trigger, TState destination, Func<TContext, bool>? guard)
        {
            if (state is null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (trigger is null)
            {
                throw new ArgumentNullException(nameof(trigger));
            }

            if (destination is null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            lock (_gate)
            {
                if (_ignored.Contains((state, trigger)))
                {
                    throw new InvalidOperationException($"Trigger '{trigger}' is already ignored in state '{state}'.");
                }

                _transitions[(state, trigger)] = new Transition(destination, guard);
            }
        }

        internal void Ignore(TState state, TTrigger trigger)
        {
            if (state is null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (trigger is null)
            {
                throw new ArgumentNullException(nameof(trigger));
            }

            lock (_gate)
            {
                if (_transitions.ContainsKey((state, trigger)))
                {
                    throw new InvalidOperationException($"Trigger '{trigger}' is already permitted in state '{state}'.");
                }

                _ignored.Add((state, trigger));
            }
        }

        internal void AddEntry(TState state, Action<TContext> action)
        {
            if (state is null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (action is null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            lock (_gate)
            {
                if (!_entries.TryGetValue(state, out var actions))
                {
                    actions = new List<Action<TContext>>();
                    _entries[state] = actions;
                }

                actions.Add(action);
            }
        }

        internal void AddExit(TState state, Action<TContext> action)
        {
            if (state is null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (action is null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            lock (_gate)
            {
                if (!_exits.TryGetValue(state, out var actions))
                {
                    actions = new List<Action<TContext>>();
                    _exits[state] = actions;
                }

                actions.Add(action);
            }
        }

        private static Action<TContext>[] Snapshot(Dictionary<TState, List<Action<TContext>>> source, TState state)
        {
            if (!source.TryGetValue(state, out var actions) || actions.Count == 0)
            {
                return Array.Empty<Action<TContext>>();
            }

            return actions.ToArray();
        }
    }

    /// <summary>Minimal finite state machine with explicitly configured transitions.</summary>
    /// <typeparam name="TState">The state type.</typeparam>
    /// <typeparam name="TTrigger">The trigger type.</typeparam>
    public sealed class StateMachine<TState, TTrigger>
        where TState : notnull
        where TTrigger : notnull
    {
        private sealed class NoContext
        {
        }

        private readonly StateMachine<TState, TTrigger, NoContext> _inner;

        /// <summary>Creates a state machine in the given initial state.</summary>
        /// <param name="initialState">The initial state.</param>
        public StateMachine(TState initialState)
        {
            if (initialState is null)
            {
                throw new ArgumentNullException(nameof(initialState));
            }

            _inner = new StateMachine<TState, TTrigger, NoContext>(initialState, new NoContext());
        }

        /// <summary>Gets the current state.</summary>
        public TState State => _inner.State;

        /// <summary>Permits <paramref name="trigger"/> to move the machine from <paramref name="state"/> to <paramref name="destination"/>.</summary>
        /// <param name="state">The source state.</param>
        /// <param name="trigger">The trigger.</param>
        /// <param name="destination">The destination state.</param>
        /// <returns>This state machine, for chaining.</returns>
        public StateMachine<TState, TTrigger> Configure(TState state, TTrigger trigger, TState destination)
        {
            _inner.Configure(state).Permit(trigger, destination);
            return this;
        }

        /// <summary>Determines whether <paramref name="trigger"/> can fire from the current state.</summary>
        /// <param name="trigger">The trigger.</param>
        /// <returns><c>true</c> if the trigger is permitted from the current state.</returns>
        public bool CanFire(TTrigger trigger) => _inner.CanFire(trigger);

        /// <summary>Fires <paramref name="trigger"/>, moving the machine to the configured destination state.</summary>
        /// <param name="trigger">The trigger.</param>
        /// <exception cref="InvalidOperationException">The trigger is not permitted from the current state.</exception>
        public void Fire(TTrigger trigger) => _inner.Fire(trigger);
    }
}

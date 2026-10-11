#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace RoguelikeToolkit.StateMachine
{
    /// <summary>
    /// Finite state machine with shared mutable context, ordered guarded branches, entry/exit actions and a
    /// transition sink.
    /// <para>
    /// <b>Threading.</b> Every member is thread-safe. Transitions are serialized by one gate, and guards,
    /// entry/exit actions and the sink run <i>while holding it</i>: other threads' <see cref="Fire"/>,
    /// <see cref="CanFire"/> and configuration calls wait until the transition ends, so callbacks must not wait
    /// on another thread that calls back into the machine. <see cref="State"/>, <see cref="Sequence"/> and
    /// <see cref="Post"/> never block. To change the machine from a worker thread without contending with
    /// the owner (for example a game's main thread), <see cref="Post"/> the trigger and let the owner call
    /// <see cref="Pump"/>; the machine itself knows nothing about any particular thread.
    /// </para>
    /// <para>
    /// <b>Re-entrancy.</b> A trigger fired from inside a callback (an entry action that auto-advances, for
    /// example) is queued and runs right after the current transition, in order, before the outermost
    /// <see cref="Fire"/> returns.
    /// </para>
    /// </summary>
    /// <typeparam name="TState">The state type.</typeparam>
    /// <typeparam name="TTrigger">The trigger type.</typeparam>
    /// <typeparam name="TContext">The shared mutable context type. One instance is shared by all states.</typeparam>
    public sealed class StateMachine<TState, TTrigger, TContext>
        where TState : notnull
        where TTrigger : notnull
        where TContext : class
    {
        private static readonly Action<TContext>[] NoActions = new Action<TContext>[0];

        private sealed class Branch
        {
            public Branch(TState destination, Func<TContext, bool>? guard)
            {
                Destination = destination;
                Guard = guard;
            }

            public TState Destination { get; }

            public Func<TContext, bool>? Guard { get; }
        }

        // Immutable pair, swapped atomically, so State and Sequence can be read without taking the gate.
        private sealed class Position
        {
            public Position(TState state, long sequence)
            {
                State = state;
                Sequence = sequence;
            }

            public TState State { get; }

            public long Sequence { get; }
        }

        private readonly object _gate = new object();
        private readonly Dictionary<(TState State, TTrigger Trigger), List<Branch>> _transitions =
            new Dictionary<(TState State, TTrigger Trigger), List<Branch>>();
        private readonly HashSet<(TState State, TTrigger Trigger)> _ignored =
            new HashSet<(TState State, TTrigger Trigger)>();
        // Copy-on-write arrays: configuring from inside a callback never disturbs an iteration in progress.
        private readonly Dictionary<TState, Action<TContext>[]> _entries = new Dictionary<TState, Action<TContext>[]>();
        private readonly Dictionary<TState, Action<TContext>[]> _exits = new Dictionary<TState, Action<TContext>[]>();
        private readonly ConcurrentQueue<TTrigger> _queue = new ConcurrentQueue<TTrigger>();
        private readonly IStateTransitionSink<TState, TTrigger, TContext> _sink;
        private readonly Action? _onPosted;
        private Position _position;
        private bool _running;
        private bool _started;
        private int _wakePending;

        /// <summary>Creates a state machine in the given initial state with the given shared context.</summary>
        /// <param name="initialState">The initial state. Its entry actions run only if you call <see cref="Start"/>.</param>
        /// <param name="context">The shared mutable context passed to every guard and action.</param>
        /// <param name="sink">Optional sink invoked once per committed transition.</param>
        /// <param name="onPosted">
        /// Optional wake-up invoked, possibly on any thread, when <see cref="Post"/> adds work and none is
        /// already waiting for a <see cref="Pump"/>. It must be cheap and must not call into the machine; typical
        /// use is scheduling <see cref="Pump"/> on the owner thread (for example a main-thread dispatcher).
        /// </param>
        public StateMachine(
            TState initialState,
            TContext context,
            IStateTransitionSink<TState, TTrigger, TContext>? sink = null,
            Action? onPosted = null)
        {
            if (initialState is null)
            {
                throw new ArgumentNullException(nameof(initialState));
            }

            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            _position = new Position(initialState, 0);
            Context = context;
            _sink = sink ?? EmptyTransitionSink<TState, TTrigger, TContext>.Instance;
            _onPosted = onPosted;
        }

        /// <summary>Gets the current state. Lock-free; never blocks on a running transition.</summary>
        public TState State => Volatile.Read(ref _position).State;

        /// <summary>Gets the shared mutable context.</summary>
        public TContext Context { get; }

        /// <summary>Gets the number of committed transitions. Lock-free.</summary>
        public long Sequence => Volatile.Read(ref _position).Sequence;

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

        /// <summary>Determines whether the current state is <paramref name="state"/>.</summary>
        /// <param name="state">The state to compare with.</param>
        /// <returns><c>true</c> if the machine is currently in <paramref name="state"/>.</returns>
        public bool IsIn(TState state) => EqualityComparer<TState>.Default.Equals(State, state);

        /// <summary>Determines whether <paramref name="trigger"/> can fire from the current state with the current context.</summary>
        /// <param name="trigger">The trigger.</param>
        /// <returns><c>true</c> if a branch is permitted and its guard (if any) passes. Ignored triggers return <c>false</c>.</returns>
        public bool CanFire(TTrigger trigger)
        {
            if (trigger is null)
            {
                throw new ArgumentNullException(nameof(trigger));
            }

            lock (_gate)
            {
                var state = State;
                return !_ignored.Contains((state, trigger))
                    && _transitions.TryGetValue((state, trigger), out var branches)
                    && SelectBranch(branches) != null;
            }
        }

        /// <summary>Lists the triggers that can fire from the current state, evaluating guards against the current context.</summary>
        /// <returns>The permitted triggers, in no particular order.</returns>
        public IReadOnlyList<TTrigger> GetPermittedTriggers()
        {
            lock (_gate)
            {
                var state = State;
                var result = new List<TTrigger>();
                foreach (var pair in _transitions)
                {
                    if (EqualityComparer<TState>.Default.Equals(pair.Key.State, state)
                        && !_ignored.Contains(pair.Key)
                        && SelectBranch(pair.Value) != null)
                    {
                        result.Add(pair.Key.Trigger);
                    }
                }

                return result;
            }
        }

        /// <summary>
        /// Runs the entry actions of the current state. A machine starts without having "entered" its initial
        /// state; call this once after configuring it when the initial state has entry actions.
        /// </summary>
        /// <exception cref="InvalidOperationException">Already started, or called from inside a callback.</exception>
        public void Start()
        {
            lock (_gate)
            {
                if (_running)
                {
                    throw new InvalidOperationException("Start cannot be called from an entry action, exit action, guard, or transition sink.");
                }

                if (_started)
                {
                    throw new InvalidOperationException("The machine has already been started.");
                }

                _started = true;
                Run(default!, hasFirst: false, startInitial: true);
            }
        }

        /// <summary>
        /// Fires <paramref name="trigger"/>: exit actions, commit to the destination, entry actions, then the
        /// sink. Called from inside a callback it is queued instead (see the class remarks).
        /// </summary>
        /// <param name="trigger">The trigger.</param>
        /// <exception cref="InvalidOperationException">The trigger is not permitted from the current state, or every branch's guard blocked it.</exception>
        /// <exception cref="AggregateException">More than one transition (the trigger plus queued ones) failed. A single failure is thrown as itself.</exception>
        /// <remarks>
        /// If an exit action or guard throws, nothing is committed and the state is unchanged. Once the
        /// exit actions have completed the transition is committed: the state and <see cref="Sequence"/>
        /// advance and the sink is notified even if an entry action then throws (reported through
        /// <see cref="StateTransition{TState, TTrigger}.Error"/> and rethrown afterwards). Remaining entry
        /// actions of that state are skipped.
        /// </remarks>
        public void Fire(TTrigger trigger)
        {
            if (trigger is null)
            {
                throw new ArgumentNullException(nameof(trigger));
            }

            lock (_gate)
            {
                if (_running)
                {
                    _queue.Enqueue(trigger);
                    return;
                }

                Run(trigger, hasFirst: true, startInitial: false);
            }
        }

        /// <summary>
        /// Queues <paramref name="trigger"/> for the thread that calls <see cref="Pump"/> (or the next
        /// <see cref="Fire"/>). Never blocks and never runs callbacks, so any thread may call it.
        /// Posted triggers run in posting order; a trigger that turns out not to be permitted by then
        /// surfaces as an exception from <see cref="Pump"/>, not from <c>Post</c>.
        /// </summary>
        /// <param name="trigger">The trigger.</param>
        public void Post(TTrigger trigger)
        {
            if (trigger is null)
            {
                throw new ArgumentNullException(nameof(trigger));
            }

            _queue.Enqueue(trigger);
            if (_onPosted != null && Interlocked.Exchange(ref _wakePending, 1) == 0)
            {
                _onPosted();
            }
        }

        /// <summary>
        /// Runs every posted trigger now, on the calling thread. Cheap when nothing is queued. Calling it from
        /// inside a callback does nothing; the transition in progress drains the queue itself.
        /// </summary>
        /// <exception cref="AggregateException">More than one queued transition failed. A single failure is thrown as itself.</exception>
        public void Pump()
        {
            // Reset before draining: a Post racing with the drain either gets drained or wakes us again.
            Interlocked.Exchange(ref _wakePending, 0);
            if (_queue.IsEmpty)
            {
                return;
            }

            lock (_gate)
            {
                if (_running)
                {
                    return;
                }

                Run(default!, hasFirst: false, startInitial: false);
            }
        }

        /// <summary>
        /// Puts the machine into <paramref name="state"/> with the given <paramref name="sequence"/> without running
        /// any action or notifying the sink. Meant for loading a saved game.
        /// </summary>
        /// <param name="state">The state to restore.</param>
        /// <param name="sequence">The saved number of committed transitions.</param>
        /// <exception cref="InvalidOperationException">Called from inside a callback.</exception>
        public void Restore(TState state, long sequence)
        {
            if (state is null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (sequence < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sequence));
            }

            lock (_gate)
            {
                if (_running)
                {
                    throw new InvalidOperationException("Restore cannot be called from an entry action, exit action, guard, or transition sink.");
                }

                Volatile.Write(ref _position, new Position(state, sequence));
            }
        }

        internal void Permit(TState state, TTrigger trigger, TState destination, Func<TContext, bool>? guard)
        {
            // 'state' always comes from a StateConfiguration, which Configure() has already null-checked.
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

                if (!_transitions.TryGetValue((state, trigger), out var branches))
                {
                    branches = new List<Branch>();
                    _transitions[(state, trigger)] = branches;
                }
                else if (branches[branches.Count - 1].Guard is null)
                {
                    throw new InvalidOperationException(
                        $"Trigger '{trigger}' in state '{state}' already has an unguarded branch to '{branches[branches.Count - 1].Destination}'; a branch added after it could never run.");
                }

                branches.Add(new Branch(destination, guard));
            }
        }

        internal void Ignore(TState state, TTrigger trigger)
        {
            // 'state' always comes from a StateConfiguration, which Configure() has already null-checked.
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

        internal void AddEntry(TState state, Action<TContext> action) => Append(_entries, state, action);

        internal void AddExit(TState state, Action<TContext> action) => Append(_exits, state, action);

        private void Append(Dictionary<TState, Action<TContext>[]> target, TState state, Action<TContext> action)
        {
            // 'state' always comes from a StateConfiguration, which Configure() has already null-checked.
            if (action is null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            lock (_gate)
            {
                var current = target.TryGetValue(state, out var existing) ? existing : NoActions;
                var next = new Action<TContext>[current.Length + 1];
                Array.Copy(current, next, current.Length);
                next[current.Length] = action;
                target[state] = next;
            }
        }

        private Branch? SelectBranch(List<Branch> branches)
        {
            for (var i = 0; i < branches.Count; i++)
            {
                var guard = branches[i].Guard;
                if (guard is null || guard(Context))
                {
                    return branches[i];
                }
            }

            return null;
        }

        // Gate held. Executes the first trigger (if any), then drains the queue; every failure is collected so one
        // bad trigger cannot strand the ones behind it, then a single failure is rethrown as itself.
        private void Run(TTrigger first, bool hasFirst, bool startInitial)
        {
            List<Exception>? errors = null;
            _running = true;
            try
            {
                if (startInitial)
                {
                    try
                    {
                        RunActions(Lookup(_entries, State));
                    }
                    catch (Exception ex)
                    {
                        errors = new List<Exception> { ex };
                    }
                }

                var next = first;
                var have = hasFirst;
                while (have || _queue.TryDequeue(out next!))
                {
                    have = false;
                    try
                    {
                        Execute(next);
                    }
                    catch (Exception ex)
                    {
                        (errors ??= new List<Exception>()).Add(ex);
                    }
                }
            }
            finally
            {
                _running = false;
            }

            if (errors is null)
            {
                return;
            }

            if (errors.Count == 1)
            {
                ExceptionDispatchInfo.Capture(errors[0]).Throw();
            }

            throw new AggregateException(errors);
        }

        private void Execute(TTrigger trigger)
        {
            var position = _position;
            var from = position.State;

            if (_ignored.Contains((from, trigger)))
            {
                return;
            }

            if (!_transitions.TryGetValue((from, trigger), out var branches))
            {
                throw new InvalidOperationException($"Cannot fire trigger '{trigger}' from state '{from}'.");
            }

            var branch = SelectBranch(branches);
            if (branch is null)
            {
                throw new InvalidOperationException($"Guard blocked trigger '{trigger}' from state '{from}'.");
            }

            var destination = branch.Destination;

            // A throwing exit action aborts before anything is committed.
            RunActions(Lookup(_exits, from));

            var sequence = position.Sequence + 1;
            Volatile.Write(ref _position, new Position(destination, sequence));

            // From here the transition is committed: entry failures are reported, not undone.
            Exception? entryError = null;
            try
            {
                RunActions(Lookup(_entries, destination));
            }
            catch (Exception ex)
            {
                entryError = ex;
            }

            try
            {
                _sink.OnTransition(new StateTransition<TState, TTrigger>(from, destination, trigger, sequence, entryError), Context);
            }
            catch (Exception sinkError)
            {
                if (entryError is null)
                {
                    throw;
                }

                throw new AggregateException(entryError, sinkError);
            }

            if (entryError != null)
            {
                ExceptionDispatchInfo.Capture(entryError).Throw();
            }
        }

        private void RunActions(Action<TContext>[] actions)
        {
            for (var i = 0; i < actions.Length; i++)
            {
                actions[i](Context);
            }
        }

        private static Action<TContext>[] Lookup(Dictionary<TState, Action<TContext>[]> source, TState state) =>
            source.TryGetValue(state, out var actions) ? actions : NoActions;
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
        /// <exception cref="InvalidOperationException">The pair already has a transition.</exception>
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

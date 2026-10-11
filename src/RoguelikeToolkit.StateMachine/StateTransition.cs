using System;

namespace RoguelikeToolkit.StateMachine
{
    /// <summary>Describes a single committed state transition.</summary>
    /// <typeparam name="TState">The state type.</typeparam>
    /// <typeparam name="TTrigger">The trigger type.</typeparam>
    public readonly struct StateTransition<TState, TTrigger>
        where TState : notnull
        where TTrigger : notnull
    {
        /// <summary>Creates a transition record.</summary>
        /// <param name="from">The state the machine left.</param>
        /// <param name="to">The state the machine entered.</param>
        /// <param name="trigger">The trigger that caused the transition.</param>
        /// <param name="sequence">The 1-based sequence number of the transition.</param>
        /// <param name="error">The exception thrown by an entry action, if one did.</param>
        public StateTransition(TState from, TState to, TTrigger trigger, long sequence, Exception? error = null)
        {
            From = from;
            To = to;
            Trigger = trigger;
            Sequence = sequence;
            Error = error;
        }

        /// <summary>Gets the state the machine left.</summary>
        public TState From { get; }

        /// <summary>Gets the state the machine entered.</summary>
        public TState To { get; }

        /// <summary>Gets the trigger that caused the transition.</summary>
        public TTrigger Trigger { get; }

        /// <summary>Gets the 1-based sequence number of the transition.</summary>
        public long Sequence { get; }

        /// <summary>
        /// Gets the exception thrown by an entry action, or <c>null</c> when entry completed. The machine
        /// is in <see cref="To"/> either way: leaving a state is committed before entering the next one runs.
        /// </summary>
        public Exception? Error { get; }

        /// <summary>Gets a value indicating whether an entry action threw during this transition.</summary>
        public bool HasFailed => Error != null;

        /// <inheritdoc />
        public override string ToString() =>
            $"{From} --{Trigger}--> {To} #{Sequence}" + (Error is null ? string.Empty : $" (entry failed: {Error.GetType().Name})");
    }
}

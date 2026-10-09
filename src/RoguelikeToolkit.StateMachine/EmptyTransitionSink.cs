namespace RoguelikeToolkit.StateMachine
{
    /// <summary>No-op transition sink. Use <see cref="Instance"/> to avoid allocating a sink per machine.</summary>
    /// <typeparam name="TState">The state type.</typeparam>
    /// <typeparam name="TTrigger">The trigger type.</typeparam>
    /// <typeparam name="TContext">The shared mutable context type.</typeparam>
    public sealed class EmptyTransitionSink<TState, TTrigger, TContext> : IStateTransitionSink<TState, TTrigger, TContext>
        where TState : notnull
        where TTrigger : notnull
        where TContext : class
    {
        private EmptyTransitionSink()
        {
        }

        /// <summary>Gets the shared no-op sink instance.</summary>
        public static EmptyTransitionSink<TState, TTrigger, TContext> Instance { get; } =
            new EmptyTransitionSink<TState, TTrigger, TContext>();

        /// <inheritdoc />
        public void OnTransition(StateTransition<TState, TTrigger> transition, TContext context)
        {
        }
    }
}

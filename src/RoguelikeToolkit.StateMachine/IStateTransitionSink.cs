namespace RoguelikeToolkit.StateMachine
{
    /// <summary>Receives exactly one callback per committed transition.</summary>
    /// <typeparam name="TState">The state type.</typeparam>
    /// <typeparam name="TTrigger">The trigger type.</typeparam>
    /// <typeparam name="TContext">The shared mutable context type.</typeparam>
    public interface IStateTransitionSink<TState, TTrigger, TContext>
        where TState : notnull
        where TTrigger : notnull
        where TContext : class
    {
        /// <summary>Invoked once after the machine commits to <paramref name="transition"/>.</summary>
        /// <param name="transition">The committed transition.</param>
        /// <param name="context">The shared context. Snapshot what is needed synchronously; it remains mutable.</param>
        void OnTransition(StateTransition<TState, TTrigger> transition, TContext context);
    }
}

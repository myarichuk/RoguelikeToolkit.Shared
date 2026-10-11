using System;

namespace RoguelikeToolkit.EventAggregator
{
    /// <summary>
    /// Delivers published events to subscribed handlers.
    /// <para>
    /// Dispatch is by <b>exact</b> event type: a handler for <c>Base</c> never receives a <c>Derived</c>,
    /// and a handler for an interface never receives its implementers. <see cref="Publish{TEvent}"/> uses the
    /// <i>static</i> type argument, so <c>Publish((object)evt)</c> reaches only <c>object</c> subscribers;
    /// use <see cref="PublishByRuntimeType"/> when only the runtime type is known.
    /// </para>
    /// </summary>
    public interface IEventAggregator
    {
        /// <summary>Subscribes a handler to events of type <typeparamref name="TEvent"/>.</summary>
        /// <typeparam name="TEvent">The event type to subscribe to.</typeparam>
        /// <param name="handler">The handler invoked for each published event.</param>
        /// <returns>An <see cref="IDisposable"/> that unsubscribes the handler when disposed.</returns>
        IDisposable Subscribe<TEvent>(Action<TEvent> handler);

        /// <summary>Removes a previously subscribed handler.</summary>
        /// <typeparam name="TEvent">The event type.</typeparam>
        /// <param name="handler">The handler to remove.</param>
        /// <returns><c>true</c> if the handler was subscribed; otherwise <c>false</c>.</returns>
        bool Unsubscribe<TEvent>(Action<TEvent> handler);

        /// <summary>Gets the number of handlers subscribed for <typeparamref name="TEvent"/>.</summary>
        /// <typeparam name="TEvent">The event type.</typeparam>
        /// <returns>The subscriber count, or 0 when nothing subscribed yet.</returns>
        int SubscriberCount<TEvent>();

        /// <summary>Publishes an event to all handlers subscribed for <typeparamref name="TEvent"/>.</summary>
        /// <typeparam name="TEvent">The event type (the static type argument, not <c>@event.GetType()</c>).</typeparam>
        /// <param name="event">The event instance.</param>
        /// <remarks>
        /// Delivery works on a snapshot taken when publishing starts: a handler subscribed during the publish
        /// is not called for this event, and a handler unsubscribed during the publish (even by an earlier
        /// handler) <b>still runs</b> for this event. Publishing from inside a handler is allowed and delivers
        /// depth-first.
        /// </remarks>
        /// <exception cref="AggregateException">
        /// One or more handlers threw. Every handler still runs; all failures are
        /// collected in <see cref="AggregateException.InnerExceptions"/> in subscription order.
        /// </exception>
        void Publish<TEvent>(TEvent @event);

        /// <summary>
        /// Publishes an event to the handlers subscribed for <c>@event.GetType()</c>, for callers that only hold
        /// the event as <see cref="object"/>. Subscribers of base types or interfaces are not notified.
        /// Boxes value-type events.
        /// </summary>
        /// <param name="event">The event instance. Must not be <c>null</c>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="event"/> is <c>null</c>.</exception>
        /// <exception cref="AggregateException">One or more handlers threw. See <see cref="Publish{TEvent}"/>.</exception>
        void PublishByRuntimeType(object @event);
    }
}

using System;

namespace RoguelikeToolkit.EventAggregator
{
    /// <summary>Delivers published events to subscribed handlers.</summary>
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

        /// <summary>Publishes an event to all subscribed handlers.</summary>
        /// <typeparam name="TEvent">The event type.</typeparam>
        /// <param name="event">The event instance.</param>
        /// <exception cref="AggregateException">
        /// One or more handlers threw. Every handler still runs; all failures are
        /// collected in <see cref="AggregateException.InnerExceptions"/> in subscription order.
        /// </exception>
        void Publish<TEvent>(TEvent @event);
    }
}

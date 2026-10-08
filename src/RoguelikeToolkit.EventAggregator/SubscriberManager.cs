using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.EventAggregator
{
    /// <summary>
    /// Maps each event type to its <see cref="EventChannel{TEvent}"/> for O(1) lookup,
    /// so publishing an event of type <c>T</c> finds the subscriber list for exactly that type.
    /// </summary>
    public sealed class SubscriberManager
    {
        private readonly object @lock = new object();
        private readonly Dictionary<Type, object> _channels = new Dictionary<Type, object>();

        /// <summary>Gets the channel for <typeparamref name="TEvent"/>, creating it on first use.</summary>
        /// <typeparam name="TEvent">The event type.</typeparam>
        /// <returns>The channel holding the subscriber list for <typeparamref name="TEvent"/>.</returns>
        public EventChannel<TEvent> GetOrCreateChannel<TEvent>()
        {
            lock (@lock)
            {
                if (!_channels.TryGetValue(typeof(TEvent), out var channel))
                {
                    channel = new EventChannel<TEvent>();
                    _channels[typeof(TEvent)] = channel;
                }

                return (EventChannel<TEvent>)channel;
            }
        }

        /// <summary>Gets the channel for <typeparamref name="TEvent"/> when one exists.</summary>
        /// <typeparam name="TEvent">The event type.</typeparam>
        /// <param name="channel">The channel, or <c>null</c> when no handler ever subscribed.</param>
        /// <returns><c>true</c> if a channel exists for <typeparamref name="TEvent"/>; otherwise <c>false</c>.</returns>
        public bool TryGetChannel<TEvent>(out EventChannel<TEvent>? channel)
        {
            lock (@lock)
            {
                if (_channels.TryGetValue(typeof(TEvent), out var existing))
                {
                    channel = (EventChannel<TEvent>)existing;
                    return true;
                }

                channel = null;
                return false;
            }
        }

        /// <summary>Subscribes a handler to events of type <typeparamref name="TEvent"/>.</summary>
        /// <typeparam name="TEvent">The event type to subscribe to.</typeparam>
        /// <param name="handler">The handler invoked for each published event.</param>
        /// <returns>An <see cref="IDisposable"/> that unsubscribes the handler when disposed.</returns>
        public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
        {
            return GetOrCreateChannel<TEvent>().Subscribe(handler);
        }

        /// <summary>Removes a previously subscribed handler.</summary>
        /// <typeparam name="TEvent">The event type.</typeparam>
        /// <param name="handler">The handler to remove.</param>
        /// <returns><c>true</c> if the handler was subscribed; otherwise <c>false</c>.</returns>
        public bool Unsubscribe<TEvent>(Action<TEvent> handler)
        {
            if (handler is null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            lock (@lock)
            {
                if (!_channels.TryGetValue(typeof(TEvent), out var existing))
                {
                    return false;
                }

                return ((EventChannel<TEvent>)existing).Unsubscribe(handler);
            }
        }

        /// <summary>Publishes an event to the subscriber list registered for its type.</summary>
        /// <typeparam name="TEvent">The event type. Only handlers subscribed for exactly this type run.</typeparam>
        /// <param name="event">The event instance.</param>
        /// <exception cref="AggregateException">
        /// One or more handlers threw. Every handler still runs; all failures are
        /// collected in <see cref="AggregateException.InnerExceptions"/> in subscription order.
        /// </exception>
        public void Publish<TEvent>(TEvent @event)
        {
            EventChannel<TEvent>? channel;
            lock (@lock)
            {
                if (!_channels.TryGetValue(typeof(TEvent), out var existing))
                {
                    return;
                }

                channel = (EventChannel<TEvent>)existing;
            }

            channel.Publish(@event);
        }

        /// <summary>Gets the number of handlers subscribed for <typeparamref name="TEvent"/>.</summary>
        /// <typeparam name="TEvent">The event type.</typeparam>
        /// <returns>The subscriber count, or 0 when no channel exists yet.</returns>
        public int SubscriberCount<TEvent>()
        {
            lock (@lock)
            {
                if (!_channels.TryGetValue(typeof(TEvent), out var existing))
                {
                    return 0;
                }

                return ((EventChannel<TEvent>)existing).SubscriberCount;
            }
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace RoguelikeToolkit.EventAggregator
{
    /// <summary>
    /// Maps each event type to its <see cref="EventChannel{TEvent}"/>, so publishing an event of type
    /// <c>T</c> finds the subscriber list for exactly that type. Lock-free on the publish path.
    /// </summary>
    internal sealed class SubscriberManager
    {
        private readonly ConcurrentDictionary<Type, IEventChannel> _channels =
            new ConcurrentDictionary<Type, IEventChannel>();

        /// <summary>Gets the channel for <typeparamref name="TEvent"/>, creating it on first use.</summary>
        public EventChannel<TEvent> GetOrCreateChannel<TEvent>()
        {
            if (_channels.TryGetValue(typeof(TEvent), out var existing))
            {
                return (EventChannel<TEvent>)existing;
            }

            return (EventChannel<TEvent>)_channels.GetOrAdd(typeof(TEvent), static _ => new EventChannel<TEvent>());
        }

        /// <summary>Gets the channel for <typeparamref name="TEvent"/> when one exists.</summary>
        public bool TryGetChannel<TEvent>([NotNullWhen(true)] out EventChannel<TEvent>? channel)
        {
            if (_channels.TryGetValue(typeof(TEvent), out var existing))
            {
                channel = (EventChannel<TEvent>)existing;
                return true;
            }

            channel = null;
            return false;
        }

        /// <summary>Subscribes a handler to events of type <typeparamref name="TEvent"/>.</summary>
        public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
        {
            return GetOrCreateChannel<TEvent>().Subscribe(handler);
        }

        /// <summary>Removes a previously subscribed handler.</summary>
        public bool Unsubscribe<TEvent>(Action<TEvent> handler)
        {
            if (handler is null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            return TryGetChannel<TEvent>(out var channel) && channel.Unsubscribe(handler);
        }

        /// <summary>Publishes an event to the subscriber list registered for <typeparamref name="TEvent"/>.</summary>
        public void Publish<TEvent>(TEvent @event)
        {
            if (TryGetChannel<TEvent>(out var channel))
            {
                channel.Publish(@event);
            }
        }

        /// <summary>Publishes an event to the subscriber list registered for its runtime type.</summary>
        public void PublishByRuntimeType(object @event)
        {
            if (@event is null)
            {
                throw new ArgumentNullException(nameof(@event));
            }

            if (_channels.TryGetValue(@event.GetType(), out var channel))
            {
                channel.PublishBoxed(@event);
            }
        }

        /// <summary>Gets the number of handlers subscribed for <typeparamref name="TEvent"/>.</summary>
        public int SubscriberCount<TEvent>()
        {
            return TryGetChannel<TEvent>(out var channel) ? channel.SubscriberCount : 0;
        }
    }
}

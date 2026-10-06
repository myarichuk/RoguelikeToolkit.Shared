using System;
using System.Collections.Generic;

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

        /// <summary>Publishes an event to all subscribed handlers.</summary>
        /// <typeparam name="TEvent">The event type.</typeparam>
        /// <param name="event">The event instance.</param>
        void Publish<TEvent>(TEvent @event);
    }

    /// <summary>Synchronous, thread-safe, in-process event aggregator.</summary>
    public sealed class EventAggregator : IEventAggregator
    {
        private readonly object _gate = new object();
        private readonly Dictionary<Type, List<Delegate>> _handlers = new Dictionary<Type, List<Delegate>>();

        /// <inheritdoc />
        public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
        {
            if (handler is null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            lock (_gate)
            {
                if (!_handlers.TryGetValue(typeof(TEvent), out var handlers))
                {
                    handlers = new List<Delegate>();
                    _handlers[typeof(TEvent)] = handlers;
                }

                handlers.Add(handler);
            }

            return new Subscription(this, typeof(TEvent), handler);
        }

        /// <inheritdoc />
        public void Publish<TEvent>(TEvent @event)
        {
            List<Delegate> snapshot;
            lock (_gate)
            {
                if (!_handlers.TryGetValue(typeof(TEvent), out var handlers))
                {
                    return;
                }

                snapshot = new List<Delegate>(handlers);
            }

            foreach (var handler in snapshot)
            {
                ((Action<TEvent>)handler)(@event);
            }
        }

        private void Unsubscribe(Type eventType, Delegate handler)
        {
            lock (_gate)
            {
                if (_handlers.TryGetValue(eventType, out var handlers))
                {
                    handlers.Remove(handler);
                }
            }
        }

        private sealed class Subscription : IDisposable
        {
            private readonly EventAggregator _owner;
            private readonly Type _eventType;
            private readonly Delegate _handler;
            private bool _disposed;

            public Subscription(EventAggregator owner, Type eventType, Delegate handler)
            {
                _owner = owner;
                _eventType = eventType;
                _handler = handler;
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    _disposed = true;
                    _owner.Unsubscribe(_eventType, _handler);
                }
            }
        }
    }
}

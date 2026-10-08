using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.EventAggregator
{
    /// <summary>Holds the subscriber list for a single event type.</summary>
    /// <typeparam name="TEvent">The event type delivered through this channel.</typeparam>
    public class EventChannel<TEvent>
    {
        private readonly object @lock = new object();
        private readonly List<Action<TEvent>> _subscribers = new List<Action<TEvent>>();

        /// <summary>Gets the number of subscribed handlers.</summary>
        public int SubscriberCount
        {
            get
            {
                lock (@lock)
                {
                    return _subscribers.Count;
                }
            }
        }

        /// <summary>Subscribes a handler to events delivered through this channel.</summary>
        /// <param name="handler">The handler invoked for each published event.</param>
        /// <returns>An <see cref="IDisposable"/> that unsubscribes the handler when disposed.</returns>
        public IDisposable Subscribe(Action<TEvent> handler)
        {
            if (handler is null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            lock (@lock)
            {
                _subscribers.Add(handler);
            }

            return new Subscription(this, handler);
        }

        /// <summary>Publishes an event to all subscribed handlers.</summary>
        /// <param name="event">The event instance.</param>
        /// <exception cref="AggregateException">
        /// One or more handlers threw. Every handler still runs; all failures are
        /// collected in <see cref="AggregateException.InnerExceptions"/> in subscription order.
        /// </exception>
        public void Publish(TEvent @event)
        {
            List<Action<TEvent>> snapshot;
            lock (@lock)
            {
                if (_subscribers.Count == 0)
                {
                    return;
                }

                snapshot = new List<Action<TEvent>>(_subscribers);
            }

            List<Exception>? failures = null;
            foreach (var handler in snapshot)
            {
                try
                {
                    handler(@event);
                }
                catch (Exception ex)
                {
                    failures ??= new List<Exception>();
                    failures.Add(ex);
                }
            }

            if (failures is not null)
            {
                throw new AggregateException(failures);
            }
        }

        /// <summary>Removes a previously subscribed handler.</summary>
        /// <param name="handler">The handler to remove.</param>
        /// <returns><c>true</c> if the handler was subscribed; otherwise <c>false</c>.</returns>
        public bool Unsubscribe(Action<TEvent> handler)
        {
            if (handler is null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            lock (@lock)
            {
                return _subscribers.Remove(handler);
            }
        }

        private sealed class Subscription : IDisposable
        {
            private readonly EventChannel<TEvent> _owner;
            private readonly Action<TEvent> _handler;
            private bool _disposed;

            public Subscription(EventChannel<TEvent> owner, Action<TEvent> handler)
            {
                _owner = owner;
                _handler = handler;
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    _disposed = true;
                    _owner.Unsubscribe(_handler);
                }
            }
        }
    }
}

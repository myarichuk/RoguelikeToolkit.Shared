#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;

namespace RoguelikeToolkit.EventAggregator
{
    /// <summary>Type-erased view of a channel, so the bus can dispatch by runtime type without reflection.</summary>
    internal interface IEventChannel
    {
        void PublishBoxed(object @event);
    }

    /// <summary>
    /// Holds the subscriber list for a single event type. The list is copy-on-write: subscribe and
    /// unsubscribe swap in a new array under a lock, publish reads the current array lock-free
    /// and allocates nothing.
    /// </summary>
    /// <typeparam name="TEvent">The event type delivered through this channel.</typeparam>
    internal sealed class EventChannel<TEvent> : IEventChannel
    {
        private readonly object _gate = new object();
        private Action<TEvent>[] _handlers = Array.Empty<Action<TEvent>>();

        /// <summary>Gets the number of subscribed handlers.</summary>
        public int SubscriberCount => Volatile.Read(ref _handlers).Length;

        /// <summary>Subscribes a handler to events delivered through this channel.</summary>
        /// <param name="handler">The handler invoked for each published event.</param>
        /// <returns>An <see cref="IDisposable"/> that unsubscribes the handler when disposed.</returns>
        public IDisposable Subscribe(Action<TEvent> handler)
        {
            if (handler is null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            lock (_gate)
            {
                var current = _handlers;
                var next = new Action<TEvent>[current.Length + 1];
                Array.Copy(current, next, current.Length);
                next[current.Length] = handler;
                Volatile.Write(ref _handlers, next);
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
            var snapshot = Volatile.Read(ref _handlers);

            List<Exception>? failures = null;
            for (var i = 0; i < snapshot.Length; i++)
            {
                try
                {
                    snapshot[i](@event);
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

        void IEventChannel.PublishBoxed(object @event)
        {
            Publish((TEvent)@event);
        }

        /// <summary>Removes the first registration of a handler (by delegate equality).</summary>
        /// <param name="handler">The handler to remove.</param>
        /// <returns><c>true</c> if the handler was subscribed; otherwise <c>false</c>.</returns>
        public bool Unsubscribe(Action<TEvent> handler)
        {
            if (handler is null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            lock (_gate)
            {
                var current = _handlers;
                var index = Array.FindIndex(current, h => h.Equals(handler));
                if (index < 0)
                {
                    return false;
                }

                var next = new Action<TEvent>[current.Length - 1];
                Array.Copy(current, 0, next, 0, index);
                Array.Copy(current, index + 1, next, index, current.Length - index - 1);
                Volatile.Write(ref _handlers, next);
                return true;
            }
        }

        private sealed class Subscription : IDisposable
        {
            private readonly EventChannel<TEvent> _owner;
            private readonly Action<TEvent> _handler;
            private int _disposed;

            public Subscription(EventChannel<TEvent> owner, Action<TEvent> handler)
            {
                _owner = owner;
                _handler = handler;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    _owner.Unsubscribe(_handler);
                }
            }
        }
    }
}

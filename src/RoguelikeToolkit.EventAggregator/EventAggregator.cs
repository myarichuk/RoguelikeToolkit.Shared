#nullable enable

using System;

namespace RoguelikeToolkit.EventAggregator
{
    /// <summary>
    /// In-process, synchronous event bus. Thread-safe: any thread may subscribe, unsubscribe and publish.
    /// Handlers run on the publishing thread.
    /// </summary>
    public sealed class EventAggregator : IEventAggregator
    {
        private readonly SubscriberManager _manager = new SubscriberManager();

        /// <inheritdoc />
        public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
        {
            return _manager.Subscribe(handler);
        }

        /// <inheritdoc />
        public void Publish<TEvent>(TEvent @event)
        {
            _manager.Publish(@event);
        }

        /// <inheritdoc />
        public void PublishByRuntimeType(object @event)
        {
            _manager.PublishByRuntimeType(@event);
        }

        /// <inheritdoc />
        public bool Unsubscribe<TEvent>(Action<TEvent> handler)
        {
            return _manager.Unsubscribe(handler);
        }

        /// <inheritdoc />
        public int SubscriberCount<TEvent>()
        {
            return _manager.SubscriberCount<TEvent>();
        }
    }
}

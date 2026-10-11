#nullable enable

using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.Determinism
{
    /// <summary>The state of a <see cref="SequencedQueue{T}"/>, for save files.</summary>
    /// <typeparam name="T">The item type.</typeparam>
    public readonly struct SequencedQueueSnapshot<T>
    {
        /// <summary>Creates a snapshot.</summary>
        /// <param name="nextSequence">The sequence number the next enqueued item will get.</param>
        /// <param name="pending">The items that were enqueued but not yet drained, oldest first.</param>
        public SequencedQueueSnapshot(long nextSequence, IReadOnlyList<T> pending)
        {
            NextSequence = nextSequence;
            Pending = pending;
        }

        /// <summary>Gets the sequence number the next enqueued item will get.</summary>
        public long NextSequence { get; }

        /// <summary>Gets the items that were enqueued but not yet drained, oldest first.</summary>
        public IReadOnlyList<T> Pending { get; }
    }

    /// <summary>
    /// A queue for deterministic, replayable event processing: any thread may <see cref="Enqueue"/>, one thread
    /// <see cref="Drain"/>s. Sequence assignment and enqueue are one atomic step, so queue order always equals
    /// sequence order, which is the recorded arrival order and therefore the replay order.
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    public sealed class SequencedQueue<T>
    {
        private readonly object _gate = new object();
        private readonly Queue<T> _queue = new Queue<T>();
        private long _nextSequence;
        private bool _draining;

        /// <summary>Creates an empty queue.</summary>
        /// <param name="firstSequence">The sequence number of the first item.</param>
        public SequencedQueue(long firstSequence = 1)
        {
            _nextSequence = firstSequence;
        }

        /// <summary>Gets the sequence number the next enqueued item will get.</summary>
        public long NextSequence
        {
            get
            {
                lock (_gate)
                {
                    return _nextSequence;
                }
            }
        }

        /// <summary>Gets the number of items waiting to be drained.</summary>
        public int Count
        {
            get
            {
                lock (_gate)
                {
                    return _queue.Count;
                }
            }
        }

        /// <summary>Gets a value indicating whether a <see cref="Drain"/> is in progress.</summary>
        public bool IsDraining
        {
            get
            {
                lock (_gate)
                {
                    return _draining;
                }
            }
        }

        /// <summary>Thread-safe. Assigns the next sequence number, creates the item with it, and enqueues it, atomically.</summary>
        /// <param name="create">Builds the item from its sequence number. Runs under the queue's lock: keep it trivial and do not call back into the queue.</param>
        /// <returns>The enqueued item.</returns>
        public T Enqueue(Func<long, T> create)
        {
            if (create is null)
            {
                throw new ArgumentNullException(nameof(create));
            }

            lock (_gate)
            {
                var item = create(_nextSequence);
                _nextSequence++;
                _queue.Enqueue(item);
                return item;
            }
        }

        /// <summary>
        /// Single consumer. Dispatches queued items in order until the queue is empty, including items enqueued by
        /// <paramref name="handler"/> itself during the drain. A handler that throws does not stop the drain.
        /// </summary>
        /// <param name="handler">Called for each item on the draining thread.</param>
        /// <param name="maxItems">Runaway guard: the most items one drain may dispatch.</param>
        /// <param name="onError">
        /// Receives (item, exception) when <paramref name="handler"/> throws. When <c>null</c>, failures are collected and
        /// thrown together as an <see cref="AggregateException"/> after the drain finishes.
        /// </param>
        /// <returns>The number of items dispatched.</returns>
        /// <exception cref="InvalidOperationException">
        /// A drain is already in progress (this one is not re-entrant, and there is one consumer), or more than
        /// <paramref name="maxItems"/> items were pending, which usually means a handler that endlessly re-enqueues.
        /// The remaining items stay queued.
        /// </exception>
        /// <exception cref="AggregateException">Handlers threw and no <paramref name="onError"/> was given.</exception>
        public int Drain(Action<T> handler, int maxItems = 10_000, Action<T, Exception>? onError = null)
        {
            if (handler is null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            if (maxItems <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxItems), maxItems, "Must be positive.");
            }

            lock (_gate)
            {
                if (_draining)
                {
                    throw new InvalidOperationException("Drain is not re-entrant and supports a single consumer.");
                }

                _draining = true;
            }

            var dispatched = 0;
            List<Exception>? failures = null;
            Exception? runaway = null;
            try
            {
                while (true)
                {
                    T item;
                    lock (_gate)
                    {
                        if (_queue.Count == 0)
                        {
                            break;
                        }

                        if (dispatched >= maxItems)
                        {
                            runaway = new InvalidOperationException(
                                $"More than {maxItems} items in one drain; does a handler endlessly enqueue? {_queue.Count} item(s) remain queued.");
                            break;
                        }

                        item = _queue.Dequeue();
                    }

                    dispatched++;
                    try
                    {
                        handler(item);
                    }
                    catch (Exception ex)
                    {
                        if (onError != null)
                        {
                            onError(item, ex);
                        }
                        else
                        {
                            failures ??= new List<Exception>();
                            failures.Add(ex);
                        }
                    }
                }
            }
            finally
            {
                lock (_gate)
                {
                    _draining = false;
                }
            }

            if (runaway != null)
            {
                if (failures is null)
                {
                    throw runaway;
                }

                failures.Add(runaway);
            }

            if (failures != null)
            {
                throw new AggregateException("One or more handlers threw during Drain.", failures);
            }

            return dispatched;
        }

        /// <summary>Captures the next sequence number and the undrained items. Not callable during a drain.</summary>
        /// <returns>A snapshot to pass to <see cref="Restore"/>.</returns>
        /// <exception cref="InvalidOperationException">A drain is in progress.</exception>
        public SequencedQueueSnapshot<T> Capture()
        {
            lock (_gate)
            {
                ThrowIfDraining("capture");
                return new SequencedQueueSnapshot<T>(_nextSequence, _queue.ToArray());
            }
        }

        /// <summary>Replaces the queue's contents and sequence counter. Not callable during a drain.</summary>
        /// <param name="snapshot">A snapshot from <see cref="Capture"/>.</param>
        /// <exception cref="InvalidOperationException">A drain is in progress.</exception>
        public void Restore(SequencedQueueSnapshot<T> snapshot)
        {
            lock (_gate)
            {
                ThrowIfDraining("restore");
                _queue.Clear();
                if (snapshot.Pending != null)
                {
                    foreach (var item in snapshot.Pending)
                    {
                        _queue.Enqueue(item);
                    }
                }

                _nextSequence = snapshot.NextSequence;
            }
        }

        private void ThrowIfDraining(string what)
        {
            if (_draining)
            {
                throw new InvalidOperationException($"Cannot {what} during a drain.");
            }
        }
    }
}

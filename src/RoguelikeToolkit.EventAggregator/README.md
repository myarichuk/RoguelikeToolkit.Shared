# RoguelikeToolkit.EventAggregator

Synchronous, in-process publish/subscribe. Thread-safe, zero dependencies, `netstandard2.1` (Unity 2021.2+).

```csharp
using RoguelikeToolkit.EventAggregator;

var bus = new EventAggregator();

using var subscription = bus.Subscribe<DoorOpened>(e => Console.WriteLine($"door {e.Id} opened"));
bus.Publish(new DoorOpened(7));   // handlers run now, on this thread
// subscription.Dispose() unsubscribes
```

> **Naming note.** The class `RoguelikeToolkit.EventAggregator.EventAggregator` shares its name with its namespace. From code
> inside a `RoguelikeToolkit.*` namespace the simple name `EventAggregator` binds to the namespace first (CS0118), so qualify it
> (`new RoguelikeToolkit.EventAggregator.EventAggregator()`) or alias it (`using Bus = RoguelikeToolkit.EventAggregator.EventAggregator;`).
> Code outside `RoguelikeToolkit.*` is unaffected.

## Semantics worth knowing

- **Exact-type dispatch.** A `Base` subscriber never receives a `Derived`. `Publish<T>` uses the static `T`, so
  `Publish((object)evt)` reaches only `object` subscribers; use `PublishByRuntimeType(object)` when you only hold an `object`.
- **Snapshot delivery.** A handler subscribed during a publish misses that event. A handler unsubscribed during a publish
  (even by an earlier handler) still runs for that event.
- **Failures.** Every handler runs; if any threw, `Publish` throws an `AggregateException` afterwards, in subscription order.
- **Re-entrancy.** Publishing from inside a handler is allowed and delivers depth-first.
- **Cost.** Publishing is lock-free and allocates nothing; subscribing and unsubscribing copy the (small) handler array.

Apache-2.0.

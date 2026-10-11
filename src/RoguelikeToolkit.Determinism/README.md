# RoguelikeToolkit.Determinism

Building blocks for simulations you can replay, save and reload and get the same result from. Zero dependencies,
`netstandard2.1` (Unity 2021.2+).

## `DeterministicRandom`

Seeded SplitMix64: the same seed gives the same numbers on every platform, and the output is a **contract** (it only
changes in a major version), so saves and replays survive upgrades.

```csharp
using RoguelikeToolkit.Determinism;

var world = new DeterministicRandom(seed);
int roll = world.NextInt(1, 21);                    // d20, no modulo bias
int pick = world.WeightedIndex(new[] { 5.0, 1.0 }); // one NextDouble, zero weights never chosen
world.Shuffle(deck);

// Independent streams: keyed by the seed, NOT by how far `world` has advanced.
var loot = world.Derive("loot:" + chestId);

// Save / load
ulong saved = world.State;
var reloaded = new DeterministicRandom(seed);       // same seed: Derive() keeps producing the same streams
reloaded.Restore(saved);
```

**Why `Derive` ignores the current state.** If a derived stream depended on the parent's position, then any new
code that draws from the parent would silently change every stream derived after it. Keying off the immutable seed means
adding a consumer never reshuffles anyone else's results. Give each subsystem its own salt.

Not thread-safe (like `System.Random`); `Derive` is. Not cryptographically secure.

## `SequencedQueue<T>`

Any thread enqueues, one thread drains. Sequence assignment and enqueue are a single atomic step, so queue order is
the recorded arrival order, which is also the replay order.

```csharp
var queue = new SequencedQueue<GameEvent>();

// any thread
queue.Enqueue(seq => new GameEvent(seq, "door.opened"));

// the owner thread (game loop)
queue.Drain(e => Dispatch(e), maxItems: 10_000, onError: (e, ex) => Log(e, ex));

// saving
var snapshot = queue.Capture();     // next sequence + undrained items
queue.Restore(snapshot);
```

- Items enqueued by a handler during a drain are processed in the same drain, after the ones already queued.
- A throwing handler does not stop the drain. With `onError` you get each failure; without it, the failures are thrown
  together as an `AggregateException` once the queue is empty.
- `maxItems` guards against handlers that endlessly re-enqueue: exceeding it throws and leaves the rest queued.
- `Drain` is not re-entrant and supports one consumer; `Capture`/`Restore` are refused while draining.

Apache-2.0.

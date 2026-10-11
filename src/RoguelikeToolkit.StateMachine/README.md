# RoguelikeToolkit.StateMachine

Finite state machine with a shared context, ordered guarded branches, entry/exit actions and a transition sink.
Thread-safe, zero dependencies, `netstandard2.1` (Unity 2021.2+).

```csharp
using RoguelikeToolkit.StateMachine;

var ai = new StateMachine<AiState, AiTrigger, Enemy>(AiState.Idle, enemy);

ai.Configure(AiState.Idle)
    .Permit(AiTrigger.PlayerSpotted, AiState.Flee,   e => e.Hp < 10)   // first passing guard wins...
    .Permit(AiTrigger.PlayerSpotted, AiState.Chase);                    // ...unguarded "otherwise" goes last
ai.Configure(AiState.Chase)
    .OnEntry(e => e.PlayAlertSound())
    .OnExit(e => e.StopAlertSound())
    .Permit(AiTrigger.PlayerLost, AiState.Idle)
    .Ignore(AiTrigger.PlayerSpotted);

ai.Start();                       // runs Idle's entry actions (a new machine has not "entered" its initial state)
ai.Fire(AiTrigger.PlayerSpotted);
```

## Transition semantics

1. Pick the first branch whose guard passes (none: `InvalidOperationException`).
2. Run the old state's exit actions. If one throws, **nothing is committed**.
3. Commit: `State` and `Sequence` advance.
4. Run the new state's entry actions. If one throws, the transition **stays committed**; the remaining entry actions are skipped.
5. Notify the sink once, with `StateTransition.Error` set if step 4 failed, then rethrow that error.

A `Fire` from inside a callback (an entry action that auto-advances, say) is queued and runs right after the current
transition. Several failures surface as one `AggregateException`; a single one is thrown as itself.

## Threads

Everything is thread-safe. Transitions are serialized by one gate and callbacks run while holding it, so a callback
must not wait on another thread that fires the same machine. `State`, `Sequence` and `Post` never block.

To drive the machine from worker threads while one thread (say Unity's main thread) owns it:

```csharp
var ai = new StateMachine<AiState, AiTrigger, Enemy>(AiState.Idle, enemy,
    onPosted: () => mainThread.Enqueue(ai.Pump));   // optional wake-up, called on the posting thread

// any thread
ai.Post(AiTrigger.PlayerSpotted);

// owner thread (or from Update): applies everything posted, in order
ai.Pump();
```

The machine knows nothing about Unity or any particular thread; `Pump` runs wherever you call it.

## Saves

`Restore(state, sequence)` puts the machine back without running actions or notifying the sink.
`GetPermittedTriggers()` and `IsIn(state)` are there for UI and debugging.

Apache-2.0.

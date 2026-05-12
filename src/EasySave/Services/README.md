# EasySave services — concurrency primitives map

V3 introduces several concurrency primitives spread across the services
layer. New contributors (or anyone coming back to the repo after a few
months) need to know which primitive to reach for in which situation.
This README is the index — the rationale for each choice lives at the
top of the corresponding file.

## Five primitives, five intents

| Primitive | File | Why this one |
|---|---|---|
| `Channel<T>` | `JsonDailyLogger`, `LogCentralizer/Program.cs`, `EasyLog/HttpLogShipper` | **Producer → single writer pipeline.** Many callers append entries; one background task drains and persists. `SingleReader=true` lets the runtime skip locking on the consumer side, so 1000 entries/s on the producer hot path costs ~µs per Append. Use when the consumer must serialize work and back-pressure is acceptable (logs, audit trails). |
| `SemaphoreSlim` | `BigFileGate`, `ParallelBackupOrchestrator._slots` | **Bounded resource pool.** N tokens, async-aware. Big-file gate uses a single-slot semaphore to serialize transfers above the threshold (CdC "interdiction de transférer en parallèle deux fichiers > n Ko"). Orchestrator uses an N-slot semaphore for `max_parallel_jobs`. Use when the contract is "no more than K concurrent X". |
| `Mutex` (named) | `CryptoSoftAdapter._gate` | **System-wide exclusion across processes.** CdC v3 says CryptoSoft is mono-instance — no two CryptoSoft.exe instances on the same machine. A named `Global\…` mutex is the only primitive that crosses the process boundary. Use ONLY when the constraint must hold across multiple OS processes. Inside one process, prefer `SemaphoreSlim`. |
| `ManualResetEventSlim` | `JobExecutionContext.PauseGate` | **Pause / resume gate, no counting.** Worker threads `Wait(ct)` on the gate at every file boundary; the controller calls `Reset()` to pause, `Set()` to resume. Token-aware `Wait(ct)` lets a Stop unblock the gate the same as a Resume would, without dueling primitives. Use when the contract is binary (paused / running) and you need the consumer to block cheaply. |
| `CancellationTokenSource` | `JobExecutionContext.Cts`, `LogCentralizer DailyFileWriter` | **One-way stop signal.** Idiomatic .NET cooperative cancellation. The orchestrator cancels per-job CTS for Stop; the hosted service propagates `stoppingToken` through every async call. Use whenever you need to surface "give up cleanly" through an async call chain — never reuse a CTS, create new ones. |

## Decision tree

```
Need to coordinate?
├── Across processes on the same machine?            → Mutex (named, Global\)
├── "No more than K concurrent X" ?                  → SemaphoreSlim
├── Producer + single consumer with backlog?         → Channel<T>
├── Pause / resume cooperatively, no counting?       → ManualResetEventSlim
└── Stop / shutdown signal through async chain?      → CancellationTokenSource
```

## Anti-patterns observed historically

- **`lock` for cross-thread state with async callers.** `lock` does not
  cooperate with `await` and will deadlock the moment the body becomes
  asynchronous. V3 removed several `lock` blocks from `JsonDailyLogger`
  in favour of `Channel` for this exact reason.
- **Double-Dispose on `CancellationTokenSource`.** A second `Cancel()`
  or `Dispose()` on a CTS that already shut down throws
  `ObjectDisposedException`. `HttpLogShipper` and `CryptoSoftAdapter`
  both gate Dispose with `Interlocked.CompareExchange` for this reason.
- **`Mutex.WaitOne` from the same thread as `ReleaseMutex`** — fine and
  required for OS-level thread affinity. Calling `ReleaseMutex` from a
  different thread throws `ApplicationException`. The CryptoSoft test
  suite uses a dedicated `Thread` (not `Task.Run`) for the holder
  exactly because of this.

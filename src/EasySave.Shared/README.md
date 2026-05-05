# EasySave.Shared

Wire-format DTOs shared between the EasySave engine (server) and the
EasySave.RemoteConsole (client) in v3. This assembly has **no dependency on
the engine or the UI** — both sides depend on it, but it depends on neither.

## Wire format

The v3 socket protocol is **NDJSON**: one message per line, each line a
single JSON object terminated by `\n`. This lets the receiver use
`StreamReader.ReadLineAsync()` for framing instead of carrying its own
length-prefix or buffering layer, and matches the rest of the codebase's
choice of `System.Text.Json` (no `Newtonsoft.Json`).

## Types

| Direction | Type | Purpose |
|---|---|---|
| client → server | `CommandDto` | Pause / Play / Stop a specific job |
| server → client | `EventDto` | All notifications: job state changes, progress snapshots, log lines, errors |

`JobProgressDto` is the per-job payload nested inside a `JobProgress` event.
`JobStateEnum` mirrors `EasySave.JobState` numerically (so a cast on the
server is loss-free) but with client-friendly labels.

## Adding a new event or command

1. Append a new entry at the **end** of `EventType` / `CommandType` (never
   reorder — the numeric values are part of the wire format).
2. If the new event has its own payload shape, add a nullable field on
   `EventDto` and document which `Type` populates it in the inline comment
   block.

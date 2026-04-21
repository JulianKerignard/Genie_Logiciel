# Architecture

High-level view of EasySave v1.0 for anyone picking up the project.
For individual design decisions, see [`docs/adrs/`](adrs/).

## Solution layout

```
EasySave.sln
├── src/
│   ├── EasySave/      # Console application (entry point, CLI, services, models)
│   └── EasyLog/       # Reusable class library (ships as EasyLog.dll)
└── tests/
    └── EasySave.Tests/
```

Two projects is a hard requirement from the cahier — `EasyLog.dll` must stay
reusable by other ProSoft applications. `EasySave` depends on `EasyLog` via a
`ProjectReference`; the reverse is never true.

## Layered architecture

Three layers inside `EasySave`, with a strict one-way dependency:

```
 CLI ──────────────▶ Services ──────────────▶ Models
 (ConsoleUI,         (BackupManager,           (BackupJob,
  CommandParser,      StateTracker,             StateEntry,
  Program)            JobRepository,            BackupType,
                      AppConfig,                JobState,
                      LanguageService,          LogEntry)
                      Backup strategies)
```

- **Models** know nothing about the rest. They are POCOs with public
  auto-properties, serialized verbatim to JSON.
- **Services** contain all the behaviour: CRUD, persistence, backup
  execution, i18n. They never reference anything from `CLI`.
- **CLI** is the only layer that reads `Console.In` / writes `Console.Out`.
  It orchestrates services based on user input.

### Why this matters for handover

Any new delivery channel (a WPF UI in v2.0, a WebAPI, a background service)
can sit next to `CLI` and consume the same `Services`. No service rewrite
needed.

## Dependency injection

DI is **manual** in `Program.Main` — no container, no framework.

```csharp
AppConfig.Load();

var logger = new JsonDailyLogger(AppConfig.Instance.LogDirectory);
var backupManager = new BackupManager(
    logger,
    new FullBackupStrategy(),
    new DifferentialBackupStrategy(),
    StateTracker.Instance,
    JobRepository.Instance);
var langService = new LanguageService(AppConfig.Instance);
```

Every service with more than one collaborator uses **constructor injection**
(`BackupManager`, `ConsoleUI`). Null guards (`ArgumentNullException.ThrowIfNull`)
are in the constructor. Singletons (`StateTracker`, `JobRepository`) are
fetched via `Instance` and passed in explicitly — they are never located
from inside a service.

## Design patterns

| Pattern | Where | Why |
| --- | --- | --- |
| **Strategy** | `IBackupStrategy`, `FullBackupStrategy`, `DifferentialBackupStrategy` | Isolate the "should this file be copied?" decision so new backup modes (v3.0+) drop in without touching `BackupManager`. See [ADR 0001](adrs/0001-strategy-pattern-for-backup-types.md). |
| **Singleton** | `StateTracker.Instance`, `JobRepository.Instance`, `AppConfig.Instance` | Single source of truth for on-disk state. Thread-safe via `Lazy<T>` + internal lock. |
| **Repository** | `JobRepository` | Hides the `jobs.json` format from callers; returns `IReadOnlyList<BackupJob>`. |
| **POCO** | `BackupJob`, `StateEntry`, `LogEntry`, `BackupType`, `JobState` | Plain models with public auto-properties, serialized verbatim to JSON. |
| **Template-like dispatch** | `BackupManager.RunJob` | Common execution loop (enumerate → filter via strategy → copy → log → update state). Strategy supplies the variation point only. |

## Persistence — atomic writes

Three files persist state on disk:

| File | Written by | Format |
| --- | --- | --- |
| `data/logs/yyyy-MM-dd.json` | `JsonDailyLogger` | JSON array of `LogEntry` |
| `data/state.json` | `StateTracker` | JSON array of `StateEntry` (one per job) |
| `data/jobs.json` | `JobRepository` | JSON array of `BackupJob` |

All three follow the **same atomic-write contract**:

1. Serialize the full payload.
2. Write to a temporary sibling file (`*.tmp` or `*.<guid>.tmp`).
3. `File.Move(tmp, final, overwrite: true)` to swap atomically.
4. On exception, delete the tmp and rethrow — never leave a half-written file.

Corrupted reads are **quarantined**, not dropped. A JSON parse failure moves
the unreadable file to `*.corrupted-<timestamp>-<guid>` and logs to
`Console.Error`, so the user's previous data stays available for recovery.

## Configuration

`AppConfig` is the single source of truth for every path and user-facing
setting. It is populated once at startup from `appsettings.json` (next to
the executable) and exposed via `AppConfig.Instance`.

Defaults are anchored to `AppDomain.CurrentDomain.BaseDirectory` so the
app runs out of the box after `dotnet publish`. `appsettings.json` overrides
let operators relocate logs, state, and job files (UNC paths supported).

Invalid JSON or a transient I/O error on the config file falls back silently
to defaults — the app never crashes because of a config problem.

## Internationalization

`LanguageService` loads `Resources/{en,fr}.json` at startup based on
`AppConfig.Language`. Every user-facing string in `ConsoleUI` goes through
`_lang.T(key)`. Users can hot-switch languages from the menu via
`LanguageService.SetLanguage(...)`, which reloads the resource file.

Service-layer exceptions carry translation **keys** (not prose) so the CLI
can render them in the active language without coupling services to the UI.

## Execution flow — running a job

End-to-end when the user picks `4. Execute backup job(s)` and types `1-3;5`:

1. `ConsoleUI` → `CommandParser.ParseJobSelection("1-3;5")` → `[1,2,3,5]`.
2. For each index, `ConsoleUI` calls `BackupManager.ExecuteJob(name)`.
3. `BackupManager` loads jobs from `JobRepository`, resolves the job, and
   picks a strategy from `job.Type`.
4. `BackupManager.RunJob`:
   - Enumerates the source tree recursively.
   - Filters eligible files via `strategy.ShouldCopy(...)`.
   - Updates `StateTracker` with total counts.
   - For each eligible file: copies, times the copy, writes a `LogEntry`
     to `JsonDailyLogger`, updates `StateTracker`.
   - On `IOException` or `UnauthorizedAccessException`, the copy fails with
     `FileTransferTimeMs = -1` and the loop continues.
5. Final `StateTracker` update sets `JobState.Inactive` and `FilesRemaining = 0`.

## Testing

Integration tests live under `tests/EasySave.Tests/` and run against the real
filesystem (each test uses its own temp directory). Test classes that mutate
singletons (`AppConfig.Instance`, `StateTracker.Instance`, `JobRepository.Instance`)
share an xUnit `[Collection("StateCollection")]` with `DisableParallelization = true`
to avoid races between test classes.

## Reference files for a new contributor

- [`CLAUDE.md`](../CLAUDE.md) — cahier constraints, review focus, what *not*
  to suggest.
- [`docs/COMMIT_CONVENTION.md`](COMMIT_CONVENTION.md) — branch and commit rules.
- [`docs/user-manual.md`](user-manual.md) — end-user guide (1 page).
- [`docs/support-client.md`](support-client.md) — deployment and support.
- [`docs/adrs/`](adrs/) — architecture decision records.
- [`docs/diagrams/`](diagrams/) — UML (use case, class, activity, sequence).
- [`src/EasyLog/README.md`](../src/EasyLog/README.md) — DLL public API and
  versioning policy.
- [`CHANGELOG.md`](../CHANGELOG.md) — release history.

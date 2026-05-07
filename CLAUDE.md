# Project context for automated reviewers

This file is loaded by the Claude Code Action during PR reviews. Keep reviews aligned with the constraints below. Do not suggest improvements that contradict them.

## Project

- **EasySave** — backup tool for the fictional editor **ProSoft**
- School project: CESI PGE A3 FISE INFO, Software Engineering track
- Team of 4 developers, solo owners per zone (see `docs/COMMIT_CONVENTION.md`)
- **v1.0 released** (console, 5 backup jobs max, JSON daily logs)
- **v2.1.0 released** (Avalonia GUI, CryptoSoft encryption, XML log format, scheduling, business-software pause, restore, runtime FR/EN switch, removed 5-job cap)
- **v3.0 in progress** (parallel multi-job runner, remote operator console over TCP, big-file gate, event bus, state repository abstraction)

## Solution layout

```
EasySave.sln
├── src/
│   ├── EasySave/                console executable + backup engine + repositories
│   ├── EasySave.UI/             Avalonia GUI (v2 primary interface)
│   ├── EasySave.Shared/         v3 wire-format DTOs (EventDto / CommandDto / JobProgressDto / EventType / CommandType / JobStateEnum) and IEventBus
│   └── EasyLog/                 reusable class library (DLL) imposed by the cahier
├── EasySave.RemoteConsole/      standalone Avalonia client app for the v3 remote console
└── tests/
    ├── EasySave.Tests/          v1 tests
    └── EasySave.Tests.V2/       v2 + v3 tests
```

`EasyLog.dll` must stay reusable by other ProSoft applications — its v1.0 surface is frozen.

## Hard constraints (still apply on every version)

Do not propose changes that break any of these:

- **C# on .NET 8.0**.
- Two backup types: `Full` and `Differential`. No other.
- Sources and targets support **local disks, external drives, network shares**. Recursive traversal of subdirectories.
- **English only** for code, identifiers, comments, commit messages, PR bodies.
- **EN + FR localization** for end-user messages (`src/EasySave/Resources/*.json`, `src/EasySave.UI/Assets/i18n/*.json`).
- Daily logs written in **real time**, one file per day (`yyyy-MM-dd.json` or `.xml`).
- Log paths must be in **UNC format** (e.g. `\\server\share\file`). Local paths use the `\\?\` extended-length prefix.
- `FileTransferTimeMs < 0` is the **error signal** for a failed file copy.
- Logs directory and state file location must be **configurable** via `appsettings.json`. No hardcoded paths like `C:\temp`.
- Single `state.json` (not one per job).
- `EasyLog.dll` public API is **frozen in v1.0** — additive nullable fields on `LogEntry` are allowed (see `EncryptionTimeMs`, `EventType`); breaking renames or removed members are not.
- Files **under 500 lines**. Refactor if a file grows past that.
- **Zero duplication.** Explicit grading criterion.
- User manual must fit on **one page**.

## Constraints lifted in later versions (do not flag)

- **5 backup jobs max** was lifted in v2.0 — the v3 engine accepts an arbitrary number.
- **Console only / no GUI** was the v1.0 rule — v2 ships an Avalonia GUI.
- **Synchronous-only** was the v1.0 default — async/await is the norm in v2/v3 paths (TCP, Channels, Avalonia bindings).
- **Pre-v3, no networking** — v3 introduces an in-process TCP server (`TcpRemoteConsoleServer`) and a separate Avalonia client app.

## Architecture

- Layered: `CLI / UI -> Services -> Models / Repositories`. Models know nothing, Services ignore CLI/UI.
- Strategy pattern for backup types (`IBackupStrategy`).
- Singleton for `StateTracker` (one `state.json`).
- Repository for `JobRepository`, `SettingsRepository`, `SchedulerService`, `IStateRepository` (v3).
- Constructor-based Dependency Injection, wired manually in `Program.Main` / `App.OnFrameworkInitializationCompleted`.
- `sealed` on concrete classes by default.
- `Nullable` enabled everywhere.

### v3-specific contracts

The wire format between the engine and the remote console **must stay stable** across the v3 cycle. Do not propose:

- Renaming or removing fields on `EventDto`, `CommandDto`, `JobProgressDto`, or the `EventType` / `CommandType` / `JobStateEnum` numeric values. Additive optional fields are OK.
- Replacing the `Channel<T>` pattern in `JsonDailyLogger` / `ChannelEventBus` / `IEventBus` consumers with locks or BlockingCollection — the design is "Append is sync but the writer task batches" by intent (see `src/EasyLog/JsonDailyLogger.cs` for rationale).
- Replacing `IRemoteConsoleServer.BroadcastAsync` with a fire-and-forget mechanism — the bus consumer relies on the awaited completion to keep event ordering observable.
- Changing the v3 protocol from newline-delimited JSON over raw TCP to anything else (gRPC, SignalR, WebSocket) — the transport is intentionally minimal so the standalone Avalonia client can implement it without extra dependencies.

## Conventions

- **Branch naming**: `feat/xxx`, `fix/xxx`, `refactor/xxx`, `docs/xxx`, `test/xxx`, `chore/xxx`, `ci/xxx`, `hotfix/xxx`, `perf/xxx`. Kebab-case. V3 work uses `feat/v3/...` or `feat/v3-...` by convention.
- **Commits**: Conventional Commits (`feat(scope): ...`), imperative, lowercase subject, no trailing period.
- **PR target**: `staging`. Only the release merge goes to `main`.
- **Maintenance branches**: `release/v1.x` (carries v1.0.x and v1.1.0), `release/v2.x` (carries v2.0.x and v2.1.0).
- **Tags**: `v1.0.0`, `v1.0.1`, `v1.1.0`, `v2.0.0`, `v2.1.0`, `v3.0.0` on the appropriate release branch at each livrable. No intermediate alpha/beta tags.
- See `docs/COMMIT_CONVENTION.md` for the full detail.

## Review focus

When reviewing a PR, prioritize:

1. **Real bugs** — null derefs, race conditions, unhandled exceptions, wrong UNC handling, silent data loss, **broken durability contracts on `IDailyLogger.Append`** (must be on disk when Append returns), **v3 concurrency bugs** (TCP server race between BroadcastAsync and HandleClient finally, ParallelBackupOrchestrator interleaving, BigFileGate deadlocks).
2. **Cahier violations** — hardcoded paths, >500 line files, duplicated logic, breaking the `EasyLog` v1.0 contract, breaking the v3 wire format.
3. **Security hygiene** — command injection, path traversal, secrets in commits, opening TCP ports on `IPAddress.Any` when `Loopback` would do.
4. **Test coverage gaps on critical paths** — `JsonDailyLogger` concurrency, `BackupManager` strategy dispatch, `CommandParser` edge cases, `TcpRemoteConsoleServer` brutal-disconnect cleanup, `ChannelEventBus` faulted-handler isolation, `ParallelBackupOrchestrator` cancellation.

## Suggestions to avoid

- **Do not suggest external libraries** for logging, DI, CLI parsing, or JSON (Serilog, NLog, MediatR, CommandLineParser, Newtonsoft.Json, AutoMapper, Polly). We stay on `System.Text.Json` + custom code. `EasyLog` is the logger by contract.
- **Do not suggest redesigning `IDailyLogger`** — its v1.0 surface is frozen. Propose an `IDailyLoggerV2` only if strictly necessary.
- **Do not bikeshed on micro-style** (spaces, ordering, naming micro-variants). `.editorconfig` + `dotnet format` run in CI and own that territory.
- **Do not suggest heavy test frameworks** (FluentAssertions, Moq, AutoFixture). Plain xUnit is enough.
- **Do not suggest `DateTime.UtcNow` for daily log file names.** The daily file uses local time on purpose — it aligns with the operator's business day.
- **Do not suggest** adding AI or author attribution (`Co-Authored-By: Claude`, "Generated with", etc.) to commit messages or code. These are forbidden by team policy.
- **Do not suggest replacing the Channel-based writer** in `JsonDailyLogger` with a global lock — the channel is there *because* the lock collapsed under v3 multi-job load (4 jobs × 1000 entries = 4000 file rewrites under the old design).
- **Do not suggest binding the v3 TCP server to `IPAddress.Any` "for tests"** — tests bind to `IPAddress.Loopback:0` to grab a free ephemeral port; production uses the configured port via `appsettings.json`.

## Language rules

- All code, identifiers, comments, log messages, and PR descriptions in **English**.
- French is allowed in the internal team docs under `docs/` (`COMMIT_CONVENTION.md` is bilingual, that is fine).
- User-facing UI strings live in `Resources/en.json`/`fr.json` (console) and `src/EasySave.UI/Assets/i18n/{en,fr}.json` (Avalonia GUI). The C# / XAML code only references translation keys.

## When in doubt

- If a change seems to break cahier compliance, say so explicitly.
- If a suggestion is stylistic only, mark it as `nit:` so the author can skip it.
- Always end reviews with a final `Verdict: OK` or `Verdict: Changes requested` line.

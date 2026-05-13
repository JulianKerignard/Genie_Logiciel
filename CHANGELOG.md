# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [3.0.0] — 2026-05-13

Major release. EasySave moves from sequential to parallel backups, introduces a
remote operator console over TCP, a Docker-based daily-log centralizer, a global
big-file gate, cross-job priority extensions, CryptoSoft mono-instance
enforcement, and automatic pause / resume on business-software detection.
`EasyLog.dll` reaches v1.2 with optional `MachineName` / `UserName` / `EventType`
fields — additive only; the v1.0 public surface and on-disk JSON / XML shape
stay frozen for existing consumers.

### Added

- **Parallel backup engine** (`IParallelBackupOrchestrator` /
  `ParallelBackupOrchestrator`): jobs selected via Run All execute concurrently
  bounded by `max_parallel_jobs` (default 4). Per-job
  `JobExecutionContext` (CTS + PauseGate) isolates Pause / Stop so one job
  cannot freeze another. The legacy sequential path is gone.
- **Big-file gate** (`IBigFileGate` / `BigFileGate`): files ≥
  `large_file_threshold_kb` (default 4096) traverse a global `SemaphoreSlim`
  N=1 — only one large transfer in flight across every job. Small files keep
  running fully parallel.
- **Priority extensions cross-job gate** (`IPriorityGate` / `PriorityGate`):
  no non-priority file starts on any job while a priority extension is still
  pending on any other job. Extensions configured via the new
  `priority_extensions` setting.
- **Pause / Play / Stop on every job and globally**: per-card buttons in the
  Avalonia GUI and identical commands from the remote console. Pause stops
  at the next file boundary; Stop cancels immediately; Play resumes from the
  saved offset (Full) or re-scans (Differential).
- **Business-software auto-pause/auto-resume** (V3 semantics): when a process
  in `business_software` is detected, every running job pauses at its next
  file boundary; when the process exits, the watcher resumes them
  automatically. Replaces the V2 "refuse to launch" rule. The event is
  consigned in the daily log.
- **CryptoSoft mono-instance**: a named system mutex
  (`Global\ProSoft.CryptoSoft`) guarantees only one CryptoSoft process runs
  at a time on the machine; concurrent encrypt requests serialize transparently.
- **Daily-log centralizer** (`src/LogCentralizer`): ASP.NET Core minimal API
  shipped as a Docker image (`docker-compose.yml`). Receives `LogEntry`
  rows from every workstation and writes a single daily file per host or per
  fleet, demultiplexed by the new `MachineName` / `UserName` fields.
- **`LogMode` routing** (`Local` / `Centralized` / `Both`): `JsonDailyLogger`
  and `XmlDailyLogger` accept an `ILogShipper`; `HttpLogShipper` posts to
  `log_centralized_endpoint` with retry + bounded buffer. Local writes preserved
  in `Both` mode, dropped in `Centralized`.
- **Remote operator console** (`EasySave.RemoteConsole`): standalone Avalonia
  client that connects to `TcpRemoteConsoleServer` over newline-delimited
  JSON / TCP. Live job board (progress, state, current file) and Pause / Play /
  Stop commands. Optional TLS via self-signed certificate + TOFU
  `known_hosts`. Multi-console broadcast (`CommandReceived` event audits
  which console issued which command). Auto-reconnect 1 s → 2 s → 5 s → 10 s.
- **Thread-safe `IStateRepository`** (`StateTracker`): atomic per-job updates
  for the concurrent V3 multi-job runs; `state.json` no longer corrupts under
  parallel writes.
- **`StateEntry.FailedFilesCount`**: surfaces the number of files that failed
  during a job so the GUI / state.json reflect a partial-success outcome
  instead of presenting an all-or-nothing result.
- **`EasyLog` v1.2 fields** (additive, all nullable): `MachineName`,
  `UserName`, and `EventType` (`LogEvent` enum covering V3 events —
  `RemoteConsoleConnected`, `ParallelJobStarted`, `BigFileEnqueued`,
  `JobPaused`, `BusinessSoftwareAutoPaused`, etc.). Omitted from output when
  null so v1 / v2 consumers see the same JSON / XML shape they always have.
- **V3 settings** in `appsettings.json`: `max_parallel_jobs`,
  `large_file_threshold_kb`, `priority_extensions`, `log_mode`,
  `log_centralized_endpoint`, `remote_console_enabled`, `remote_console_port`,
  `remote_console_tls_enabled`.

### Changed

- `JsonDailyLogger` switches to a single-writer **`Channel<T>`** consumer.
  Concurrent `Append` calls from N parallel jobs no longer trigger N file
  rewrites — the writer batches them and writes once per drain (4 jobs ×
  1000 entries = a handful of writes instead of 4000). `Append` stays
  synchronous and durable: it blocks until the entry is on disk.
- `XmlDailyLogger` adopts the same `LogRouter.Normalize` helper as the JSON
  writer for host-field stamping, so both formats produce identical
  `MachineName` / `UserName` semantics.
- `BackupManagerAdapter.PauseJob` and `ResumeJob` route through the new
  `IJobController` contract; `JobsViewModel.PauseJob` calls both the adapter
  and the orchestrator so Run-All-launched jobs honor the manual Pause / Stop
  buttons.
- Sidebar version label promoted from `v2.0` to `v3.0`.
- `CryptoSoft` CLI exits with a documented error code when the mono-instance
  mutex is already held — callers retry transparently within
  `crypto_soft.timeout_ms`.

### Fixed

- **`JsonDailyLogger.ReadExisting`** survives a transient `IOException` on
  the day file (writer-task no longer dies, closes #155).
- **`TcpRemoteConsoleServer`** clean-up race on brutal client disconnect:
  the connection state is published exactly once even when `BroadcastAsync`
  and the client `HandleClient` finally block race.
- **`TcpRemoteConsoleClient.ConnectAsync`** publishes the `Error` state when
  the initial connect throws — the UI no longer shows "connecting…" forever.
- **Read-line guard** in the remote protocol caps inbound payloads at 64 KB
  to prevent OOM on a hostile / malformed client.
- **State cache** (`StateTracker`) clears `_cacheDirty` after a successful
  write and guards the timer callback against re-entrancy on Dispose.
- **LogCentralizer Docker image** runs as a non-root user with correct write
  permissions on the mounted journal volume.
- **HttpLogShipper** order-preserving retry: entries published in order A, B,
  C arrive in order A, B, C even when A's first POST fails and is retried.

### Documentation

- V3 UML diagrams (`docs/diagrams/`): Class · Activity · Deployment ·
  Sequence (Parallel + BigFileGate) · Sequence (Play-Pause-Stop) ·
  Sequence (Remote Console).
- V3 acceptance recettes (`docs/recettes/`): parallelism, pause / resume on
  Run All, priority extensions, business-software auto-pause, CryptoSoft
  mono-instance, remote console, backward compatibility with v1 / v2.
- V3 user manual (one page) generated by `docs/generate-manuel.js` —
  sections updated to cover parallelism, priority files, big-file gate,
  pause / play / stop, business-software auto-pause, CryptoSoft mono-instance,
  log centralization, JSON / XML format switch.
- `docs/v3-log-centralizer-admin.md` — administrator handbook for the Docker
  centralizer (deployment, retention, troubleshooting).
- `docs/v3-remote-console-tls.md` — TLS configuration handbook.
- `src/EasySave/Services/README.md` — taxonomy of the five concurrency
  primitives used in V3 (`Channel<T>`, `SemaphoreSlim`, named `Mutex`,
  `ManualResetEventSlim`, `CancellationTokenSource`) and when each applies.

### Tests

- `ParallelBackupOrchestratorTests`: deterministic concurrency tests on the
  `max_parallel_jobs` cap, Pause / Resume / Stop isolation, and faulted-job
  containment.
- `BigFileGateTests`, `PriorityGateTests`: semaphore-N=1 contract and
  cross-job priority ordering.
- `TcpRemoteConsoleServerTests`: brutal-disconnect cleanup and the
  `BroadcastAsync` / `HandleClient` finally race.
- `HttpLogShipperTests`: retry order, zero-loss, throughput.
- `LogCentralizerE2ETests`: Testcontainers end-to-end round-trip of a
  `LogEntry` from the shipper to the centralized daily file.
- `StateTrackerConcurrencyTests`: N-writer race on `state.json`.
- `ChannelEventBusTests`: faulted-handler isolation between subscribers.

## [2.1.0] — 2026-05-05

Maintenance release on `release/v2.x` rolling up the fixes and small UX
improvements accumulated since `v2.0.0`. Scope is bug-fix-heavy; the runtime
FR/EN re-render and a few new disposable contracts justify a minor bump
rather than a patch. `EasyLog.dll` v1.0 public API stays frozen; `LogEntry`
shape on disk is unchanged.

### Added

- **Runtime FR/EN switch** finishes the v2.0 feature: job-card chips
  (`Idle`/`Running`/`Done`), backup-type label (`Full`/`Differential`), the
  `JobEdit` window title and type ComboBox, and the `About` window title +
  OK button now all flip language without a restart (#108).
- **`error.persistence_unavailable` UI key** (en + fr) — surfaces a localized
  banner when `schedules.json` cannot be read instead of leaving the user
  with the raw key (#115).

### Changed

- `RunProgressViewModel` implements `IDisposable` so a future reset path can
  release its `JobsViewModel` subscription without pinning the object graph
  (#128).
- `BackupManager.ExecuteJob` resume cursor is now a file path
  (`string? resumeAfterPath`) instead of an integer index. Robust to source
  mutations between pause and resume (#126).

### Fixed

- **`XmlDailyLogger.ReadExisting`** narrows its `catch` to `XmlException`,
  so a transient `IOException` (antivirus / OneDrive / file lock) no longer
  quarantines the live daily log and fragments the day (#113, closes #112).
- **`SchedulerService.GetAll`** propagates `IOException` instead of returning
  an empty list — prevents the next `Save` from silently overwriting
  `schedules.json` with `[]`. `ScheduleViewModel` flags persistence failure
  to disable Save and surface a localized error (#115, closes #111).
- **`SchedulerDispatchService.Tick`** consults `BusinessWatcherService.
  IsBusinessSoftwareRunning` and skips the tick when a watched process is
  open. The reactive event-based gate was edge-triggered and missed the case
  where the process was already running at watcher startup (#120,
  closes #116).
- **`AppConfig.Load`** propagates `IOException` instead of falling back to
  hardcoded defaults, removing the same silent-overwrite trap as #69 / #97
  / #112 / #115 from the config layer (#121, closes #118).
- **Edit / Delete buttons on job cards** are now gated by
  `BackupJobVM.IsBusy` (= `IsRunning || IsPaused`) — clicking Delete on a
  running job no longer wipes the live `state.json` entry while the worker
  thread re-creates it as an orphan, restoring the contract that #68 closed
  (#119, closes #117).
- **Pause/resume on a Full backup** survives source mutations: deleting a
  copied file or adding a new one between pause and resume no longer makes
  the index-based cursor silently skip the next file (#126).
- Job-card layout: long source / target paths are clamped with ellipsis so
  the `Delete` button never overlaps the path text (#108).
- `JobEditViewModel` persists changes against the running `BackupManager`
  singleton; the next navigation back to Jobs sees the new entry without a
  restart (#108).

### Documentation

- `README.md` marks `v2.0.0` as the current released version, splits the
  user manual into v1 (console) and v2 (GUI), and exposes
  `cryptosoft-integration.md` and `docs/recettes/` (#114).

### Tests

- `XmlDailyLoggerTests` cover `IOException` propagation and the
  no-quarantine-on-transient-lock contract — Windows-only tests guarded for
  POSIX advisory-locking semantics (#106, #113).
- `SchedulerServiceLockPropagationTests` verify the new `IOException`
  propagation and quarantine-on-`JsonException` paths (#115).
- `BackupManagerPauseResumeTests` add a regression test for the path-based
  resume cursor — proves a deleted source file before resume no longer
  causes a silent skip (#126).
- `BackupManagerPauseTests` cover the pause/resume cancellation flow end to
  end (#107).
- `AppConfigMutationCollection` xUnit collection serializes tests that
  mutate the `AppConfig.Instance` singleton (#115).

## [2.0.0] — 2026-04-29

EasySave v2.0 adds a cross-platform Avalonia GUI, CryptoSoft encryption, XML/JSON log
switching, automatic pause on business software, and a scheduling layer — while keeping
the v1.x console and `EasyLog.dll` contracts fully intact.

### Added

- **Avalonia GUI** (`src/EasySave.UI`): MVVM shell (CommunityToolkit.Mvvm) with sidebar
  navigation, job cards, real-time progress bars, and a settings screen.
- **i18n** (FR/EN): all user-facing strings loaded from `Assets/i18n/{lang}.json`; language
  hot-switchable via the sidebar FR/EN buttons.
- **CryptoSoft integration**: configurable path + per-file timeout; encrypted files are
  stamped with source mtime so differential backups skip them correctly on the next run.
- **`EncryptionTimeMs` in logs**: `LogEntry` carries a nullable `EncryptionTimeMs` field
  (omitted from output when null so v1 consumers are unaffected).
- **XML log format**: `XmlDailyLogger` writes `yyyy-MM-dd.xml` with a `<Logs>/<Entry>`
  structure validated by the project XSD (`docs/schemas/easysave-log.xsd`).
- **Log format switch**: `settings.json` `log_format` field selects JSON or XML at startup;
  configurable from the Settings screen without restarting.
- **Business software auto-pause**: `BusinessSoftwareDetector` polls the OS process list;
  `JobsViewModel` pauses running jobs at the next file boundary when a watched process
  appears and resumes them automatically when it closes.
- **Pause/resume at file boundary**: `BackupManager.ExecuteJob` accepts a
  `CancellationToken` and a `startFromIndex`; paused Full-backup jobs resume from where
  they stopped; Differential jobs re-scan naturally.
- **`StateTracker.Pause/Resume`**: persists `"Paused"` state and `PauseReason` to
  `state.json` so monitoring tools see the correct status without polling.
- **RestoreView + RestoreViewModel**: browse per-job restore points (timestamp, type, size),
  choose an alternative destination, and track restore progress with a `ProgressBar`.
- **ScheduleView + ScheduleViewModel**: per-job enable/disable toggle and interval picker
  (minutes); next-run time computed and displayed; configuration persisted to `schedules.json`.
- **`IRestoreService` / `ISchedulerService`**: public interfaces + concrete file-backed
  implementations registered in the DI container.
- **`XmlFormatter`**: serializes `LogEntry` as an `<Entry>` XML fragment; used by both the
  new `XmlDailyLogger` and the existing EasyLog schema validation helper.

### Changed

- `BackupManager.ExecuteJob` gains optional `startFromIndex` and `CancellationToken`
  parameters (backward-compatible defaults — existing callers are unaffected).
- `BackupManager.RunJob` transitions state to `JobState.Paused` (not `Inactive`) when
  cancelled so `state.json` correctly reflects the job's status until resumed.
- `SettingsViewModel` now loads from `SettingsRepository` on construction and persists
  via `SettingsRepository.Save` — the mock data initialization is removed.
- `App.axaml.cs` selects the logger (`JsonDailyLogger` or `XmlDailyLogger`) based on the
  user-saved `LogFormat` setting at startup.
- `MainWindowViewModel` is extended with `NavigateToRestore` and `NavigateToSchedule`
  commands; the sidebar exposes two new navigation buttons.
- `BackupManagerAdapter.PauseJob` stores the pause reason and writes it to `StateTracker`
  after the cancellation is confirmed; `ResumeJob` reads the saved `FilesRemaining` to
  compute the correct `startFromIndex` for Full-backup jobs.

### Fixed

- Business-software pause no longer waits for the entire job to finish before marking it
  as paused — the job now stops at the next file boundary (no partial writes).
- Resuming a paused Full-backup job no longer re-copies already-transferred files.

[Unreleased]: https://github.com/JulianKerignard/Genie_Logiciel_Groupe4/compare/v3.0.0...HEAD
[3.0.0]: https://github.com/JulianKerignard/Genie_Logiciel_Groupe4/compare/v2.1.0...v3.0.0
[2.1.0]: https://github.com/JulianKerignard/Genie_Logiciel_Groupe4/compare/v2.0.0...v2.1.0
[2.0.0]: https://github.com/JulianKerignard/Genie_Logiciel_Groupe4/compare/v1.0.1...v2.0.0

## [1.0.1] — 2026-04-21

Production hardening and documentation pass. No public API change —
`EasyLog.dll` v1.x contract is preserved; existing `appsettings.json`
overrides keep working unchanged.

### Fixed

- `AppConfig.Load` now also catches `IOException`, so a locked or
  unreadable `appsettings.json` falls back to defaults instead of
  crashing the process at startup.
- `StateTracker` quarantines a corrupted `state.json` as
  `*.corrupted-<ts>-<guid>` and logs to stderr, instead of silently
  wiping every other job's state on the next `Update`. Mirrors the
  `JobRepository` behaviour.

### Changed

- `AppConfig` defaults now resolve to the OS-standard per-user
  application data directory instead of `data/` next to the executable.
  `LogDirectory`, `StateFilePath`, and `JobsFilePath` default under
  `%AppData%\ProSoft\EasySave\` on Windows and
  `~/.config/ProSoft/EasySave/` on Linux/macOS. Avoids UAC issues when
  the app is installed under `C:\Program Files`. All three paths remain
  overridable from `appsettings.json`.
- `FullBackupStrategy` and `DifferentialBackupStrategy` marked `sealed`
  for consistency with every other concrete service class.
- `FileHelpers.QuarantineCorruptedFile` centralises the corrupted-file
  rename + stderr log pattern previously duplicated across
  `JobRepository` and `StateTracker`.
- New `CLI/JobSelectionRunner` centralises the execute-loop previously
  duplicated between `Program.cs` (direct CLI mode) and
  `ConsoleUI.ExecuteJobs` (interactive menu).

### Documentation

- `docs/architecture.md` — high-level overview of the layered design,
  design patterns map, atomic-write contract, execution flow, and MVC
  mapping for the upcoming v2.0 WPF migration.
- `src/EasyLog/README.md` — DLL public API reference, usage examples,
  SemVer policy, and v1.x frozen contract.
- `docs/adrs/0001-strategy-pattern-for-backup-types.md` — first
  Architecture Decision Record.
- `BackupManager` gained full XML documentation on its public surface.

## [1.0.0] — 2026-04-21

First release delivered to ProSoft. Console backup tool with up to 5 jobs,
full and differential strategies, daily JSON logging, and English/French UI.

### Added

- **EasySave console application** with interactive menu and direct CLI mode (`EasySave 1-3;5`).
- **Backup engine** supporting two strategies: `Full` (every file) and `Differential` (size + mtime comparison).
- **Job management**: create, list, remove, execute one or several jobs — limit of 5 jobs enforced.
- **Selection syntax** for execution: single index, range (`1-3`), list (`1;3`), combined (`1-3;5`).
- **EasyLog library** (`EasyLog.dll`): reusable daily JSON logger with thread-safe, atomic writes.
- **Path normalization** in logs — UNC paths preserved, local Windows paths wrapped with `\\?\`.
- **Corrupted log recovery** — unreadable day files are quarantined (`*.corrupted-<ts>-<guid>`) instead of dropped.
- **State tracking** via a single `state.json`, updated in real time (start, per-file progress, end).
- **Job repository** persisting the 5-job list to `jobs.json` atomically.
- **Internationalization**: English and French messages loaded from `Resources/{lang}.json`, hot-switchable at runtime.
- **AppConfig** singleton reading `appsettings.json` — all paths (logs, state, jobs) are configurable.
- **Unit and integration tests** (41 tests covering logger, backup strategies, state tracker, job repository, backup manager, command parser).
- **User manual** (`docs/user-manual.md`, one page) and **customer support guide** (`docs/support-client.md`).
- **UML diagrams**: use case, class, activity, sequence (`docs/diagrams/`).
- **CI pipeline** — `.NET Build` workflow running `dotnet build`, `dotnet test`, and `dotnet format --severity warn` on Ubuntu 24.04.
- **Automated PR review** via Claude Code Review with project context in `CLAUDE.md`.
- **Commit convention** documented in `docs/COMMIT_CONVENTION.md` (Conventional Commits + branch naming).

### Fixed

- Log file tmp-name collisions on concurrent append (`*.tmp` switched to GUID-suffixed names).
- `dotnet format` catch-clause indentation violations in `JsonDailyLogger`.
- `AppConfig` path resolution now relative to the executable, not the working directory.
- Shipped `appsettings.json` no longer contains developer-only path overrides.
- `BackupManager` rejects empty job names, source paths, and target paths.
- Corrupted log files are preserved under a timestamped backup name instead of being overwritten.

### Changed

- `JobRepository.Load()` returns `IReadOnlyList<BackupJob>` instead of `List<BackupJob>`.
- Test classes touching shared singletons joined to `StateCollection` to disable parallelization and prevent race conditions.
- Exception messages in `BackupManager` carry both a translation key and human-readable detail (`"error.max_jobs: Maximum 5 jobs allowed."`).

### Security

- Log and state paths default to paths under the executable — no hardcoded `C:\temp` or similar.

[1.0.1]: https://github.com/JulianKerignard/Genie_Logiciel_Groupe4/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/JulianKerignard/Genie_Logiciel_Groupe4/releases/tag/v1.0.0

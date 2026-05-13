# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [3.0.0] — 2026-05-13

Major release introducing parallel execution, real-time remote control, and
centralized log collection. The single-machine sequential engine of v2 is
replaced by a parallel orchestrator with cross-job coordination
(priority extensions, large-file gate) and a TCP control plane that a
standalone Avalonia console can drive from another workstation.
`EasyLog.dll` stays additive (no member removed); the v3 wire format
(`CommandDto` / `EventDto` / `JobProgressDto` / `JobStateEnum`) is the new
public surface for the protocol and is frozen for the v3.x line.

### Added

- **Parallel backup orchestrator** — `ParallelBackupOrchestrator` runs up
  to `max_parallel_jobs` jobs concurrently (default 4, configurable in
  `appsettings.json`). Replaces the v1/v2 sequential loop.
- **Priority extensions gate** — `IPriorityGate` blocks non-priority files
  on any job as long as at least one job has priority-extension files
  pending. Extensions configured via `priority_extensions` in settings.
- **Big-file gate** — `IBigFileGate` (`SemaphoreSlim(1)`) serializes the
  transfer of files at or above `large_file_threshold_kb` across every
  job in flight. Below the threshold, files copy with zero gate overhead.
- **Per-job and global Play / Pause / Stop** — `IJobController` exposes
  `Pause`, `Resume`, `Stop`, `PauseAll`, `ResumeAll`, `StopAll`. Pause
  halts at the next file boundary and preserves the resume cursor; Stop
  cancels the per-job CTS and transitions the job to `Inactive`.
- **Real-time progress** — `JobProgressDto` (state, files left, total
  files, bytes left, total bytes) published on `IEventBus` and broadcast
  to all connected remote consoles.
- **Auto-pause on business-software detection (v3 contract)** —
  `BusinessSoftwareControllerBridge` calls `PauseAll()` when a configured
  process appears and `ResumeAll()` when it disappears. Replaces the v2
  refuse-to-start contract.
- **TCP remote console server** — `TcpRemoteConsoleServer` accepts
  multiple concurrent clients over newline-delimited JSON; broadcasts
  events to every connected client with per-client write locks for
  ordering. Configurable via `remote_console_enabled` and
  `remote_console_port`.
- **Standalone Avalonia remote console** — `EasySave.RemoteConsole`
  project ships as a separate desktop app that connects over the TCP
  protocol and pilots the engine from another workstation. TOFU
  validation of the server thumbprint via a local `known_hosts` file.
- **Optional TLS on the remote console** — `SelfSignedCertProvider`
  generates a self-signed RSA-2048 PFX on first run; server wraps the
  socket in `SslStream` (TLS 1.2 / 1.3) when `remote_console_tls_enabled`
  is set. See `docs/v3-remote-console-tls.md`.
- **CryptoSoft mono-instance enforcement** — `CryptoSoftAdapter` acquires
  a named system mutex (`Global\ProSoft.CryptoSoft.SingleInstance`) before
  every encryption call so two EasySave instances on the same machine
  cannot run CryptoSoft concurrently. Handles `AbandonedMutexException`.
- **HTTP log shipper** — `HttpLogShipper` (in `EasyLog`) POSTs each
  `LogEntry` to a centralized HTTP endpoint with an exponential backoff
  schedule (1 s, 2 s, 5 s, 10 s, 30 s) and zero-loss replay: entries stay
  in the in-memory buffer until the POST succeeds.
- **`LogMode` selector** — `Local` (default, v1/v2 behaviour),
  `Centralized` (skip local file, ship only), or `Both`. Configured via
  `log_mode` and `log_centralized_endpoint` in `appsettings.json` and
  wired in both the CLI (`Program.cs`) and the GUI
  (`App.axaml.cs`) entry points via `DailyLoggerFactory`.
- **LogCentralizer service** — ASP.NET Core minimal-API container
  (`LogCentralizer/Dockerfile`) that receives shipped log entries,
  drains them through a single `Channel<T>` writer, and persists one
  `yyyy-MM-dd.jsonl` per day under `/var/log/easysave`. Compose file +
  admin manual in `docs/v3-log-centralizer-admin.md`.
- **`MachineName` and `UserName` on `LogEntry`** (additive, optional) —
  stamped by `Json` / `XmlDailyLogger.Append` from `Environment` so a
  single centralized daily file can distinguish entries by their origin.
  `EasyLog.dll` minor bump (v1.2.0): existing v1.0 / v1.1 consumers
  remain compatible (fields are nullable, `[JsonIgnore(WhenWritingNull)]`).
- **`FailedFiles` counter on `StateEntry`** — incremented per file when
  `ProcessFile` returns `FileTransferTimeMs < 0`. Lets an operator spot
  a partially-failed run from `state.json` without parsing the daily log.
- **`IStateRepository`** abstraction over `state.json` persistence in
  `StateTracker` (thread-safe in-memory cache with a 200 ms throttled
  disk flush). Decouples the writer from the file format and unblocks
  future alternative backends.
- **`IEventBus`** (`ChannelEventBus`) — single bus that the engine
  publishes to and bridges (`StateTrackerEventBridge`,
  `RemoteConsoleBroadcastBridge`) subscribe to. Replaces the v2 direct
  coupling between `StateTracker` and consumers.
- **Testcontainers e2e suite** for `LogCentralizer` — spins a real Docker
  container, posts entries through the shipper, and asserts the daily
  file shape and content match the wire format.
- **V3 UML diagrams** — class diagram + sequence diagrams for
  Play / Pause / Stop / PauseAll under `docs/diagrams/`.
- **V3 manual-acceptance recettes** under `docs/recettes/` — remote
  console, parallel orchestrator, priority extensions, business-software
  auto-pause, backward-compat with v1/v2, mix-pause scenarios.

### Changed

- `JsonDailyLogger` and `XmlDailyLogger` accept an optional `ILogShipper`
  and a `LogMode` (default `Local`). When the shipper is `null`, the
  effective mode is forced back to `Local` so a misconfigured central
  setup never silently drops entries.
- `JsonDailyLogger` writer task is channel-based (`Channel<WriteRequest>`,
  `SingleReader = true`) so concurrent `Append` calls from multiple jobs
  do not serialize on a file-rewrite lock. The v1 `Append` durability
  contract is preserved: the method does not return until the entry is
  flushed.
- `BackupManager.ExecuteJob` honors `PauseGate` and `CancellationToken`
  at every file boundary so per-job Pause / Resume / Stop commands take
  effect promptly without leaving partial files on disk.
- `StateTracker` is now thread-safe (read/write cache) with a throttled
  disk flush; the v2 lock-per-update pattern was rewritten on top of the
  new `IStateRepository` abstraction.
- `TcpRemoteConsoleServer.ReadLineAsync` is capped at 64 KB per line to
  prevent an OOM if a misbehaving client never sends `\n`.

### Fixed

- `HttpLogShipper.Dispose` is idempotent (`Interlocked.CompareExchange`)
  so a double dispose during shutdown does not throw
  `ObjectDisposedException` after the CTS has already fired.
- `Program.cs` `ProcessExit` handler disposes the logger before the
  shipper so the logger's writer loop can flush any pending forward
  without hitting a disposed `HttpClient`.
- `App.axaml.cs` `DisposeServices` keeps the `HttpLogShipper` alive on a
  static field and drains it as the last step so an in-flight POST is
  not abandoned when the window closes.
- `LogCentralizer` Docker image now runs as a non-root user that can
  write to the bind-mounted log directory; previous builds wrote as
  root and crashed when the host mount was owned by the runtime user.
- `TcpRemoteConsoleServer` brutal-disconnect cleanup race fixed: the
  per-client `WriteLock` and `Writer` are disposed only after the
  client entry is removed from the `ConcurrentDictionary`, so
  `BroadcastAsync` cannot acquire a freshly disposed lock.

### Limitations / known gaps

- The CLI `RemoteConsoleEnabled = true` path still wires
  `ParallelBackupOrchestratorStub` instead of the real orchestrator —
  commands routed via the CLI remote console are no-ops. The GUI path
  (`App.axaml.cs`) wires the real orchestrator. Tracked for v3.1.
- `Avalonia` package reference is pinned to `12.0.1` in the UI projects;
  the public stable line is 11.x. Build is reproducible locally but the
  pin needs to be re-aligned with the official feed before the next
  minor release.

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

[Unreleased]: https://github.com/JulianKerignard/Genie_Logiciel_Groupe4/compare/v2.1.0...HEAD
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

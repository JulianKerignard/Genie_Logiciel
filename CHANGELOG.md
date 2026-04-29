# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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

[Unreleased]: https://github.com/JulianKerignard/Genie_Logiciel_Groupe4/compare/v2.0.0...HEAD
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

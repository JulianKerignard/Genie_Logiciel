# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.1.0] — 2026-04-28

Maintenance release on the v1.x line. v1.x ships **side-by-side** with the
upcoming v2.0; the team continues to patch v1 from the `release/v1.x`
branch while v2 lands on `staging` / `main`.

No public API change — `EasyLog.dll` v1.x contract is preserved; existing
`appsettings.json` overrides keep working unchanged.

### Fixed

- **Data loss on locked `jobs.json`** (`JobRepository.Load`): a transient
  `IOException` (antivirus or backup agent reading the file) used to be
  caught alongside `JsonException`, which renamed the file to
  `*.corrupted-<ts>-<guid>` and made the next run see zero jobs. The
  catches are now split: only `JsonException` quarantines, `IOException`
  returns empty without renaming so the next attempt reads the file
  cleanly once the lock releases.
- **Interactive menu errors on stderr** (`ConsoleUI.ExecuteJobs`): both
  callbacks were `Console.WriteLine`. The interactive menu now matches
  the CLI direct mode and routes errors to `Console.Error.WriteLine`.
- **`CommandParser` range DoS**: an input like `"1-9999999"` previously
  allocated millions of `HashSet` entries before the caller could reject
  the indices. Indices above the cahier v1.0 cap of 5 now make
  `ParseJobSelection` return an empty list, same convention as the
  existing `< 1` branch.
- **`BackupManager.ExecuteAll` typed catch**: the bare
  `catch (Exception)` was silently swallowing programmer errors
  (NullReferenceException, OutOfMemoryException) alongside the expected
  IO failures. Catch is now restricted to `IOException`,
  `UnauthorizedAccessException`, and `DirectoryNotFoundException`.

### Changed

- **Bare i18n keys as exception messages**
  (`BackupManager.AddJob`): `InvalidOperationException` now carries
  `"error.max_jobs"` / `"error.duplicate_job"` instead of the compound
  `"error.max_jobs: Maximum 5 jobs allowed."` format. The CLI's
  split-on-colon hack is gone; `ConsoleUI.AddJob` passes the message
  straight to `LanguageService.T`. Tests assert exact equality on the
  key.

### Documentation

- Sequence diagram replaced by a corrected version (renamed to
  `EasySave v1.1 — Sequence Diagram.png` to follow the existing
  naming pattern).
- New `CommandParserTests` covering valid input, invalid input, and
  above-cap input (23 cases).

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

[Unreleased]: https://github.com/JulianKerignard/Genie_Logiciel_Groupe4/compare/v1.1.0...HEAD
[1.1.0]: https://github.com/JulianKerignard/Genie_Logiciel_Groupe4/compare/v1.0.1...v1.1.0
[1.0.1]: https://github.com/JulianKerignard/Genie_Logiciel_Groupe4/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/JulianKerignard/Genie_Logiciel_Groupe4/releases/tag/v1.0.0

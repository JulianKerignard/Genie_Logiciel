# EasySave

EasySave is a backup management application developed by **ProSoft** (Group 4).
It allows users to create, configure, and run backup jobs from a console interface.

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 10+ / Linux / macOS

## Project structure

```
EasySave.sln
├── src/
│   ├── EasySave/          # Console application (entry point)
│   └── EasyLog/           # Reusable daily JSON logger library
├── tests/
│   ├── EasySave.Tests/    # v1 unit and integration tests (xUnit)
│   └── EasySave.Tests.V2/ # v2 unit and integration tests (xUnit)
└── docs/                  # Conventions and documentation
```

## Build, run & test

```bash
# Restore and build
dotnet build EasySave.sln

# Run the console app (interactive menu)
dotnet run --project src/EasySave

# Run the console app (direct CLI mode — executes jobs 1 to 3 and 5)
dotnet run --project src/EasySave -- "1-3;5"

# Run the full test suite
dotnet test EasySave.sln

# Check formatting (CI runs this with --verify-no-changes)
dotnet format --severity warn
```

## Modules

### EasySave

Console application providing the user interface for managing backup jobs.

### EasyLog

Reusable library that writes daily JSON log files (`yyyy-MM-dd.json`).
Thread-safe, atomic writes, designed to be shared across ProSoft applications.
See [src/EasyLog/README.md](src/EasyLog/README.md) for the public API, usage
examples, and versioning policy.

## Documentation

- [Architecture overview](docs/architecture.md) — layered design, patterns, persistence, execution flow
- [User manual](docs/user-manual.md) — end-user guide (1 page)
- [Customer support guide](docs/support-client.md) — deployment, configuration, and support contacts
- [Changelog](CHANGELOG.md) — release notes (Keep a Changelog format)
- [Architecture Decision Records](docs/adrs/) — design decisions and their rationale
- [UML diagrams](docs/diagrams/) — use case, class, activity, sequence
- [EasyLog DLL documentation](src/EasyLog/README.md) — public API and versioning policy

## Roadmap

### v1.x (current — released)

- Console application, up to 5 backup jobs (Full / Differential).
- `EasyLog.dll` — daily JSON logger, thread-safe, atomic writes.
- Real-time `state.json`, configurable paths via `appsettings.json`.
- English and French UI, hot-switchable.
- Latest: [v1.0.1](https://github.com/JulianKerignard/Genie_Logiciel_Groupe4/releases/tag/v1.0.1).
- Maintained on the [`release/v1.x`](https://github.com/JulianKerignard/Genie_Logiciel_Groupe4/tree/release/v1.x) branch.

### v2.0 (in progress)

EasySave v2.0 evolves the console tool into a graphical application while keeping
the v1.0 services intact. The v1.x `EasyLog.dll` contract is preserved (frozen
public API), so v2.0 reuses the library without recompilation.

- **Cross-platform GUI in Avalonia** (`EasySave.UI`) — replaces the console as the
  primary interface. MVVM pattern, Windows + macOS supported. The console stays
  available as a fallback for scripting and CI.
- **File encryption via CryptoSoft** — selected file extensions (configured in
  `appsettings.json`) are passed through the external CryptoSoft binary during a
  backup. Encryption time and failures are recorded in the daily log. See
  [`docs/cryptosoft-integration.md`](docs/cryptosoft-integration.md) for the
  integration contract.
- **XML logger formatter** — `EasyLog` gains an `ILogFormatter` abstraction so
  daily logs can be written in JSON (current) or XML (with XSD schema). Choice
  exposed in `appsettings.json`.
- **`EncryptionTimeMs` field on `LogEntry`** — nullable, optional, additive
  (forward-compatible with v1.x consumers that ignore unknown fields).
- **Job count limit removed** — v2.0 accepts more than 5 jobs (cahier v2.0
  requirement).
- **Settings UI** — edit `encrypted_extensions`, `business_software_list` and
  language from the GUI without manually touching `appsettings.json`.
- **Restore** (Phase 5 bonus) — restore a backup chain (Full + subsequent Diffs),
  with decryption when needed.
- **Scheduler** (Phase 5 bonus) — run jobs on a recurring schedule.

Tracking: see ClickUp space *EasySave v2.0*, branch
[`v2-dev`](https://github.com/JulianKerignard/Genie_Logiciel_Groupe4/tree/v2-dev)
once created.

### v3.0 (planned, scope to be confirmed)

A new backup mode is expected (incremental). The `IBackupStrategy` interface
introduced in v1.0 already accepts new strategies as a single class addition,
so the backup engine should not need restructuring.

## Contributing

- Follow [commit conventions](docs/COMMIT_CONVENTION.md)
- One branch per feature/fix — never commit directly on `staging` or `main`
- All commits in English, imperative tense

## Team

- **Group 4** — CESI A3 Software Engineering Project

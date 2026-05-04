# EasySave

EasySave is a backup management application developed by **ProSoft** (Group 4).
It allows users to create, configure, and run backup jobs from a cross-platform
Avalonia GUI (v2.0) or from the original console interface (v1.x, still shipped).

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 10+ / Linux / macOS

## Project structure

```
EasySave.sln
├── src/
│   ├── EasySave/          # Console application (entry point)
│   ├── EasySave.UI/       # Avalonia GUI (v2.0, primary interface)
│   └── EasyLog/           # Reusable daily logger library (JSON + XML)
├── tests/
│   ├── EasySave.Tests/    # v1 unit and integration tests (xUnit)
│   └── EasySave.Tests.V2/ # v2 unit and integration tests (xUnit)
└── docs/                  # Conventions and documentation
```

## Build, run & test

```bash
# Restore and build
dotnet build EasySave.sln

# Run the GUI (Avalonia, v2.0 primary interface)
dotnet run --project src/EasySave.UI

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

### EasySave.UI

Avalonia cross-platform GUI (Windows + macOS), primary interface in v2.0.
MVVM pattern, runtime FR/EN switch, settings editor, restore and scheduler.

### EasySave

Console application — kept available for scripting, CI, and headless usage.

### EasyLog

Reusable library that writes daily log files (`yyyy-MM-dd.json` or `.xml`),
with the format selectable at runtime. Thread-safe, atomic writes, designed
to be shared across ProSoft applications. The v1.0 public API stays frozen;
v2.0 adds the optional `EncryptionTimeMs` field and the XML formatter.
See [src/EasyLog/README.md](src/EasyLog/README.md) for the public API, usage
examples, and versioning policy.

## Documentation

- [Architecture overview](docs/architecture.md) — layered design, patterns, persistence, execution flow
- [User manual v1](docs/user-manual.md) — console end-user guide (1 page)
- [User manual v2](docs/user-manual-v2.md) — GUI end-user guide
- [CryptoSoft integration](docs/cryptosoft-integration.md) — encryption contract and behaviour
- [Customer support guide](docs/support-client.md) — deployment, configuration, and support contacts
- [Changelog](CHANGELOG.md) — release notes (Keep a Changelog format)
- [Architecture Decision Records](docs/adrs/) — design decisions and their rationale
- [UML diagrams](docs/diagrams/) — use case, class, activity, sequence
- [Test recipes](docs/recettes/) — manual acceptance scenarios for v2 features
- [EasyLog DLL documentation](src/EasyLog/README.md) — public API and versioning policy

## Roadmap

### v2.0 (current — released)

Latest: [v2.0.0](https://github.com/JulianKerignard/Genie_Logiciel_Groupe4/releases/tag/v2.0.0).

EasySave v2.0 evolves the console tool into a graphical application while keeping
the v1.0 services intact. The v1.x `EasyLog.dll` contract is preserved (frozen
public API), so v2.0 reuses the library additively.

- **Cross-platform GUI in Avalonia** (`EasySave.UI`) — primary interface, MVVM,
  Windows + macOS. The console stays available as a fallback for scripting and CI.
- **File encryption via CryptoSoft** — selected file extensions (configured in
  `appsettings.json`) are passed through the external CryptoSoft binary during a
  backup. Encryption time and failures are recorded in the daily log. Contract
  documented in [docs/cryptosoft-integration.md](docs/cryptosoft-integration.md).
- **XML logger formatter** — `EasyLog` gains an `ILogFormatter` abstraction so
  daily logs can be written in JSON (default) or XML (with XSD schema). Choice
  exposed in `appsettings.json`.
- **`EncryptionTimeMs` field on `LogEntry`** — nullable, optional, additive
  (forward-compatible with v1.x consumers that ignore unknown fields).
- **Job count limit removed** — v2.0 accepts more than 5 jobs (cahier v2.0
  requirement).
- **Settings UI** — edit `encrypted_extensions`, `business_software_list` and
  language from the GUI without manually touching `appsettings.json`.
- **Pause / resume on business software detection** — running jobs auto-pause
  when a configured business application starts, resume when it exits.
- **Restore** — restore a backup chain (Full + subsequent Diffs), with decryption
  when needed.
- **Scheduler** — run jobs on a recurring schedule.
- **Runtime FR/EN switch** — language change without restart.

### v1.x (maintenance)

- Console application, up to 5 backup jobs (Full / Differential).
- `EasyLog.dll` — daily JSON logger, thread-safe, atomic writes.
- Real-time `state.json`, configurable paths via `appsettings.json`.
- English and French UI.
- Latest: [v1.1.0](https://github.com/JulianKerignard/Genie_Logiciel_Groupe4/releases/tag/v1.1.0).
- Maintained on the [`release/v1.x`](https://github.com/JulianKerignard/Genie_Logiciel_Groupe4/tree/release/v1.x) branch.

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

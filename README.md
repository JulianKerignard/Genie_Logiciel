# EasySave

EasySave is a backup management application developed by **ProSoft** (Group 4).
It evolved from a console tool with up to 5 jobs (v1.x) into a graphical
Avalonia application with encryption, scheduler and restore (v2.x), and now
to a fully parallel multi-job engine controllable from a remote operator
console over TCP (v3.0).

Three interfaces ship in the same solution:

- **EasySave.UI** — Avalonia GUI, primary interface since v2.0.
- **EasySave** — console application, kept for scripting / CI / headless usage.
- **EasySave.RemoteConsole** — standalone Avalonia client (v3.0) connecting to
  a running EasySave engine over the network.

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 10+ / Linux / macOS

## Project structure

```
EasySave.sln
├── src/
│   ├── EasySave/             # Console application + backup engine + repositories
│   ├── EasySave.UI/          # Avalonia GUI (v2.x, primary interface)
│   ├── EasySave.Shared/      # v3 wire-format DTOs (CommandDto, EventDto, …) and IEventBus
│   └── EasyLog/              # Reusable daily logger library (JSON + XML)
├── EasySave.RemoteConsole/   # v3 standalone Avalonia client (NDJSON-over-TCP)
├── tests/
│   ├── EasySave.Tests/       # v1 unit and integration tests (xUnit)
│   └── EasySave.Tests.V2/    # v2 + v3 unit and integration tests (xUnit)
└── docs/                     # Conventions, architecture, recettes, diagrams
```

## Build, run & test

```bash
# Restore and build
dotnet build EasySave.sln

# Run the GUI (Avalonia, primary interface since v2.0)
dotnet run --project src/EasySave.UI

# Run the v3 standalone remote console client (connects to a running engine
# whose appsettings.json has remote_console_enabled = true)
dotnet run --project EasySave.RemoteConsole

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

Avalonia cross-platform GUI (Windows + macOS), primary interface since v2.0.
MVVM pattern, runtime FR/EN switch, settings editor, restore and scheduler.
In v3.0 it also hosts the in-process TCP server that the remote console
client connects to.

### EasySave

Console application — kept available for scripting, CI, and headless usage.
Same backup engine as the GUI; in v3.0 it can run with the parallel
orchestrator and expose the same remote-console server when its
`appsettings.json` enables it.

### EasySave.RemoteConsole

V3.0 only — standalone Avalonia client that connects to a running EasySave
engine over TCP. Shows live job progress, sends Pause / Play / Stop commands,
and surfaces a command-history audit panel so multiple operators stay in
sync. Optional TLS handshake with TOFU known_hosts. Depends only on
`EasySave.Shared`, not on the engine project.

### EasySave.Shared

V3.0 wire-format DTOs and the `IEventBus` abstraction shared between the
engine and the remote console. Contains `CommandDto`, `EventDto`,
`JobProgressDto`, plus the `EventType` / `CommandType` / `JobStateEnum`
discriminators. Strict no-dependency on engine or UI — both sides depend on
it, it depends on neither. See
[src/EasySave.Shared/README.md](src/EasySave.Shared/README.md) for the
NDJSON framing rule and the append-only enum evolution policy.

### EasyLog

Reusable library that writes daily log files (`yyyy-MM-dd.json` or `.xml`),
with the format selectable at runtime. Thread-safe, atomic writes, designed
to be shared across ProSoft applications. The v1.0 public API stays frozen;
v2.0 adds the optional `EncryptionTimeMs` field and the XML formatter;
v3.0 adds optional centralized log shipping (Docker-hosted endpoint) and
new `LogEvent` discriminator values (`JobPaused`, `JobResumed`,
`BusinessSoftwareAutoPaused`, …) — all additive.
See [src/EasyLog/README.md](src/EasyLog/README.md) for the public API, usage
examples, and versioning policy.

## Documentation

- [Architecture overview](docs/architecture.md) — layered design, patterns, persistence, execution flow
- [User manual v1](docs/user-manual.md) — console end-user guide (1 page)
- [User manual v2](docs/user-manual-v2.md) — GUI end-user guide
- [CryptoSoft integration](docs/cryptosoft-integration.md) — encryption contract and behaviour
- [V3 remote console TLS](docs/v3-remote-console-tls.md) — opt-in TLS, self-signed cert, TOFU known_hosts
- [Customer support guide](docs/support-client.md) — deployment, configuration, and support contacts
- [Changelog](CHANGELOG.md) — release notes (Keep a Changelog format)
- [Architecture Decision Records](docs/adrs/) — design decisions and their rationale
- [UML diagrams](docs/diagrams/) — use case, class, activity, sequence (v1, v2, v3)
- [Task repartition v1](docs/EasySave_v1_0_Repartition_Taches.md) / [v2](docs/EasySave_v2_0_Repartition_Taches.md) / [v3](docs/EasySave_v3_0_Repartition_Taches.md) — per-developer scope per phase
- [Test recipes](docs/recettes/) — manual acceptance scenarios (pause/resume, parallel, big-file gate, priority extensions, TLS, …)
- [EasyLog DLL documentation](src/EasyLog/README.md) — public API and versioning policy
- [EasySave.Shared README](src/EasySave.Shared/README.md) — v3 wire format (NDJSON) and DTOs

## Roadmap

### v3.0 (current — soutenance)

Latest: [v3.0.0](https://github.com/JulianKerignard/Genie_Logiciel_Groupe4/releases/tag/v3.0.0).

EasySave v3.0 turns the sequential v2 engine into a **parallel multi-job
runner** and introduces a **remote operator console** that can drive any
running engine over TCP. Every v2.x feature stays in place; the v3 additions
are additive on top.

- **Parallel backup execution** — jobs run concurrently up to
  `max_parallel_jobs` (`appsettings.json`). Each job runs in its own
  `JobExecutionContext` (per-job `CancellationTokenSource`, pause gate,
  progress snapshot, logger) so Pause / Play / Stop on one job never
  disturb the others.
- **Priority extensions** — files whose extension is listed in
  `priority_extensions` are copied first. **No non-priority file from any
  job can be backed up while priority files are still pending on at least
  one job.** Cross-job barrier enforced by a shared `IPriorityGate`.
- **Big-file gate** — at most one file above `large_file_threshold_kb` may
  transfer at a time across the entire engine, so parallel jobs don't
  saturate the disk / network on big assets.
- **Play / Pause / Stop, per job and global** — `IJobController` (the
  orchestrator itself) exposes `Pause`/`Resume`/`Stop(jobName)` plus the
  `PauseAll`/`ResumeAll`/`StopAll` variants. Pause is effective at the
  next file boundary (the file in progress is never cut). State and log
  reflect the transitions (`state.json` `Active`/`Paused`/`Inactive` +
  `LogEvent.JobPaused` / `JobResumed` rows).
- **Business-software auto-pause via the controller** — when a configured
  process appears, the watcher now calls `IJobController.PauseAll()` (v2
  used per-job cancellation); when the last instance is gone, `ResumeAll()`
  fires automatically. Logged as `BusinessSoftwareAutoPaused` /
  `BusinessSoftwareAutoResumed`.
- **Remote operator console** (`EasySave.RemoteConsole`) — standalone
  Avalonia client that connects to a running engine over TCP. Shows live
  job progress, sends Pause / Play / Stop, and surfaces a command-history
  audit panel. Wire format: NDJSON (one JSON line terminated by `\n`),
  DTOs in `EasySave.Shared`.
- **Multi-console synchronisation** — when multiple consoles are
  connected, every command from any operator is broadcast as a
  `CommandReceived` event so every console sees who did what.
- **Optional TLS on the socket** — `remote_console_tls_enabled = true`
  wraps the TCP socket in `SslStream`. The engine generates a self-signed
  PFX on first run; the client uses a TOFU known_hosts policy (modelled
  on OpenSSH). See [docs/v3-remote-console-tls.md](docs/v3-remote-console-tls.md).
- **CryptoSoft mono-instance** — only one CryptoSoft process can run at a
  time on a given host (cahier v3 constraint).
- **Centralized log shipping** — optional HTTP endpoint
  (`log_centralized_endpoint`) ships every log line to a Docker-hosted
  collector in addition to (or instead of) the local daily file
  (`log_mode = local | centralized | both`).

### v2.x (maintenance — Avalonia GUI)

Latest: [v2.1.0](https://github.com/JulianKerignard/Genie_Logiciel_Groupe4/releases/tag/v2.1.0).
Maintained on the [`release/v2.x`](https://github.com/JulianKerignard/Genie_Logiciel_Groupe4/tree/release/v2.x) branch.

The v2 release evolved the console tool into a graphical application while
keeping the v1.0 services intact. The v1.x `EasyLog.dll` contract is
preserved (frozen public API), so v2 reuses the library additively.

- **Cross-platform GUI in Avalonia** (`EasySave.UI`) — primary interface,
  MVVM, Windows + macOS. The console stays available as a fallback for
  scripting and CI.
- **File encryption via CryptoSoft** — selected file extensions
  (configured in `appsettings.json`) are passed through the external
  CryptoSoft binary during a backup. Encryption time and failures are
  recorded in the daily log. Contract documented in
  [docs/cryptosoft-integration.md](docs/cryptosoft-integration.md).
- **XML logger formatter** — `EasyLog` gains an `ILogFormatter`
  abstraction so daily logs can be written in JSON (default) or XML (with
  XSD schema). Choice exposed in `appsettings.json`.
- **`EncryptionTimeMs` field on `LogEntry`** — nullable, optional,
  additive (forward-compatible with v1.x consumers that ignore unknown
  fields).
- **Job count limit removed** — v2.0 accepts more than 5 jobs.
- **Settings UI** — edit `encrypted_extensions`, `business_software_list`
  and language from the GUI without manually touching `appsettings.json`.
- **Pause / resume on business software detection** — running jobs
  auto-pause when a configured business application starts, resume when
  it exits.
- **Restore** — restore a backup chain (Full + subsequent Diffs), with
  decryption when needed.
- **Scheduler** — run jobs on a recurring schedule.
- **Runtime FR/EN switch** — language change without restart.

### v1.x (maintenance)

- Console application, up to 5 backup jobs (Full / Differential).
- `EasyLog.dll` — daily JSON logger, thread-safe, atomic writes.
- Real-time `state.json`, configurable paths via `appsettings.json`.
- English and French UI.
- Latest: [v1.1.0](https://github.com/JulianKerignard/Genie_Logiciel_Groupe4/releases/tag/v1.1.0).
- Maintained on the [`release/v1.x`](https://github.com/JulianKerignard/Genie_Logiciel_Groupe4/tree/release/v1.x) branch.

### v4.0 (proposals)

Discussion topic for the soutenance. Possible directions, to weigh
benefit vs development cost:

- Incremental backup strategy on top of `IBackupStrategy` (single-class
  addition, no engine restructuring).
- Multi-engine federation — one console driving several engines on
  different hosts.
- Authentication on the remote-console socket (mTLS or token).
- Native containerisation of the engine (Linux daemon image).

## Contributing

- Follow [commit conventions](docs/COMMIT_CONVENTION.md)
- One branch per feature/fix — never commit directly on `staging` or `main`
- All commits in English, imperative tense

## Team

- **Group 4** — CESI A3 Software Engineering Project

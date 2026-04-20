# Project context for automated reviewers

This file is loaded by the Claude Code Action during PR reviews. Keep reviews aligned with the constraints below. Do not suggest improvements that contradict them.

## Project

- **EasySave** — console backup tool for the fictional editor **ProSoft**
- School project: CESI PGE A3 FISE INFO, Software Engineering track
- Team of 4 developers, solo owners per zone (see `docs/COMMIT_CONVENTION.md`)
- This is **v1.0**. v2.0 will bring WPF/MVVM. v3.0 spec unknown for now.

## Solution layout

```
EasySave.sln
└── src/
    ├── EasySave/      console executable (entry point)
    └── EasyLog/       reusable class library (DLL) imposed by the cahier
```

Two projects is a **hard requirement from the cahier** — `EasyLog.dll` must stay reusable by other ProSoft applications.

## Hard constraints (from cahier des charges v1.0)

Do not propose changes that break any of these:

- **C# on .NET 8.0**, console only. No GUI in v1.0.
- **5 backup jobs max.** More = reject.
- Two backup types: `Full` and `Differential`. No other.
- Sources and targets support **local disks, external drives, network shares**.
- Recursive traversal of subdirectories.
- **English only** for code, identifiers, comments, commit messages, PR bodies.
- **EN + FR localization** for end-user messages (via `Resources/*.json`).
- Daily JSON logs written in **real time**, one file per day (`yyyy-MM-dd.json`).
- Log paths must be in **UNC format** (e.g. `\\server\share\file`).
- `FileTransferTimeMs < 0` is the **error signal** for a failed file copy.
- Logs directory and state file location must be **configurable**. No hardcoded paths like `C:\temp`.
- Single `state.json` (not one per job).
- `EasyLog` public API is **frozen in v1.0** — backward compatibility is mandatory.
- Files **under 500 lines**. Refactor if a file grows past that.
- **Zero duplication.** This is an explicit grading criterion.
- User manual must fit on **one page**.

## Architecture

- Layered: `CLI -> Services -> Models`. Models know nothing, Services ignore CLI.
- Strategy pattern for backup types (`IBackupStrategy`).
- Singleton for `StateTracker` (one `state.json`).
- Repository for `JobRepository` (abstracts persistence).
- Constructor-based Dependency Injection, wired manually in `Program.Main`.
- `sealed` on concrete classes by default.
- `Nullable` enabled everywhere.

## Conventions

- **Branch naming**: `feat/xxx`, `fix/xxx`, `refactor/xxx`, `docs/xxx`, `chore/xxx`, `ci/xxx`, `hotfix/xxx`. Kebab-case.
- **Commits**: Conventional Commits (`feat(scope): ...`), imperative, lowercase subject, no trailing period.
- **PR target**: `staging`. Only the release merge goes to `main`.
- **Tags**: `v1.0.0`, `v1.1.0`, `v2.0.0`, `v3.0.0` on `main` at each livrable. No intermediate alpha/beta tags.
- See `docs/COMMIT_CONVENTION.md` for the full detail.

## Review focus

When reviewing a PR, prioritize:

1. **Real bugs** (null derefs, race conditions, unhandled exceptions that break the flow, wrong UNC handling, silent data loss).
2. **Cahier violations** (hardcoded paths, >500 line files, duplicated logic, breaking the `EasyLog` v1.0 contract).
3. **Security hygiene** (command injection, path traversal, secrets in commits).
4. **Test coverage gaps on critical paths** (`JsonDailyLogger` concurrency, `BackupManager` strategy dispatch, `CommandParser` edge cases).

## Suggestions to avoid

- **Do not suggest external libraries** for logging, DI, CLI parsing, or JSON (Serilog, NLog, MediatR, CommandLineParser, Newtonsoft.Json, AutoMapper, Polly). We stay on `System.Text.Json` + custom code. `EasyLog` is the logger by contract.
- **Do not suggest redesigning `IDailyLogger`** — its v1.0 surface is frozen. Propose a `IDailyLoggerV2` only if strictly necessary.
- **Do not push for async** everywhere. v1.0 is synchronous by design; `async` lands in v2.0 if needed.
- **Do not bikeshed on micro-style** (spaces, ordering, naming micro-variants). `.editorconfig` + `dotnet format` run in CI and own that territory.
- **Do not suggest heavy test frameworks** (Fluent Assertions, Moq, AutoFixture). Plain xUnit is enough.
- **Do not suggest `DateTime.UtcNow` for daily log file names.** The daily file uses local time on purpose — it aligns with the operator's business day.
- **Do not suggest** adding AI or author attribution (`Co-Authored-By: Claude`, "Generated with", etc.) to commit messages or code. These are forbidden by team policy.

## Language rules

- All code, identifiers, comments, log messages, and PR descriptions in **English**.
- French is allowed in the internal team docs under `docs/` (`COMMIT_CONVENTION.md` is bilingual, that is fine).
- User-facing UI strings live in `Resources/en.json` and `Resources/fr.json`. The C# code only references translation keys.

## When in doubt

- If a change seems to break cahier compliance, say so explicitly.
- If a suggestion is stylistic only, mark it as `nit:` so the author can skip it.
- Always end reviews with a final `Verdict: OK` or `Verdict: Changes requested` line.

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
└── docs/                  # Conventions and documentation
```

## Build & Run

```bash
# Restore and build
dotnet build EasySave.sln

# Run the console app
dotnet run --project src/EasySave
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

- [User manual](docs/user-manual.md) — end-user guide (1 page)
- [Customer support guide](docs/support-client.md) — deployment, configuration, and support contacts
- [Changelog](CHANGELOG.md) — release notes (Keep a Changelog format)
- [Architecture Decision Records](docs/adrs/) — design decisions and their rationale
- [UML diagrams](docs/diagrams/) — use case, class, activity, sequence

## Contributing

- Follow [commit conventions](docs/COMMIT_CONVENTION.md)
- One branch per feature/fix — never commit directly on `staging` or `main`
- All commits in English, imperative tense

## Team

- **Group 4** — CESI A3 Software Engineering Project

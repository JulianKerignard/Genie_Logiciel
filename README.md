# EasySave

Console backup software for ProSoft, built on .NET 8.

## Projects

| Project | Type | Description |
|---|---|---|
| `src/EasySave` | Console app (net8.0) | Backup engine and CLI entry point |
| `src/EasyLog` | Class library (net8.0) | Daily JSON logger reusable across ProSoft applications |

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows, macOS or Linux

## Build

```bash
dotnet build EasySave.sln
```

## Run

```bash
dotnet run --project src/EasySave
```

## Test

```bash
dotnet test EasySave.sln
```

## Repository layout

```
EasySave.sln
src/
  EasySave/        # console application
  EasyLog/         # logging library (IDailyLogger, JsonDailyLogger)
docs/
  COMMIT_CONVENTION.md
```

## Contributing

Read [`docs/COMMIT_CONVENTION.md`](docs/COMMIT_CONVENTION.md) before opening a pull request. Work on a dedicated branch (`feat/...`, `fix/...`, etc.), open the PR against `staging`.

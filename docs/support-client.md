# EasySave — Customer Support Guide

This document is intended for ProSoft support technicians and system administrators deploying EasySave on client machines.

## System requirements

| Item | Requirement |
| --- | --- |
| Operating system | Windows 10/11, Linux (x64), macOS 12+ |
| Runtime | .NET 8.0 Runtime |
| Disk space | 50 MB for the application, plus space for backups |
| Network access | Required only if sources or targets live on network shares |

The runtime can be downloaded from <https://dotnet.microsoft.com/download/dotnet/8.0>.

## Installation layout

The application is split between the install directory (binaries, read-only) and the per-user data directory (runtime files, writable).

### Install directory

```
<install-dir>/
├── EasySave(.exe)           # Entry point
├── EasyLog.dll              # Logger library
├── appsettings.json         # Configuration file (editable)
└── Resources/
    ├── en.json
    └── fr.json
```

The `<install-dir>` depends on the deployment method (MSI, ZIP extraction, or `dotnet publish` output). Typical locations per platform:

- Windows: `C:\Program Files\ProSoft\EasySave\`
- Linux: `/opt/prosoft/easysave/`
- macOS: `/Applications/EasySave.app/Contents/MacOS/` or `/usr/local/prosoft/easysave/`

### Runtime data directory (per user)

Logs, state, and job definitions are written under the OS-standard per-user application data directory — never in `C:\temp` nor in the install directory (which would require elevation on Windows):

- Windows: `%AppData%\ProSoft\EasySave\` (resolves to `C:\Users\<user>\AppData\Roaming\ProSoft\EasySave\`)
- Linux: `~/.config/ProSoft/EasySave/`
- macOS: `~/.config/ProSoft/EasySave/`

```
<data-dir>/
├── Logs/                   # Daily log files (yyyy-MM-dd.json)
├── state.json              # Real-time state of the 5 jobs
└── jobs.json               # Persisted job definitions
```

The directory is created automatically on the first run. All three paths are overridable via `appsettings.json` (see below) if the customer needs to redirect them to a network share or a service account.

## Configurable locations

All paths below can be overridden by editing `appsettings.json`. Use the path syntax native to the target platform.

Windows example:

```json
{
  "LogDirectory": "\\\\fileserver\\ProSoft\\logs",
  "StateFilePath": "C:\\ProgramData\\ProSoft\\EasySave\\state.json",
  "JobsFilePath": "C:\\ProgramData\\ProSoft\\EasySave\\jobs.json",
  "Language": "en"
}
```

Linux / macOS example:

```json
{
  "LogDirectory": "/var/log/prosoft/easysave",
  "StateFilePath": "/var/lib/prosoft/easysave/state.json",
  "JobsFilePath": "/var/lib/prosoft/easysave/jobs.json",
  "Language": "en"
}
```

| Key | Purpose | Default (Windows) | Default (Linux / macOS) |
| --- | --- | --- | --- |
| `LogDirectory` | Folder where daily JSON logs are written | `%AppData%\ProSoft\EasySave\Logs` | `~/.config/ProSoft/EasySave/Logs` |
| `StateFilePath` | Full path of the single `state.json` | `%AppData%\ProSoft\EasySave\state.json` | `~/.config/ProSoft/EasySave/state.json` |
| `JobsFilePath` | Full path of the job definitions file | `%AppData%\ProSoft\EasySave\jobs.json` | `~/.config/ProSoft/EasySave/jobs.json` |
| `Language` | UI language (`en` or `fr`) | `en` | `en` |

Network shares must be given in **UNC format** (`\\server\share\folder`). Changes take effect on the next application startup.

## Change the configuration

1. Stop any running EasySave instance.
2. Open `appsettings.json` with a text editor.
3. Update the relevant keys. Leave unused keys unchanged.
4. Save the file and restart EasySave.

Invalid JSON falls back to the defaults without crashing the application; verify changes with a dry run (`EasySave list`) before scheduling production backups.

## Logs and troubleshooting

- Daily logs: `<LogDirectory>/yyyy-MM-dd.json`, one file per calendar day (local time).
- State file: `<StateFilePath>`, updated in real time while jobs run.
- A negative `FileTransferTimeMs` in a log entry indicates a copy failure on that file — the job continues with the next file.

## Support contact

ProSoft support is included under the following terms:

- **Annual fee:** 12% of the purchase price, billed yearly.
- **Availability:** 5 days a week, 08:00–17:00 local time.
- **Channels:** dedicated email and ticketing portal provided with the purchase contract.

Full pricing and service-level terms are defined in the *ProSoft Pricing Policy* document delivered with the contract.

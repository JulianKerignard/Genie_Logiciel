# EasySave — User Manual

EasySave is a console backup tool for ProSoft. It manages up to **5 backup jobs** and runs them on demand (full or differential).

## Launch

Two modes are available once the application is installed:

- **Interactive menu:** `EasySave`
- **Command-line:** `EasySave <command> [arguments]`

## Manage jobs

| Action | Command | Notes |
| --- | --- | --- |
| List jobs | `EasySave list` | Shows name, type, source, target. |
| Create a job | `EasySave create <name> <source> <target> <full\|diff>` | Max 5 jobs; names are unique. |
| Delete a job | `EasySave delete <name>` | Removes the job definition only. |

Sources and targets accept local disks, external drives, and network shares (UNC paths such as `\\server\share\folder`).

## Run jobs

Jobs are addressed by their position (1 to 5) in the list returned by `list`.

| Syntax | Effect |
| --- | --- |
| `1` | Run job 1 only. |
| `1-3` | Run jobs 1, 2, 3 (range). |
| `1;3` | Run jobs 1 and 3 (selection). |
| `1-3;5` | Run jobs 1, 2, 3 and 5 (combined). |

From the CLI: `EasySave run 1-3;5`. From the menu: choose **Run**, then enter the same expression.

## Change the language

The UI supports **English** and **French**.

- From the menu: **Settings → Language**.
- From the configuration file: set `"Language": "en"` or `"Language": "fr"` in `appsettings.json`, then restart.

## Where are the logs?

Daily logs are written in real time as JSON files named `yyyy-MM-dd.json` inside the log directory. The default location is `data/logs/` next to the executable, and it is configurable from `appsettings.json` (see the support guide). Each entry records the job name, source, target, size, and transfer time.

A failed file copy is recorded with a **negative transfer time** (`FileTransferTimeMs < 0`).

# CryptoSoft Integration Contract (v2.0)

CryptoSoft is the external encryption tool provided by ProSoft. EasySave v2.0 invokes
it as a child process to encrypt selected files during a backup job.

This document is the integration contract that the EasySave side relies on. Any change
on the CryptoSoft side that breaks one of these points must be coordinated with Dev2
(Backup) before merging.

## Deployment

- **Binary**: `CryptoSoft.exe` (Windows) — single self-contained executable.
- **Distribution**: copied manually onto each operator workstation. Not bundled in
  the EasySave installer (legal/licensing constraint from ProSoft).
- **Default install location** (recommended, not mandatory):
  - Windows: `C:\Program Files\ProSoft\CryptoSoft\CryptoSoft.exe`
  - Linux/macOS: not supported in v2.0.

## Configuration

Path is resolved through `appsettings.json` via two new keys:

```json
{
  "Language": "en",
  "CryptoSoftPath": "C:\\Program Files\\ProSoft\\CryptoSoft\\CryptoSoft.exe",
  "CryptoSoftExtensions": ".docx,.pdf,.xlsx",
  "CryptoSoftTimeoutMs": 30000
}
```

| Key | Type | Default | Meaning |
|---|---|---|---|
| `CryptoSoftPath` | string | `""` | Absolute path to the executable. Empty = encryption disabled. |
| `CryptoSoftExtensions` | string | `""` | Comma-separated list of file extensions to encrypt (lowercase, leading dot). Empty = nothing is encrypted. |
| `CryptoSoftTimeoutMs` | int | `30000` | Per-file timeout for the CryptoSoft child process, in milliseconds. |

When `CryptoSoftPath` is empty, EasySave **must not fail**: the file is copied as-is
and the log entry records `EncryptionTimeMs = 0` (no encryption performed). See
[Log entry contract](#log-entry-contract) below.

## CLI contract

EasySave calls CryptoSoft synchronously, one file at a time:

```
CryptoSoft.exe <source-file> <target-file>
```

| Argument | Description |
|---|---|
| `source-file` | Absolute path to the plaintext file to encrypt. |
| `target-file` | Absolute path where the encrypted file must be written. Parent directory always exists when EasySave invokes CryptoSoft. |

CryptoSoft is responsible for reading `source-file`, encrypting its content, and
writing the encrypted bytes to `target-file`. EasySave never touches the bytes.

## Return code (encryption time)

CryptoSoft signals the elapsed encryption time **through the process exit code**:

| Exit code | Meaning |
|---|---|
| `>= 0` | Encryption succeeded. Value = elapsed time in milliseconds. |
| `< 0` | Encryption failed. The negative value is an opaque error code. |

EasySave logs this value into a new `LogEntry.EncryptionTimeMs` field (see
[Log entry contract](#log-entry-contract)). The v1.0 `FileTransferTimeMs`
field stays reserved for the file copy duration and is unaffected.

A timeout on the EasySave side wraps the call. The duration is configurable via
`CryptoSoftTimeoutMs` in `appsettings.json` (default: 30000 ms). If the timeout
fires, EasySave kills the process and logs `EncryptionTimeMs = -1`.

## Log entry contract

EasySave logs **two distinct durations per file** in v2.0:

| Field | Source | Meaning |
|---|---|---|
| `FileTransferTimeMs` | Stopwatch around `File.Copy` | File copy duration in ms (v1.0 contract, unchanged). Negative = copy failed. |
| `EncryptionTimeMs` | CryptoSoft exit code | Encryption duration in ms. `0` = encryption not performed (path empty or extension out of scope). Negative = encryption failed. |

> **Open point with Dev1 (EasyLog owner)**: the v1.0 `EasyLog.LogEntry` API is
> frozen, so adding `EncryptionTimeMs` requires a coordinated EasyLog v1.1 release.
> The new field must be additive (default `0`) so v1.0 consumers keep parsing the
> log files unchanged.

## Single-instance constraint

CryptoSoft v1 enforces a single running instance per machine (mutex). If a second
job tries to invoke it concurrently, the second invocation exits immediately with
a negative code.

EasySave must therefore **serialize encryption calls** within a single job. Parallel
jobs across multiple `BackupManager` instances must either coordinate or expect
encryption failures on the loser.

> **Open point**: confirm with the tutor whether the v2.0 mutex is per-machine or
> per-process. The current assumption is per-machine.

## Performance expectations

- Average throughput on a typical workstation: ~50 MB/s for files under 100 MB.
- Files larger than 100 MB should not be encrypted in v2.0 (open point with the
  tutor): the cahier targets office documents.

## Error handling

- Missing executable → `FileNotFoundException` raised by `Process.Start`. EasySave
  logs `EncryptionTimeMs = -1` and continues with the next file.
- Non-zero negative exit code → log `EncryptionTimeMs = <code>` and continue.
- Process hang → enforce the `CryptoSoftTimeoutMs` timeout, kill, log `-1`, continue.

In every failure case the **backup job itself does not stop**. The user gets a
warning message at the end summarizing the count of failed encryptions.

## Open points

- [ ] Receive the CryptoSoft binary from the tutor (estimated end of phase 1).
- [ ] Confirm exit-code convention (currently assumed `>= 0` = ms, `< 0` = error).
- [ ] Confirm whether the file extension filter is owned by EasySave (current
      assumption) or by CryptoSoft itself.
- [ ] Confirm whether large files (> 100 MB) are in scope for v2.0.

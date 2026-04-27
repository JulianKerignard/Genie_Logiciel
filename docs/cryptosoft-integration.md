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
  "CryptoSoftExtensions": ".docx,.pdf,.xlsx"
}
```

| Key | Type | Default | Meaning |
|---|---|---|---|
| `CryptoSoftPath` | string | `""` | Absolute path to the executable. Empty = encryption disabled. |
| `CryptoSoftExtensions` | string | `""` | Comma-separated list of file extensions to encrypt (lowercase, leading dot). Empty = nothing is encrypted. |

When `CryptoSoftPath` is empty, EasySave **must not fail**: the file is copied as-is
and the log entry records `EncryptionTimeMs = 0` (no encryption performed).

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

EasySave logs this value into `LogEntry.FileTransferTimeMs` (consistent with the
v1.0 convention: negative = failure).

A timeout on the EasySave side wraps the call (default: 30 seconds per file). If
the timeout fires, EasySave kills the process and logs `EncryptionTimeMs = -1`.

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
- Process hang → enforce the 30 s timeout, kill, log `-1`, continue.

In every failure case the **backup job itself does not stop**. The user gets a
warning message at the end summarizing the count of failed encryptions.

## Open points

- [ ] Receive the CryptoSoft binary from the tutor (estimated end of phase 1).
- [ ] Confirm exit-code convention (currently assumed `>= 0` = ms, `< 0` = error).
- [ ] Confirm whether the file extension filter is owned by EasySave (current
      assumption) or by CryptoSoft itself.
- [ ] Confirm whether large files (> 100 MB) are in scope for v2.0.

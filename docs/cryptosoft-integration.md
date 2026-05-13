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

Three keys in `appsettings.json` drive the integration. They live alongside the
other v2.0 settings parsed by `AppSettings`:

```json
{
  "language": "en",
  "encrypted_extensions": [".pdf", ".docx", ".xlsx"],
  "business_software": ["calc.exe", "notepad.exe"],
  "log_format": "json",
  "crypto_soft": {
    "path": "C:\\Program Files\\ProSoft\\CryptoSoft\\CryptoSoft.exe",
    "timeout_ms": 30000
  }
}
```

| JSON key | C# property | Type | Default | Meaning |
|---|---|---|---|---|
| `crypto_soft.path` | `AppSettings.CryptoSoft.Path` | string | `""` | Absolute path to the executable. Empty = encryption disabled. |
| `crypto_soft.timeout_ms` | `AppSettings.CryptoSoft.TimeoutMs` | int | `30000` | Per-file timeout for the CryptoSoft child process, in milliseconds. |
| `encrypted_extensions` | `AppSettings.EncryptedExtensions` | string array | `[]` | File extensions (lowercase, leading dot) that must be encrypted. Empty = nothing is encrypted. |

When `crypto_soft.path` is empty, EasySave **must not fail**: the file is copied as-is
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
`crypto_soft.timeout_ms` in `appsettings.json` (default: 30000 ms). If the timeout
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

## Single-instance constraint (v3+)

The CdC v3 mandates that CryptoSoft be **Mono-Instance** — no two CryptoSoft
processes may run simultaneously on the same workstation. The enforcement lives
on **two layers** that intentionally use **different mutex names** so they can
fire independently without dead-locking each other:

| Layer | Mutex name | Role |
|---|---|---|
| CryptoSoft binary (`CryptoSoft/SystemMutexGate.cs`) | `Global\ProSoft.CryptoSoft.SingleInstance` | Cahier-aligned cross-process gate. A second CryptoSoft launch (from any EasySave instance or a manual command line) exits immediately with code `-2` (`AlreadyRunning`). |
| EasySave adapter (`CryptoSoftAdapter`) | `Global\EasySave.CryptoSoftSpawnGate` | Defensive in-process serialization. Prevents two parallel jobs in the same EasySave process from spawning CryptoSoft at the same time — even though CryptoSoft's own gate would refuse the second spawn, the adapter still serializes so the second job's file isn't dropped. |

On Windows the `Global\` prefix scopes both mutexes across user sessions; on
Linux / macOS .NET maps the named mutex to a per-runtime POSIX semaphore, which
still gives cross-process isolation within the ProSoft installation.

If the adapter's gate is contended for longer than `2 × crypto_soft.timeout_ms`
(60 s with the default 30 s budget), the queued caller bails out with
`EncryptResult.Failed()` and writes a `Trace.TraceWarning` line. Operators see
the conflict in the host trace log and can either raise `crypto_soft.timeout_ms`
or investigate why an earlier invocation is stuck.

### Robustness

- `AbandonedMutexException`: if a previous holder process died without releasing
  the mutex, the OS grants the gate to the next waiter. The adapter treats this
  as a normal acquisition — CryptoSoft itself is responsible for cleaning up any
  partial output on the next launch.
- `CryptoSoftAdapter` implements `IDisposable` so the host can release the gate
  handle deterministically on shutdown.

> **Note on the legacy CryptoSoft v1 behaviour**: pre-v3 docs claimed CryptoSoft
> itself owned a mutex and rejected the second invocation with a negative exit
> code. EasySave's caller-side mutex is the canonical enforcement point in v3.
> Even if a future CryptoSoft binary adds its own internal mutex, the caller-side
> gate guarantees the constraint regardless of the binary's behaviour.

## Performance expectations

- Average throughput on a typical workstation: ~50 MB/s for files under 100 MB.
- Files larger than 100 MB should not be encrypted in v2.0 (open point with the
  tutor): the cahier targets office documents.

## Error handling

- Missing executable → `FileNotFoundException` raised by `Process.Start`. EasySave
  logs `EncryptionTimeMs = -1` and continues with the next file.
- Non-zero negative exit code → log `EncryptionTimeMs = <code>` and continue.
- Process hang → enforce the `crypto_soft.timeout_ms` timeout, kill, log `-1`, continue.

In every failure case the **backup job itself does not stop**. The user gets a
warning message at the end summarizing the count of failed encryptions.

## Open points

- [ ] Receive the CryptoSoft binary from the tutor (estimated end of phase 1).
- [ ] Confirm exit-code convention (currently assumed `>= 0` = ms, `< 0` = error).
- [ ] Confirm whether the file extension filter is owned by EasySave (current
      assumption) or by CryptoSoft itself.
- [ ] Confirm whether large files (> 100 MB) are in scope for v2.0.

# EasyLog

**EasyLog** is a reusable .NET 8 class library that writes daily JSON log files.
It is the standard logger for ProSoft applications (EasySave v1.0 and later) and
is distributed as `EasyLog.dll`.

## Features

- **Daily rotation**: one JSON file per calendar day (`yyyy-MM-dd.json`).
- **Thread-safe writes**: serialized via an internal lock; safe to share across
  concurrent backup jobs.
- **Atomic on disk**: every append writes to a temporary file first, then
  replaces the day file via `File.Move`. No partial writes on crash.
- **UNC path normalization**: source and target paths are written in UNC form
  (`\\server\share\file`); local Windows drives are wrapped with the
  extended-length prefix (`\\?\C:\...`).
- **Corrupted file recovery**: an unreadable day file is moved aside to
  `*.corrupted-<timestamp>-<guid>` instead of being silently overwritten.
- **Error signal**: `FileTransferTimeMs < 0` indicates a failed copy, as
  required by the ProSoft specification.

## Install

Reference the DLL from any .NET 8 project:

```xml
<ItemGroup>
  <Reference Include="EasyLog">
    <HintPath>path\to\EasyLog.dll</HintPath>
  </Reference>
</ItemGroup>
```

Or add a project reference if building from source:

```xml
<ProjectReference Include="..\EasyLog\EasyLog.csproj" />
```

The accompanying `EasyLog.xml` file ships next to the DLL and provides IntelliSense
documentation in Visual Studio, Rider, and VS Code.

## Usage

### Basic append

```csharp
using EasyLog;

IDailyLogger logger = new JsonDailyLogger(@"C:\ProgramData\ProSoft\logs");

logger.Append(new LogEntry
{
    Timestamp = DateTimeOffset.Now.ToString("o"),
    JobName = "daily-backup",
    SourceFile = @"C:\data\report.pdf",
    TargetFile = @"\\nas01\backups\report.pdf",
    FileSize = 1_248_576,
    FileTransferTimeMs = 42,
});
```

### Signalling a failed copy

A negative `FileTransferTimeMs` is how EasyLog records a transfer failure:

```csharp
try
{
    File.Copy(source, target, overwrite: true);
    stopwatch.Stop();
    transferMs = stopwatch.ElapsedMilliseconds;
}
catch (IOException)
{
    transferMs = -1; // Error signal
}

logger.Append(new LogEntry
{
    Timestamp = DateTimeOffset.Now.ToString("o"),
    JobName = job.Name,
    SourceFile = source,
    TargetFile = target,
    FileSize = new FileInfo(source).Length,
    FileTransferTimeMs = transferMs,
});
```

### Sample output

`logs/2026-04-21.json`:

```json
[
  {
    "Timestamp": "2026-04-21T09:10:03.4161680+02:00",
    "JobName": "daily-backup",
    "SourceFile": "\\\\?\\C:\\data\\report.pdf",
    "TargetFile": "\\\\nas01\\backups\\report.pdf",
    "FileSize": 1248576,
    "FileTransferTimeMs": 42
  }
]
```

## Public API

Three types are exported from the `EasyLog` namespace:

### `IDailyLogger`

The contract. Consumers should depend on this interface, not on
`JsonDailyLogger`.

```csharp
public interface IDailyLogger
{
    void Append(LogEntry entry);
}
```

### `LogEntry`

The POCO that describes a single logged operation. Fields:

| Property | Type | Notes |
| --- | --- | --- |
| `Timestamp` | `string` | ISO-8601, caller-provided |
| `JobName` | `string` | Backup job that produced the entry |
| `SourceFile` | `string` | Full source path (UNC recommended) |
| `TargetFile` | `string` | Full target path (UNC recommended) |
| `FileSize` | `long` | Source size in bytes |
| `FileTransferTimeMs` | `long` | Transfer duration; `< 0` on error |

### `JsonDailyLogger`

The shipped implementation of `IDailyLogger`.

- `JsonDailyLogger(string logDirectory)` — creates the directory if missing.
- `LogDirectory { get; }` — absolute path where day files are written.
- `Append(LogEntry entry)` — adds one entry to the current day file.

## Versioning policy

EasyLog follows **[Semantic Versioning 2.0.0](https://semver.org/)**.

| Change | Version bump | Example |
| --- | --- | --- |
| New property on `LogEntry` (with default), new method on a class | Minor | 1.0.0 → 1.1.0 |
| Bug fix, internal refactor, new private helper | Patch | 1.0.0 → 1.0.1 |
| Removing/renaming a member of `IDailyLogger` or `LogEntry`, changing a signature, or changing the serialized JSON shape | **Major** | 1.x → 2.0.0 |

### v1.0.0 contract (frozen)

The public surface listed in **Public API** above is **frozen for the entire
v1.x line**. The following guarantees hold across every v1.x release:

- `IDailyLogger.Append(LogEntry)` keeps the same signature and semantics.
- Every `LogEntry` property keeps the same name, type, and meaning.
- The on-disk format stays a JSON array of `LogEntry` objects in
  `{log-dir}/yyyy-MM-dd.json`.
- `FileTransferTimeMs < 0` remains the single error signal.

**EasySave v1.0, v2.0, and v3.0 all consume the same `EasyLog.dll` without
recompilation.** A breaking change would require a new major version
(`EasyLog 2.0.0`) and an explicit migration note.

### Minor additions that are allowed

- New optional properties on `LogEntry` — existing consumers ignore them.
- New methods on `JsonDailyLogger` that do not appear on `IDailyLogger`.
- New interfaces (e.g. `IDailyLoggerV2`) living next to the existing one.

### Deprecation process

If a member must eventually be removed, it is first marked with
`[Obsolete]` in a minor release, kept working for at least one additional
minor, then removed only in the next major bump.

## Thread safety

Every public operation on `JsonDailyLogger` is safe under concurrent access
from multiple threads. Internally a single `object` lock serializes all writes
to the day file. Callers do not need to synchronize themselves.

## Testing

The project ships with integration tests under
`tests/EasySave.Tests/JsonDailyLoggerTests.cs`. They exercise:

- Basic append and JSON indentation
- Multi-threaded append (no entry lost)
- UNC path preservation
- Local path normalization (Windows / Unix branches)
- Corrupted file quarantine
- Tmp file cleanup on error
- Negative transfer time preservation

Run with:

```bash
dotnet test EasySave.sln
```

## License & ownership

Internal ProSoft library. Not published to NuGet. Distributed with each
EasySave release on GitHub.

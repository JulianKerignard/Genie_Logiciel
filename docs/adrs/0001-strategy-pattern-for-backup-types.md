# ADR 0001 — Use the Strategy pattern for backup types

- **Status:** Accepted
- **Date:** 2026-04-21
- **Decision drivers:** Cahier v1.0, team conventions, testability

## Context

The v1.0 cahier requires EasySave to support two backup modes:

- **Full** — copy every file in the source tree to the target.
- **Differential** — copy only files modified since the last full backup.

v3.0 is expected to introduce at least one additional mode (the spec is not
final yet, but the team has been warned the list will grow).

The execution loop in `BackupManager` is identical regardless of mode: enumerate
the source directory, filter eligible files, copy, log, update state. The only
variation is the *decision* of whether a given file must be copied.

## Options considered

### Option A — `if/else` on `BackupType` inside `BackupManager`

```csharp
foreach (var file in files)
{
    bool copy = job.Type switch
    {
        BackupType.Full => true,
        BackupType.Differential => IsNewerThanTarget(file, targetPath),
        _ => throw new NotSupportedException()
    };
    if (copy) Copy(file, targetPath);
}
```

- **Pro:** simple, no extra type.
- **Con:** every new mode adds a branch. `BackupManager` knows the details of
  every strategy. Violates the Open/Closed principle. Harder to unit-test the
  decision logic in isolation.

### Option B — Strategy pattern behind `IBackupStrategy`

```csharp
public interface IBackupStrategy
{
    bool ShouldCopy(FileInfo source, string targetPath);
}
```

- `FullBackupStrategy.ShouldCopy` returns `true`.
- `DifferentialBackupStrategy.ShouldCopy` compares size and last-write-time.
- `BackupManager` picks the strategy from `job.Type` once and delegates.

- **Pro:** each strategy is a self-contained unit with its own tests (see
  `FullBackupStrategyTests`, `DifferentialBackupStrategyTests`). Adding a
  v3.0 mode is a new class, no change in `BackupManager`.
- **Con:** two extra types for a v1.0 with only two modes.

### Option C — Inheritance (abstract `BackupJobExecutor` base class)

Same extensibility as Strategy but couples execution and decision in one
hierarchy. Fits less well with the existing layered architecture where
`Models` are POCOs and `Services` hold behaviour.

## Decision

**Adopt Option B (Strategy).**

Implemented as:

```
src/EasySave/Services/
├── IBackupStrategy.cs
├── FullBackupStrategy.cs
└── DifferentialBackupStrategy.cs
```

`BackupManager` receives both strategies via constructor injection and
selects at runtime based on `job.Type`:

```csharp
var strategy = job.Type == BackupType.Full ? _fullStrategy : _diffStrategy;
```

## Consequences

### Positive

- New backup modes (v3.0) are added without touching `BackupManager` —
  one new class implementing `IBackupStrategy`, one new enum value,
  one wiring change in `Program.Main`.
- Strategy-specific tests stay small and focused (currently 7 tests split
  across the two strategy files).
- Clear separation between *orchestration* (BackupManager) and *policy*
  (IBackupStrategy).

### Negative

- Two extra types for two modes in v1.0 — overhead that pays off only at v3.0.
- Constructor arguments on `BackupManager` grow each time a new mode lands
  (acceptable: DI is manual in `Program.Main`).

### Neutral

- The `BackupType` enum is kept as the persisted identifier in `jobs.json`.
  Strategies are not serialized — they are reconstructed at startup.

## References

- Cahier v1.0 §2 (backup modes)
- `src/EasySave/Services/IBackupStrategy.cs`
- Gamma et al., *Design Patterns*, Strategy — pp. 315–323

namespace EasySave.Shared;

// Snapshot of a single job's progress, serialized to JSON and pushed by the
// engine to the remote console. Mirrors the persisted EasySave.StateEntry so a
// client can render the full status (file counts, bytes, current file) without
// touching state.json directly. Sizes are bytes; Percent is 0..100 derived
// from file counters.
public sealed record JobProgressDto(
    string JobName,
    JobStateEnum State,
    string CurrentFile,
    int FilesRemaining,
    int TotalFilesEligible,
    long SizeRemaining,
    long TotalSize,
    double Percent
);

namespace EasySave.Shared;

// Snapshot of a single job's progress, serialized to JSON and pushed by the
// engine to the remote console. Mirrors the live EasySave.Models.JobProgress
// plus the persisted JobState so a client can render the full status without
// touching state.json directly.
public sealed record JobProgressDto(
    string JobName,
    JobStateEnum State,
    string CurrentFile,
    int FilesRemaining,
    int TotalFilesEligible,
    double Percent
);

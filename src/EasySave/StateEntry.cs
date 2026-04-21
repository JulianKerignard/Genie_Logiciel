namespace EasySave;

// Snapshot of a backup job at a given point in time.
// Serialized into the shared state.json file consumed by monitoring tools.
public sealed class StateEntry
{
    // Display name of the backup job.
    public string Name { get; set; } = string.Empty;

    // Timestamp of the last recorded action on this job.
    // DateTimeOffset round-trips to ISO-8601 via System.Text.Json.
    public DateTimeOffset LastActionTime { get; set; }

    // Whether the job is currently running.
    public JobState State { get; set; }

    // Total number of files eligible for the backup (meaningful when State is Active).
    public int TotalFilesEligible { get; set; }

    // Total size in bytes to transfer.
    public long TotalSize { get; set; }

    // Number of files still to be transferred.
    public int FilesRemaining { get; set; }

    // Remaining size in bytes to transfer.
    public long SizeRemaining { get; set; }

    // Progress between 0 and 100, derived from file counters to stay consistent.
    public double Progress =>
        TotalFilesEligible == 0
            ? 0.0
            : Math.Round((TotalFilesEligible - FilesRemaining) * 100.0 / TotalFilesEligible, 1);

    // Full source path of the file currently being processed (UNC format).
    public string CurrentSource { get; set; } = string.Empty;

    // Full target path of the file currently being processed (UNC format).
    public string CurrentTarget { get; set; } = string.Empty;

    public override string ToString()
    {
        return $"{Name} [{State}] {Progress:0.0}% - {FilesRemaining} files / {SizeRemaining} bytes remaining";
    }
}

namespace EasySave;

// Snapshot of a backup job at a given point in time.
// Serialized into the shared state.json file consumed by monitoring tools.
public sealed class StateEntry
{
    // Display name of the backup job.
    public string Name { get; set; } = string.Empty;

    // ISO-8601 timestamp of the last recorded action on this job.
    public string LastActionTime { get; set; } = string.Empty;

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

    // Progress between 0 and 100.
    public double Progress { get; set; }

    // Full source path of the file currently being processed (UNC format).
    public string CurrentSource { get; set; } = string.Empty;

    // Full target path of the file currently being processed (UNC format).
    public string CurrentTarget { get; set; } = string.Empty;

    public override string ToString()
    {
        return $"{Name} [{State}] {Progress:0.0}% - {FilesRemaining} files / {SizeRemaining} bytes remaining";
    }
}

namespace EasySave;

/// <summary>
/// Snapshot of a backup job at a given point in time.
/// Serialized into the shared state.json file consumed by monitoring tools.
/// </summary>
public sealed class StateEntry
{
    /// <summary>
    /// Display name of the backup job.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// ISO-8601 timestamp of the last recorded action on this job.
    /// </summary>
    public string LastActionTime { get; set; } = string.Empty;

    /// <summary>
    /// Whether the job is currently running.
    /// </summary>
    public JobState State { get; set; }

    /// <summary>
    /// Total number of files eligible for the backup (meaningful when State is Active).
    /// </summary>
    public int TotalFilesEligible { get; set; }

    /// <summary>
    /// Total size in bytes to transfer.
    /// </summary>
    public long TotalSize { get; set; }

    /// <summary>
    /// Number of files still to be transferred.
    /// </summary>
    public int FilesRemaining { get; set; }

    /// <summary>
    /// Remaining size in bytes to transfer.
    /// </summary>
    public long SizeRemaining { get; set; }

    /// <summary>
    /// Progress between 0 and 100.
    /// </summary>
    public double Progress { get; set; }

    /// <summary>
    /// Full source path of the file currently being processed (UNC format).
    /// </summary>
    public string CurrentSource { get; set; } = string.Empty;

    /// <summary>
    /// Full target path of the file currently being processed (UNC format).
    /// </summary>
    public string CurrentTarget { get; set; } = string.Empty;

    public override string ToString()
    {
        return $"{Name} [{State}] {Progress:0.0}% - {FilesRemaining} files / {SizeRemaining} bytes remaining";
    }
}

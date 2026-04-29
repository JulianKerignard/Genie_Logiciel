using EasySave;

namespace EasySave.UI.Models;

/// <summary>
/// Represents a single completed backup run that can be restored.
/// Produced by <see cref="Services.IRestoreService.GetRestorePoints"/>.
/// </summary>
public sealed class RestorePoint
{
    public string JobName { get; set; } = string.Empty;

    /// <summary>ISO-8601 timestamp of the backup run that created this restore point.</summary>
    public DateTimeOffset Timestamp { get; set; }

    public BackupType BackupType { get; set; }

    /// <summary>Total size of the backup snapshot in bytes.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Root directory that holds the backup snapshot.</summary>
    public string SnapshotPath { get; set; } = string.Empty;

    /// <summary>Human-readable backup type label.</summary>
    public string BackupTypeName => BackupType == BackupType.Full ? "Full" : "Differential";

    /// <summary>Size formatted as mebibytes.</summary>
    public string SizeMb => $"{SizeBytes / 1_048_576.0:0.0} MB";
}

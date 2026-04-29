namespace EasySave.UI.Models;

/// <summary>
/// Associates a backup job with a scheduling configuration.
/// Persisted by <see cref="Services.ISchedulerService"/>.
/// </summary>
public sealed class ScheduledJob
{
    public string JobName { get; set; } = string.Empty;

    /// <summary>Whether scheduling is active for this job.</summary>
    public bool IsEnabled { get; set; }

    /// <summary>Interval between automatic runs, in minutes.</summary>
    public int IntervalMinutes { get; set; } = 60;

    /// <summary>Timestamp of the last automatic run. Null if never run.</summary>
    public DateTimeOffset? LastRunTime { get; set; }
}

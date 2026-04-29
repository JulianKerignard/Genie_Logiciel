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

    /// <summary>Computed next run time based on LastRunTime + IntervalMinutes.</summary>
    public DateTimeOffset? NextRunTime =>
        IsEnabled && LastRunTime.HasValue
            ? LastRunTime.Value.AddMinutes(IntervalMinutes)
            : (DateTimeOffset?)null;

    public string NextRunDisplay => NextRunTime.HasValue
        ? NextRunTime.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
        : "—";
}

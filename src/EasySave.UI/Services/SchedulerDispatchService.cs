using EasySave.UI.Models;

namespace EasySave.UI.Services;

/// <summary>
/// Background service that fires due backup jobs on schedule.
/// Polls enabled schedules every minute; updates <c>LastRunTime</c> after each
/// successful dispatch so the next interval is computed correctly.
/// </summary>
public sealed class SchedulerDispatchService : IDisposable
{
    private readonly ISchedulerService _scheduler;
    private readonly IBackupManagerAdapter _backup;
    private System.Threading.Timer? _timer;
    private bool _disposed;

    public SchedulerDispatchService(ISchedulerService scheduler, IBackupManagerAdapter backup)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(backup);
        _scheduler = scheduler;
        _backup = backup;
    }

    /// <summary>
    /// Starts the one-minute polling loop.
    /// Safe to call multiple times (subsequent calls are no-ops).
    /// </summary>
    public void Start()
    {
        if (_timer is not null) return;
        _timer = new System.Threading.Timer(
            Tick, null,
            dueTime: TimeSpan.FromMinutes(1),
            period: TimeSpan.FromMinutes(1));
    }

    private void Tick(object? _)
    {
        var now = DateTimeOffset.Now;

        List<ScheduledJob> schedules;
        try
        {
            schedules = _scheduler.GetAll().ToList();
        }
        catch (IOException)
        {
            // Transient lock on schedules.json — skip this minute, the next tick will retry.
            return;
        }

        bool anyDispatched = false;

        foreach (var schedule in schedules.Where(s => s.IsEnabled))
        {
            bool isDue = schedule.LastRunTime.HasValue
                ? schedule.LastRunTime.Value.AddMinutes(schedule.IntervalMinutes) <= now
                : true; // never run — fire on first tick

            if (!isDue) continue;
            if (_backup.IsJobRunning(schedule.JobName)) continue;

            _ = _backup.RunJobAsync(schedule.JobName);
            schedule.LastRunTime = now;
            anyDispatched = true;
        }

        if (anyDispatched)
            _scheduler.SaveAll(schedules);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer?.Dispose();
    }
}

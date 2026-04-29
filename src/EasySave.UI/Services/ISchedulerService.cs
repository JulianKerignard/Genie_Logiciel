using EasySave.UI.Models;

namespace EasySave.UI.Services;

/// <summary>
/// Manages automatic backup schedules for configured jobs.
/// </summary>
public interface ISchedulerService
{
    /// <summary>Returns all persisted scheduled jobs.</summary>
    IReadOnlyList<ScheduledJob> GetAll();

    /// <summary>
    /// Persists the given schedule list, replacing any previous configuration.
    /// </summary>
    void SaveAll(IEnumerable<ScheduledJob> schedules);
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasySave.UI.Models;
using EasySave.UI.Services;

namespace EasySave.UI.ViewModels;

public sealed partial class ScheduleViewModel : ViewModelBase
{
    private readonly IBackupManagerAdapter _backup;
    private readonly ISchedulerService _scheduler;

    public ObservableCollection<ScheduledJobVM> ScheduledJobs { get; } = new();

    [ObservableProperty] private string _saveConfirmation = string.Empty;

    // Set to true when LoadSchedules() failed because of a transient IOException.
    // Save() short-circuits while this flag is set so the user cannot wipe schedules.json
    // by clicking Save on an empty grid that is empty only because the load failed.
    private bool _persistenceFailed;

    public ScheduleViewModel(IBackupManagerAdapter backup, ISchedulerService scheduler)
    {
        _backup = backup;
        _scheduler = scheduler;
        LoadSchedules();
    }

    private void LoadSchedules()
    {
        Dictionary<string, ScheduledJob> saved;
        try
        {
            saved = _scheduler.GetAll().ToDictionary(s => s.JobName, StringComparer.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            // Transient lock on schedules.json: leave the grid empty AND mark persistence
            // as failed so Save cannot overwrite the file with the empty list.
            _persistenceFailed = true;
            SaveConfirmation = TranslationSource.Instance["error.persistence_unavailable"];
            return;
        }

        foreach (var job in _backup.GetJobs())
        {
            var schedule = saved.TryGetValue(job.Name, out var s)
                ? s
                : new ScheduledJob { JobName = job.Name };
            ScheduledJobs.Add(new ScheduledJobVM(schedule));
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (_persistenceFailed)
        {
            SaveConfirmation = TranslationSource.Instance["error.persistence_unavailable"];
            return;
        }

        _scheduler.SaveAll(ScheduledJobs.Select(vm => vm.ToModel()));
        SaveConfirmation = TranslationSource.Instance["schedule.saved"];
    }
}

/// <summary>Observable wrapper around <see cref="ScheduledJob"/> for the schedule grid.</summary>
public sealed partial class ScheduledJobVM : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NextRunDisplay))]
    private bool _isEnabled;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NextRunDisplay))]
    private int _intervalMinutes;

    public string JobName { get; }
    public DateTimeOffset? LastRunTime { get; }

    public string NextRunDisplay =>
        IsEnabled && LastRunTime.HasValue
            ? LastRunTime.Value.AddMinutes(IntervalMinutes).ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : "—";

    public ScheduledJobVM(ScheduledJob model)
    {
        JobName = model.JobName;
        _isEnabled = model.IsEnabled;
        _intervalMinutes = model.IntervalMinutes;
        LastRunTime = model.LastRunTime;
    }

    public ScheduledJob ToModel() => new()
    {
        JobName = JobName,
        IsEnabled = IsEnabled,
        IntervalMinutes = IntervalMinutes,
        LastRunTime = LastRunTime,
    };
}

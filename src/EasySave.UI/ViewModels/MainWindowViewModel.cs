using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasySave.Models;
using EasySave.UI.Services;

namespace EasySave.UI.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly IBackupManagerAdapter _backup;
    private readonly BusinessWatcherService _watcher;
    private readonly IRestoreService _restoreService;
    private readonly ISchedulerService _scheduler;

    private JobsViewModel? _jobsVm;
    private RunProgressViewModel? _progressVm;

    [ObservableProperty]
    private ViewModelBase _currentView = null!;

    /// <summary>
    /// Replaces <see cref="CurrentView"/> and disposes the previous one when it
    /// owns native resources (i18n event subscriptions on <c>JobEditViewModel</c>,
    /// timers on <c>RunProgressViewModel</c>, etc.). The two cached singletons
    /// (<see cref="_jobsVm"/>, <see cref="_progressVm"/>) are skipped because
    /// navigation cycles through them repeatedly — disposing them would
    /// cripple the next navigation.
    /// </summary>
    private void SetCurrentView(ViewModelBase next)
    {
        if (CurrentView is IDisposable disposable
            && !ReferenceEquals(CurrentView, _jobsVm)
            && !ReferenceEquals(CurrentView, _progressVm))
        {
            disposable.Dispose();
        }
        CurrentView = next;
    }

    public MainWindowViewModel(
        IBackupManagerAdapter backup,
        BusinessWatcherService watcher,
        IRestoreService restoreService,
        ISchedulerService scheduler)
    {
        _backup = backup;
        _watcher = watcher;
        _restoreService = restoreService;
        _scheduler = scheduler;
        NavigateToJobs();
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    [RelayCommand]
    private void NavigateToJobs()
    {
        SetCurrentView(GetOrCreateJobsVm());
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        SetCurrentView(new SettingsViewModel());
    }

    [RelayCommand]
    private void NavigateToLogs()
    {
        SetCurrentView(new LogsViewModel());
    }

    [RelayCommand]
    private void NavigateToRestore()
    {
        SetCurrentView(new RestoreViewModel(_backup, _restoreService));
    }

    [RelayCommand]
    private void NavigateToSchedule()
    {
        SetCurrentView(new ScheduleViewModel(_backup, _scheduler));
    }

    // ── Sub-navigation ────────────────────────────────────────────────────────

    private void ShowJobEdit(BackupJob? job)
    {
        var jobsVm = GetOrCreateJobsVm();
        SetCurrentView(new JobEditViewModel(
            job,
            onDone: NavigateToJobs,
            backup: _backup,
            onSaved: jobsVm.OnJobSaved));
    }

    private void ShowRunProgress()
    {
        var jobsVm = GetOrCreateJobsVm();
        if (_progressVm is null)
        {
            _progressVm = new RunProgressViewModel(jobsVm);
            _progressVm.CloseRequested = () => SetCurrentView(jobsVm);
        }
        SetCurrentView(_progressVm);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private JobsViewModel GetOrCreateJobsVm()
    {
        if (_jobsVm is not null) return _jobsVm;
        _jobsVm = new JobsViewModel(_backup, _watcher);
        _jobsVm.RequestOpenJobEdit = job => ShowJobEdit(job);
        _jobsVm.RequestShowProgress = () => ShowRunProgress();
        return _jobsVm;
    }
}

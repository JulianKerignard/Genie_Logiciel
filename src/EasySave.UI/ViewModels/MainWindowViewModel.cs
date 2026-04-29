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
        CurrentView = GetOrCreateJobsVm();
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        CurrentView = new SettingsViewModel();
    }

    [RelayCommand]
    private void NavigateToLogs()
    {
        CurrentView = new LogsViewModel();
    }

    [RelayCommand]
    private void NavigateToRestore()
    {
        CurrentView = new RestoreViewModel(_backup, _restoreService);
    }

    [RelayCommand]
    private void NavigateToSchedule()
    {
        CurrentView = new ScheduleViewModel(_backup, _scheduler);
    }

    // ── Sub-navigation ────────────────────────────────────────────────────────

    private void ShowJobEdit(BackupJob? job)
    {
        var jobsVm = GetOrCreateJobsVm();
        CurrentView = new JobEditViewModel(
            job,
            onDone: NavigateToJobs,
            backup: _backup,
            onSaved: jobsVm.OnJobSaved);
    }

    private void ShowRunProgress()
    {
        var jobsVm = GetOrCreateJobsVm();
        if (_progressVm is null)
        {
            _progressVm = new RunProgressViewModel(jobsVm);
            _progressVm.CloseRequested = () => CurrentView = jobsVm;
        }
        CurrentView = _progressVm;
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

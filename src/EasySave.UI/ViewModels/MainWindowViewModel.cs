using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasySave.Models;
using EasySave.UI.Services;

namespace EasySave.UI.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly IBackupManagerAdapter _backup;
    private readonly BusinessWatcherService _watcher;

    private JobsViewModel? _jobsVm;
    private RunProgressViewModel? _progressVm;

    [ObservableProperty]
    private ViewModelBase _currentView = null!;

    public MainWindowViewModel(IBackupManagerAdapter backup, BusinessWatcherService watcher)
    {
        _backup = backup;
        _watcher = watcher;
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

    // ── Sub-navigation ────────────────────────────────────────────────────────

    private void ShowJobEdit(BackupJob? job)
    {
        CurrentView = new JobEditViewModel(job, onDone: NavigateToJobs);
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
        _jobsVm.RequestOpenJobEdit  = job => ShowJobEdit(job);
        _jobsVm.RequestShowProgress = () => ShowRunProgress();
        return _jobsVm;
    }
}

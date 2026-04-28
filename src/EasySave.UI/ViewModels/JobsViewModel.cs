using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasySave;
using EasySave.Models;
using EasySave.UI.Services;

namespace EasySave.UI.ViewModels;

public sealed partial class JobsViewModel : ViewModelBase
{
    private readonly IBackupManagerAdapter _backup;
    private readonly BusinessWatcherService _watcher;
    // Tracks jobs paused by the watcher (distinct from user-initiated pauses).
    private readonly HashSet<string> _watcherPausedJobs = new();

    // Set by MainWindowViewModel after construction.
    public Action<BackupJob?>? RequestOpenJobEdit { get; set; }
    public Action? RequestShowProgress { get; set; }

    public ObservableCollection<BackupJobVM> Jobs { get; } = new();

    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isBusinessSoftwareDetected;
    [ObservableProperty] private string _detectedSoftwareName = string.Empty;

    public bool IsEmpty => Jobs.Count == 0;

    public JobsViewModel(IBackupManagerAdapter backup, BusinessWatcherService watcher)
    {
        _backup = backup;
        _watcher = watcher;
        Jobs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
        LoadJobs();
        _backup.StateUpdated += OnStateUpdated;
        _watcher.BusinessSoftwareDetected += OnBusinessSoftwareDetected;
        _watcher.BusinessSoftwareGone     += OnBusinessSoftwareGone;
        _watcher.Start();
    }

    private void LoadJobs()
    {
        foreach (var job in _backup.GetJobs())
            Jobs.Add(new BackupJobVM(job));
    }

    // ── State polling callbacks ───────────────────────────────────────────────

    private void OnStateUpdated(object? sender, StateEntry entry)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var vm = Jobs.FirstOrDefault(j => j.Name == entry.Name);
            if (vm is null) return;
            vm.Progress = (int)entry.Progress;
            vm.CurrentFile = entry.CurrentSource;
            vm.FilesRemaining = entry.FilesRemaining;
            if (entry.State == JobState.Active)
            {
                // Only promote Idle → Running; don't touch a user-paused job.
                if (vm.UiState == UiJobState.Idle) vm.UiState = UiJobState.Running;
            }
            else
            {
                // Backend finished (Inactive): always reset to Idle so a job that
                // ran to completion while "paused" doesn't stay stuck on Paused.
                vm.UiState = UiJobState.Idle;
                _watcherPausedJobs.Remove(vm.Name);
            }
        });
    }

    // ── Business software watcher ─────────────────────────────────────────────

    private void OnBusinessSoftwareDetected(object? sender, string name)
    {
        IsBusinessSoftwareDetected = true;
        DetectedSoftwareName = name;
        foreach (var job in Jobs.Where(j => j.UiState == UiJobState.Running).ToList())
        {
            _watcherPausedJobs.Add(job.Name);
            job.UiState = UiJobState.Paused;
            _backup.PauseJob(job.Name);
        }
    }

    private void OnBusinessSoftwareGone(object? sender, EventArgs _)
    {
        IsBusinessSoftwareDetected = false;
        DetectedSoftwareName = string.Empty;
        // Only resume jobs that the watcher itself paused; leave user-paused jobs alone.
        foreach (var job in Jobs.Where(j => j.UiState == UiJobState.Paused
                                         && _watcherPausedJobs.Contains(j.Name)).ToList())
        {
            _watcherPausedJobs.Remove(job.Name);
            job.UiState = UiJobState.Running;
            _backup.ResumeJob(job.Name);
        }
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void AddJob() => RequestOpenJobEdit?.Invoke(null);

    [RelayCommand]
    private void EditJob(BackupJobVM vm) => RequestOpenJobEdit?.Invoke(vm.Model);

    [RelayCommand]
    private void DeleteJob(BackupJobVM vm)
    {
        // TODO: add confirmation dialog (MsBox.Avalonia) once package is added
        try
        {
            _backup.RemoveJob(vm.Model.Name);
            Jobs.Remove(vm);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task RunJobAsync(BackupJobVM vm)
    {
        if (IsBusinessSoftwareDetected || vm.UiState == UiJobState.Running) return;
        vm.UiState = UiJobState.Running;
        RequestShowProgress?.Invoke();
        try
        {
            await _backup.RunJobAsync(vm.Name).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            if (vm.UiState != UiJobState.Paused)
            {
                vm.UiState = UiJobState.Idle;
                vm.Progress = 0;
                vm.CurrentFile = string.Empty;
                vm.FilesRemaining = 0;
            }
        }
    }

    [RelayCommand]
    private async Task RunAllAsync()
    {
        if (IsBusinessSoftwareDetected) return;
        RequestShowProgress?.Invoke();
        var tasks = Jobs
            .Where(j => j.UiState == UiJobState.Idle)
            .Select(RunJobInternal)
            .ToList();
        await Task.WhenAll(tasks).ConfigureAwait(true);
    }

    [RelayCommand]
    private void PauseJob(BackupJobVM vm)
    {
        vm.UiState = UiJobState.Paused;
        _backup.PauseJob(vm.Name);
    }

    [RelayCommand]
    private void ResumeJob(BackupJobVM vm)
    {
        if (IsBusinessSoftwareDetected) return;
        vm.UiState = UiJobState.Running;
        _backup.ResumeJob(vm.Name);
    }

    // Used by RunAllAsync to run a job without navigating (navigation is done once before the loop).
    private async Task RunJobInternal(BackupJobVM vm)
    {
        vm.UiState = UiJobState.Running;
        try
        {
            await _backup.RunJobAsync(vm.Name).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            if (vm.UiState != UiJobState.Paused)
            {
                vm.UiState = UiJobState.Idle;
                vm.Progress = 0;
                vm.CurrentFile = string.Empty;
                vm.FilesRemaining = 0;
            }
        }
    }
}

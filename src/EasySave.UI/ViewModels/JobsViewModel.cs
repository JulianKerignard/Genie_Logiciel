using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasySave;
using EasySave.Models;
using EasySave.Services;
using EasySave.UI.Services;

namespace EasySave.UI.ViewModels;

public sealed partial class JobsViewModel : ViewModelBase
{
    private readonly IBackupManagerAdapter _backup;
    private readonly IBusinessSoftwareSignals _watcher;
    // V3 parallel orchestrator. Used by RunAllAsync to launch jobs in
    // parallel bounded by max_parallel_jobs. Single-job Run/Pause/Resume
    // still goes through _backup (the adapter) — Pause/Resume routes to
    // both so a job started by RunAll can still be paused via its card.
    private readonly IParallelBackupOrchestrator? _orchestrator;
    // Tracks jobs paused by the watcher (distinct from user-initiated pauses).
    private readonly HashSet<string> _watcherPausedJobs = new();
    // Names currently executing inside the orchestrator (added at RunAllAsync start,
    // removed in its finally). Used to route Pause/Resume to the orchestrator's
    // PauseGate instead of the adapter, which doesn't know about Run-All jobs.
    private readonly HashSet<string> _orchestratorTrackedJobs = new();

    // Set by MainWindowViewModel after construction.
    public Action<BackupJob?>? RequestOpenJobEdit { get; set; }
    public Action? RequestShowProgress { get; set; }

    public ObservableCollection<BackupJobVM> Jobs { get; } = new();

    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isBusinessSoftwareDetected;
    [ObservableProperty] private string _detectedSoftwareName = string.Empty;

    public bool IsEmpty => Jobs.Count == 0;

    public JobsViewModel(
        IBackupManagerAdapter backup,
        IBusinessSoftwareSignals watcher,
        IParallelBackupOrchestrator? orchestrator = null)
    {
        _backup = backup;
        _watcher = watcher;
        _orchestrator = orchestrator;
        Jobs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
        LoadJobs();
        _backup.StateUpdated += OnStateUpdated;
        _watcher.BusinessSoftwareDetected += OnBusinessSoftwareDetected;
        _watcher.BusinessSoftwareGone += OnBusinessSoftwareGone;
    }

    private void LoadJobs()
    {
        foreach (var job in _backup.GetJobs())
            Jobs.Add(new BackupJobVM(job));
    }

    /// <summary>
    /// Updates the observable Jobs collection after JobEditViewModel persists
    /// a new or edited job. Called via callback so the list stays in sync
    /// without a full reload.
    /// </summary>
    public void OnJobSaved(BackupJob saved, BackupJob? original)
    {
        if (original is not null)
        {
            var existing = Jobs.FirstOrDefault(j =>
                j.Name.Equals(original.Name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                Jobs.Remove(existing);
                existing.Dispose();
            }
        }
        Jobs.Add(new BackupJobVM(saved));
    }

    // ── State polling callbacks ───────────────────────────────────────────────

    private void OnStateUpdated(object? sender, StateEntry entry)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var vm = Jobs.FirstOrDefault(j => j.Name == entry.Name);
            if (vm is null) return;
            // Paused / Failed badges are owned by the UI controller (Pause click,
            // exception path). Don't let a queued backend snapshot clobber them:
            // on Pause the engine writes state=Paused but FilesRemaining is
            // preserved, so vm.Progress stays accurate from the previous Active tick.
            if (vm.UiState is not (UiJobState.Paused or UiJobState.Failed))
            {
                vm.Progress = (int)entry.Progress;
                vm.CurrentFile = entry.CurrentSource;
                vm.FilesRemaining = entry.FilesRemaining;
            }
            if (entry.State == JobState.Active)
            {
                // Promote Idle/Completed/Failed → Running on a new run; don't touch a user-paused job.
                if (vm.UiState is UiJobState.Idle or UiJobState.Completed or UiJobState.Failed)
                    vm.UiState = UiJobState.Running;
            }
            else if (entry.State == JobState.Inactive)
            {
                // Backend finished: mark Completed when the job ran to its end,
                // otherwise Idle. Skip when the run-loop already stamped Failed
                // or Paused — those badges are owned by the UI controller.
                if (vm.UiState is not (UiJobState.Failed or UiJobState.Paused))
                {
                    vm.UiState = entry.FilesRemaining == 0 && entry.TotalFilesEligible > 0
                        ? UiJobState.Completed
                        : UiJobState.Idle;
                }
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
            // Adapter PauseJob: no-op for orchestrator-tracked jobs, real pause for
            // single-run jobs. Orchestrator.Pause resets the per-job PauseGate so
            // Run-All-launched jobs stall at the next file boundary without giving up
            // their slot — Resume continues from the same offset, no restart.
            _backup.PauseJob(job.Name, $"BusinessSoftwareDetected: {name}");
            if (_orchestrator is not null && _orchestratorTrackedJobs.Contains(job.Name))
                _orchestrator.Pause(job.Name);
        }
    }

    private void OnBusinessSoftwareGone(object? sender, EventArgs _)
    {
        IsBusinessSoftwareDetected = false;
        DetectedSoftwareName = string.Empty;
        // Only resume jobs that the watcher itself paused; leave user-paused jobs alone.
        var toResume = Jobs
            .Where(j => j.UiState == UiJobState.Paused && _watcherPausedJobs.Contains(j.Name))
            .ToList();
        foreach (var job in toResume)
        {
            _watcherPausedJobs.Remove(job.Name);
            job.UiState = UiJobState.Running;
            // Adapter ResumeJob handles single-run jobs; for orchestrator-tracked
            // jobs the adapter is a no-op and the PauseGate signal does the work.
            _backup.ResumeJob(job.Name);
            if (_orchestrator is not null && _orchestratorTrackedJobs.Contains(job.Name))
                _orchestrator.Resume(job.Name);
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
            vm.Dispose();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task RunJobAsync(BackupJobVM vm)
    {
        if (IsBusinessSoftwareDetected
            || vm.UiState is UiJobState.Running or UiJobState.Paused) return;
        vm.LastError = string.Empty;
        StatusMessage = string.Empty;
        vm.UiState = UiJobState.Running;
        RequestShowProgress?.Invoke();
        var failed = false;
        try
        {
            await _backup.RunJobAsync(vm.Name).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            failed = true;
            vm.LastError = ex.Message;
            StatusMessage = ex.Message;
        }
        finally
        {
            // Final state (Completed vs Idle) is set by OnStateUpdated when the
            // backend writes its last Inactive snapshot. Leave the UiState alone
            // here so we don't overwrite Completed → Idle. Only clean up the
            // transient progress fields that have no meaning between runs.
            // Failure path overrides the backend state to Failed so the badge
            // reflects the exception that was swallowed at the adapter boundary.
            if (failed)
            {
                vm.UiState = UiJobState.Failed;
                vm.Progress = 0;
            }
            else if (vm.UiState == UiJobState.Running)
            {
                vm.UiState = UiJobState.Idle;
                vm.Progress = 0;
            }
            vm.CurrentFile = string.Empty;
            vm.FilesRemaining = 0;
        }
    }

    [RelayCommand]
    private async Task RunAllAsync()
    {
        if (IsBusinessSoftwareDetected) return;
        StatusMessage = string.Empty;
        RequestShowProgress?.Invoke();

        // Include Completed jobs so a second click after a successful run
        // re-launches every job; only Running and Paused are skipped to avoid
        // double-starting an active or user-paused backup.
        var eligible = Jobs
            .Where(j => j.UiState is UiJobState.Idle or UiJobState.Completed or UiJobState.Failed)
            .ToList();
        if (eligible.Count == 0) return;

        foreach (var vm in eligible)
        {
            vm.LastError = string.Empty;
            vm.UiState = UiJobState.Running;
        }

        // V3 path: when the parallel orchestrator is wired (GUI host), run
        // every eligible job concurrently bounded by max_parallel_jobs.
        if (_orchestrator is not null)
        {
            foreach (var vm in eligible) _orchestratorTrackedJobs.Add(vm.Name);
            try
            {
                var results = await _orchestrator.RunAsync(
                    eligible.Select(j => j.Name),
                    CancellationToken.None).ConfigureAwait(true);

                var resultByName = results.ToDictionary(r => r.JobName, StringComparer.Ordinal);
                foreach (var vm in eligible)
                {
                    if (resultByName.TryGetValue(vm.Name, out var result)
                        && result.Outcome == JobOutcome.Failed)
                    {
                        if (!string.IsNullOrEmpty(result.Message))
                            vm.LastError = result.Message;
                        vm.UiState = UiJobState.Failed;
                        vm.Progress = 0;
                    }
                }
                var firstFailure = results.FirstOrDefault(r =>
                    r.Outcome == JobOutcome.Failed && !string.IsNullOrEmpty(r.Message));
                if (firstFailure is not null)
                    StatusMessage = $"{firstFailure.JobName}: {firstFailure.Message}";
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
            }
            finally
            {
                foreach (var vm in eligible)
                {
                    _orchestratorTrackedJobs.Remove(vm.Name);
                    if (vm.UiState == UiJobState.Running)
                    {
                        vm.UiState = UiJobState.Idle;
                        vm.Progress = 0;
                    }
                }
            }
            return;
        }

        var tasks = eligible.Select(RunJobInternal).ToList();
        await Task.WhenAll(tasks).ConfigureAwait(true);
    }

    [RelayCommand]
    private void PauseJob(BackupJobVM vm)
    {
        vm.UiState = UiJobState.Paused;
        // Run-All jobs (in the orchestrator): resetting the PauseGate stalls the
        // worker at the next file boundary without releasing its slot, so Resume
        // continues from the same offset — no restart, no progress floor needed.
        if (_orchestrator is not null && _orchestratorTrackedJobs.Contains(vm.Name))
        {
            _orchestrator.Pause(vm.Name);
            return;
        }
        // Single-Run jobs (in the adapter): a real pause that stops the
        // worker at the next file boundary and persists "Paused" in
        // state.json, ready to be resumed from the same offset.
        _backup.PauseJob(vm.Name);
    }

    [RelayCommand]
    private void ResumeJob(BackupJobVM vm)
    {
        if (IsBusinessSoftwareDetected) return;
        vm.UiState = UiJobState.Running;
        // Run-All-launched jobs: signal the PauseGate so the worker thread
        // (still parked at its file boundary) continues from where it stopped.
        if (_orchestrator is not null && _orchestratorTrackedJobs.Contains(vm.Name))
        {
            vm.LastError = string.Empty;
            _orchestrator.Resume(vm.Name);
            return;
        }
        // Single-Run-launched jobs: the adapter holds the pause offset.
        _backup.ResumeJob(vm.Name);
    }

    // Used by RunAllAsync to run a job without navigating (navigation is done once before the loop).
    private async Task RunJobInternal(BackupJobVM vm)
    {
        vm.LastError = string.Empty;
        vm.UiState = UiJobState.Running;
        var failed = false;
        try
        {
            await _backup.RunJobAsync(vm.Name).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            failed = true;
            vm.LastError = ex.Message;
            StatusMessage = ex.Message;
        }
        finally
        {
            if (failed)
            {
                vm.UiState = UiJobState.Failed;
                vm.Progress = 0;
            }
            else if (vm.UiState == UiJobState.Running)
            {
                vm.UiState = UiJobState.Idle;
                vm.Progress = 0;
            }
            vm.CurrentFile = string.Empty;
            vm.FilesRemaining = 0;
        }
    }
}

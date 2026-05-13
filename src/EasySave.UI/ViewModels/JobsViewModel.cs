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
    // Subset of _watcherPausedJobs: jobs stopped via the orchestrator (Run All
    // path). Needed to route the resume back through orchestrator.RunAsync
    // instead of _backup.ResumeJob, which is a no-op for orchestrator-tracked jobs.
    private readonly HashSet<string> _watcherOrchestratorStoppedJobs = new();

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
            vm.Progress = (int)entry.Progress;
            vm.CurrentFile = entry.CurrentSource;
            vm.FilesRemaining = entry.FilesRemaining;
            if (entry.State == JobState.Active)
            {
                // Promote Idle/Completed → Running on a new run; don't touch a user-paused job.
                if (vm.UiState is UiJobState.Idle or UiJobState.Completed)
                    vm.UiState = UiJobState.Running;
            }
            else
            {
                // Backend finished (Inactive): mark Completed when the job ran to
                // its end (no remaining files), otherwise fall back to Idle so a
                // job that exited via pause/cancel doesn't claim success.
                vm.UiState = entry.FilesRemaining == 0 && entry.TotalFilesEligible > 0
                    ? UiJobState.Completed
                    : UiJobState.Idle;
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
            _backup.PauseJob(job.Name, $"BusinessSoftwareDetected: {name}");
            // Mirror the wiring already in place for the manual Pause button:
            // jobs launched by Run All are tracked in the orchestrator, not in
            // the adapter, so PauseJob above is a no-op for them. Stop() cancels
            // the per-job CTS and halts the worker at the next file boundary.
            if (_orchestrator is not null)
            {
                _orchestrator.Stop(job.Name);
                _watcherOrchestratorStoppedJobs.Add(job.Name);
            }
        }
    }

    private void OnBusinessSoftwareGone(object? sender, EventArgs e)
    {
        IsBusinessSoftwareDetected = false;
        DetectedSoftwareName = string.Empty;
        // Only resume jobs that the watcher itself paused; leave user-paused jobs alone.
        var toResume = Jobs
            .Where(j => j.UiState == UiJobState.Paused && _watcherPausedJobs.Contains(j.Name))
            .ToList();
        var orchestratorRestart = toResume
            .Where(j => _watcherOrchestratorStoppedJobs.Contains(j.Name))
            .Select(j => j.Name)
            .ToList();
        foreach (var job in toResume)
        {
            _watcherPausedJobs.Remove(job.Name);
            _watcherOrchestratorStoppedJobs.Remove(job.Name);
            job.UiState = UiJobState.Running;
            // Resumes adapter-tracked jobs (single-run path); no-op for orchestrator-tracked.
            _backup.ResumeJob(job.Name);
        }
        // Orchestrator-tracked jobs were stopped (one-way). Restart them.
        // Limitation: restarts from the beginning of the job (resumeAfterPath is
        // not propagated by BackupManagerJobRunner in this iteration — see PR #180).
        if (orchestratorRestart.Count > 0)
            _ = RunWatcherOrchestratorJobsAsync(orchestratorRestart);
    }

    private async Task RunWatcherOrchestratorJobsAsync(IReadOnlyList<string> jobNames)
    {
        try
        {
            await _orchestrator!.RunAsync(jobNames, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            foreach (var name in jobNames)
            {
                var vm = Jobs.FirstOrDefault(j => j.Name == name);
                if (vm?.UiState == UiJobState.Running)
                {
                    vm.UiState = UiJobState.Idle;
                    vm.Progress = 0;
                }
            }
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
            // Final state (Completed vs Idle) is set by OnStateUpdated when the
            // backend writes its last Inactive snapshot. Leave the UiState alone
            // here so we don't overwrite Completed → Idle. Only clean up the
            // transient progress fields that have no meaning between runs.
            if (vm.UiState == UiJobState.Running)
            {
                vm.UiState = UiJobState.Idle;
                // The job exited mid-flight (exception or external cancel) — wipe
                // the leftover progress so the bar doesn't stay at e.g. 47% with
                // an Idle badge until the next run starts.
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
        RequestShowProgress?.Invoke();

        // Include Completed jobs so a second click after a successful run
        // re-launches every job; only Running and Paused are skipped to avoid
        // double-starting an active or user-paused backup.
        var eligible = Jobs
            .Where(j => j.UiState is UiJobState.Idle or UiJobState.Completed)
            .ToList();
        if (eligible.Count == 0) return;

        foreach (var vm in eligible) vm.UiState = UiJobState.Running;

        // V3 path: when the parallel orchestrator is wired (GUI host), run
        // every eligible job concurrently bounded by max_parallel_jobs.
        // Progress updates still flow through StateTracker → adapter
        // .StateUpdated → OnStateUpdated, so the cards refresh as usual.
        // Fallback (no orchestrator injected — e.g. unit tests of this
        // ViewModel) keeps the original Task.WhenAll loop via the adapter.
        if (_orchestrator is not null)
        {
            try
            {
                await _orchestrator.RunAsync(
                    eligible.Select(j => j.Name),
                    CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
            }
            finally
            {
                // If the orchestrator threw (ObjectDisposedException on
                // shutdown, ArgumentException on duplicate names) before
                // the engine could publish its final state, the cards we
                // promoted to Running above would otherwise stay stuck
                // there. Reset only the cards we touched and only if they
                // are still Running — leave cards the engine has already
                // moved to Paused / Completed alone.
                foreach (var vm in eligible)
                {
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
        // Single-Run jobs (in the adapter): a real pause that stops the
        // worker at the next file boundary and persists "Paused" in
        // state.json, ready to be resumed from the same offset.
        _backup.PauseJob(vm.Name);
        // Run-All jobs (in the orchestrator): the v3 BackupManagerJobRunner
        // does not honor ctx.PauseGate today, so orchestrator.Pause() would
        // be a no-op. Use orchestrator.Stop() which cancels the per-job CTS
        // and stops the worker at the next file boundary. Effective result:
        // the job halts and shows Inactive — it cannot be resumed from the
        // same offset on this path (Resume button is a no-op for Run-All
        // jobs in this iteration). Documented limitation.
        _orchestrator?.Stop(vm.Name);
    }

    [RelayCommand]
    private void ResumeJob(BackupJobVM vm)
    {
        if (IsBusinessSoftwareDetected) return;
        vm.UiState = UiJobState.Running;
        // Only the adapter knows how to resume from a saved offset. For
        // Run-All-launched jobs, Pause acted as Stop above; Resume is a
        // visual no-op until the user clicks Run on the card again.
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
            // Final state (Completed vs Idle) is set by OnStateUpdated when the
            // backend writes its last Inactive snapshot. Leave the UiState alone
            // here so we don't overwrite Completed → Idle. Only clean up the
            // transient progress fields that have no meaning between runs.
            if (vm.UiState == UiJobState.Running)
            {
                vm.UiState = UiJobState.Idle;
                // The job exited mid-flight (exception or external cancel) — wipe
                // the leftover progress so the bar doesn't stay at e.g. 47% with
                // an Idle badge until the next run starts.
                vm.Progress = 0;
            }
            vm.CurrentFile = string.Empty;
            vm.FilesRemaining = 0;
        }
    }
}

using EasySave.Models;
using EasySave.Services;
using EasySave.Shared;
using EasySave.UI.Services;
using EasySave.UI.ViewModels;

namespace EasySave.Tests.V2;

public class JobsViewModelWatcherIntegrationTests
{
    // Triggers watcher appear/gone events synchronously without a real polling loop.
    private sealed class StubSignals : IBusinessSoftwareSignals
    {
        public event EventHandler<string>? BusinessSoftwareDetected;
        public event EventHandler? BusinessSoftwareGone;

        public void RaiseDetected(string name) =>
            BusinessSoftwareDetected?.Invoke(this, name);

        public void RaiseGone() =>
            BusinessSoftwareGone?.Invoke(this, EventArgs.Empty);
    }

    // Records PauseJob / ResumeJob calls; returns no jobs from GetJobs so
    // LoadJobs leaves Jobs empty and tests can add VMs manually.
    private sealed class RecordingAdapter : IBackupManagerAdapter
    {
        public List<string> PausedJobs { get; } = new();
        public List<string> ResumedJobs { get; } = new();

        public event EventHandler<StateEntry>? StateUpdated { add { } remove { } }

        public IReadOnlyList<BackupJob> GetJobs() => Array.Empty<BackupJob>();
        public void AddJob(BackupJob job) { }
        public void RemoveJob(string name) { }
        public Task RunJobAsync(string jobName, string? resumeAfterPath = null, CancellationToken ct = default)
            => Task.CompletedTask;
        public void PauseJob(string jobName, string reason = "UserRequested")
            => PausedJobs.Add(jobName);
        public void ResumeJob(string jobName) => ResumedJobs.Add(jobName);
        public bool IsJobRunning(string jobName) => false;
        public void Dispose() { }
    }

    // Returns immediately — used for tests that only inspect Stop calls.
    private sealed class RecordingOrchestrator : IParallelBackupOrchestrator
    {
        public List<string> StoppedJobs { get; } = new();
        public List<IReadOnlyList<string>> RunAsyncCalls { get; } = new();

        public event Action<JobProgressDto> ProgressChanged = static _ => { };

        public Task<IReadOnlyList<JobResult>> RunAsync(IEnumerable<string> jobNames, CancellationToken ct)
        {
            RunAsyncCalls.Add(jobNames.ToList());
            return Task.FromResult<IReadOnlyList<JobResult>>(Array.Empty<JobResult>());
        }

        public void Pause(string jobName) { }
        public void Resume(string jobName) { }
        public void Stop(string jobName) => StoppedJobs.Add(jobName);
        public void PauseAll() { }
        public void ResumeAll() { }
        public void StopAll() { }
        public void Dispose() { }
    }

    // Blocks RunAsync until the caller resolves the TCS, then immediately for
    // any subsequent calls (TCS task is permanently complete after SetResult).
    // Used to simulate a Run All that is in flight while the watcher fires.
    private sealed class BlockingOrchestrator : IParallelBackupOrchestrator
    {
        private readonly Task<IReadOnlyList<JobResult>> _runResult;
        public List<string> StoppedJobs { get; } = new();
        public List<IReadOnlyList<string>> RunAsyncCalls { get; } = new();

        public BlockingOrchestrator(Task<IReadOnlyList<JobResult>> runResult)
            => _runResult = runResult;

        public event Action<JobProgressDto> ProgressChanged = static _ => { };

        public Task<IReadOnlyList<JobResult>> RunAsync(IEnumerable<string> jobNames, CancellationToken ct)
        {
            RunAsyncCalls.Add(jobNames.ToList());
            return _runResult;
        }

        public void Stop(string jobName) => StoppedJobs.Add(jobName);
        public void Pause(string jobName) { }
        public void Resume(string jobName) { }
        public void PauseAll() { }
        public void ResumeAll() { }
        public void StopAll() { }
        public void Dispose() { }
    }

    private static BackupJobVM MakeVm(string name, UiJobState state)
    {
        var vm = new BackupJobVM(new BackupJob { Name = name });
        vm.UiState = state;
        return vm;
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void OnBusinessSoftwareDetected_StopsRunAllJobsViaOrchestrator()
    {
        var signals = new StubSignals();
        var adapter = new RecordingAdapter();
        var orchestrator = new RecordingOrchestrator();
        var sut = new JobsViewModel(adapter, signals, orchestrator);

        var job1 = MakeVm("Job1", UiJobState.Running);
        var job2 = MakeVm("Job2", UiJobState.Running);
        sut.Jobs.Add(job1);
        sut.Jobs.Add(job2);

        signals.RaiseDetected("calc");

        Assert.Contains("Job1", orchestrator.StoppedJobs);
        Assert.Contains("Job2", orchestrator.StoppedJobs);
        Assert.Equal(UiJobState.Paused, job1.UiState);
        Assert.Equal(UiJobState.Paused, job2.UiState);
        // Adapter PauseJob is also called (covers single-run jobs on the same path).
        Assert.Contains("Job1", adapter.PausedJobs);
        Assert.Contains("Job2", adapter.PausedJobs);
    }

    [Fact]
    public async Task OnBusinessSoftwareGone_ResumesPreviouslyPausedOrchestratorJobs()
    {
        // Use a blocking orchestrator so that RunAllAsync is still in flight
        // (and _orchestratorTrackedJobs is populated) when the watcher fires.
        var tcs = new TaskCompletionSource<IReadOnlyList<JobResult>>();
        var signals = new StubSignals();
        var adapter = new RecordingAdapter();
        var orchestrator = new BlockingOrchestrator(tcs.Task);
        var sut = new JobsViewModel(adapter, signals, orchestrator);

        // Jobs start Idle so Run All picks them up.
        var job1 = MakeVm("Job1", UiJobState.Idle);
        var job2 = MakeVm("Job2", UiJobState.Idle);
        sut.Jobs.Add(job1);
        sut.Jobs.Add(job2);

        // Run All executes synchronously up to "await _orchestrator.RunAsync()",
        // registering Job1 and Job2 in _orchestratorTrackedJobs before yielding.
        var runAll = sut.RunAllCommand.ExecuteAsync(null);

        // While the orchestrator is blocking: fire the watcher.
        signals.RaiseDetected("calc");

        // Complete the orchestrator run so RunAll's finally clears _orchestratorTrackedJobs.
        tcs.SetResult(Array.Empty<JobResult>());
        await runAll;

        // Gone: orchestrator-tracked jobs must be restarted via RunAsync.
        signals.RaiseGone();

        // Two RunAsync calls total: 1 for Run All, 1 for watcher restart.
        Assert.Equal(2, orchestrator.RunAsyncCalls.Count);
        var restarted = orchestrator.RunAsyncCalls[1];
        Assert.Contains("Job1", restarted);
        Assert.Contains("Job2", restarted);
        // Jobs are not Paused after the restart (either Running while the
        // fake orchestrator runs, or Idle once it completes immediately).
        Assert.NotEqual(UiJobState.Paused, job1.UiState);
        Assert.NotEqual(UiJobState.Paused, job2.UiState);
    }

    [Fact]
    public void OnBusinessSoftwareDetected_DoesNotPauseUserPausedJobs()
    {
        var signals = new StubSignals();
        var adapter = new RecordingAdapter();
        var orchestrator = new RecordingOrchestrator();
        var sut = new JobsViewModel(adapter, signals, orchestrator);

        var running = MakeVm("Job1", UiJobState.Running);
        var userPaused = MakeVm("Job2", UiJobState.Paused);
        sut.Jobs.Add(running);
        sut.Jobs.Add(userPaused);

        signals.RaiseDetected("calc");

        // Only the Running job should be stopped.
        Assert.Equal(new[] { "Job1" }, orchestrator.StoppedJobs);
        Assert.Equal(UiJobState.Paused, running.UiState);
        // User-paused job must not be touched.
        Assert.DoesNotContain("Job2", orchestrator.StoppedJobs);
        Assert.DoesNotContain("Job2", adapter.PausedJobs);
        Assert.Equal(UiJobState.Paused, userPaused.UiState);
    }

    [Fact]
    public void OnBusinessSoftwareGone_AdapterTrackedJob_ResumesViaAdapterOnly_NoOrchestratorRestart()
    {
        // Regression guard for the double-start bug: a job started via the
        // single-job Run card is adapter-tracked, not orchestrator-tracked.
        // Even when an orchestrator is injected, Gone must NOT dispatch a
        // second orchestrator.RunAsync for that job.
        var signals = new StubSignals();
        var adapter = new RecordingAdapter();
        var orchestrator = new RecordingOrchestrator();
        var sut = new JobsViewModel(adapter, signals, orchestrator);

        // Job1 is Running but was never registered in _orchestratorTrackedJobs
        // because RunAllAsync was never called.
        var job1 = MakeVm("Job1", UiJobState.Running);
        sut.Jobs.Add(job1);

        signals.RaiseDetected("calc");
        signals.RaiseGone();

        // Adapter-side resume must have been called.
        Assert.Contains("Job1", adapter.ResumedJobs);
        // Orchestrator must NOT have been asked to restart the job.
        Assert.Empty(orchestrator.RunAsyncCalls);
    }
}

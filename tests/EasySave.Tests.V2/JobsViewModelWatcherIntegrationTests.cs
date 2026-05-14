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

    // Returns immediately — used for tests that only inspect Pause/Resume calls.
    private sealed class RecordingOrchestrator : IParallelBackupOrchestrator
    {
        public List<string> PausedJobs { get; } = new();
        public List<string> ResumedJobs { get; } = new();
        public List<string> StoppedJobs { get; } = new();
        public List<IReadOnlyList<string>> RunAsyncCalls { get; } = new();

        public event Action<JobProgressDto> ProgressChanged = static _ => { };

        public Task<IReadOnlyList<JobResult>> RunAsync(IEnumerable<string> jobNames, CancellationToken ct)
        {
            RunAsyncCalls.Add(jobNames.ToList());
            return Task.FromResult<IReadOnlyList<JobResult>>(Array.Empty<JobResult>());
        }

        public void Pause(string jobName) => PausedJobs.Add(jobName);
        public void Resume(string jobName) => ResumedJobs.Add(jobName);
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
        public List<string> PausedJobs { get; } = new();
        public List<string> ResumedJobs { get; } = new();
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
        public void Pause(string jobName) => PausedJobs.Add(jobName);
        public void Resume(string jobName) => ResumedJobs.Add(jobName);
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
    public async Task OnBusinessSoftwareDetected_PausesRunAllJobsViaOrchestratorPauseGate()
    {
        // V3.1: orchestrator-tracked jobs are paused via orchestrator.Pause
        // (resets the PauseGate) rather than Stop+restart. The worker thread
        // parks at the next file boundary and resumes from the same offset.
        var tcs = new TaskCompletionSource<IReadOnlyList<JobResult>>();
        var signals = new StubSignals();
        var adapter = new RecordingAdapter();
        var orchestrator = new BlockingOrchestrator(tcs.Task);
        var sut = new JobsViewModel(adapter, signals, orchestrator);

        var job1 = MakeVm("Job1", UiJobState.Idle);
        var job2 = MakeVm("Job2", UiJobState.Idle);
        sut.Jobs.Add(job1);
        sut.Jobs.Add(job2);

        // RunAll registers both jobs in _orchestratorTrackedJobs and then blocks.
        var runAll = sut.RunAllCommand.ExecuteAsync(null);

        signals.RaiseDetected("calc");

        Assert.Contains("Job1", orchestrator.PausedJobs);
        Assert.Contains("Job2", orchestrator.PausedJobs);
        // Stop must NOT be called — the whole point is to keep the worker
        // parked on the PauseGate so Resume picks up at the same file.
        Assert.Empty(orchestrator.StoppedJobs);
        Assert.Equal(UiJobState.Paused, job1.UiState);
        Assert.Equal(UiJobState.Paused, job2.UiState);
        // Adapter PauseJob is also called (covers single-run jobs on the same path).
        Assert.Contains("Job1", adapter.PausedJobs);
        Assert.Contains("Job2", adapter.PausedJobs);

        tcs.SetResult(Array.Empty<JobResult>());
        await runAll;
    }

    [Fact]
    public async Task OnBusinessSoftwareGone_ResumesOrchestratorJobsViaPauseGate_NoRestart()
    {
        // V3.1: orchestrator-tracked jobs resume by signaling the PauseGate
        // through orchestrator.Resume. The original RunAsync stays in flight
        // (no second RunAsync call) — the worker thread continues from the
        // paused file boundary.
        var tcs = new TaskCompletionSource<IReadOnlyList<JobResult>>();
        var signals = new StubSignals();
        var adapter = new RecordingAdapter();
        var orchestrator = new BlockingOrchestrator(tcs.Task);
        var sut = new JobsViewModel(adapter, signals, orchestrator);

        var job1 = MakeVm("Job1", UiJobState.Idle);
        var job2 = MakeVm("Job2", UiJobState.Idle);
        sut.Jobs.Add(job1);
        sut.Jobs.Add(job2);

        var runAll = sut.RunAllCommand.ExecuteAsync(null);

        signals.RaiseDetected("calc");
        signals.RaiseGone();

        Assert.Contains("Job1", orchestrator.ResumedJobs);
        Assert.Contains("Job2", orchestrator.ResumedJobs);
        // Only one RunAsync — the original Run All. No restart was needed.
        Assert.Single(orchestrator.RunAsyncCalls);
        Assert.NotEqual(UiJobState.Paused, job1.UiState);
        Assert.NotEqual(UiJobState.Paused, job2.UiState);

        tcs.SetResult(Array.Empty<JobResult>());
        await runAll;
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

        // Only the Running job should be paused.
        Assert.Equal(UiJobState.Paused, running.UiState);
        Assert.DoesNotContain("Job2", orchestrator.PausedJobs);
        Assert.DoesNotContain("Job2", adapter.PausedJobs);
        Assert.Equal(UiJobState.Paused, userPaused.UiState);
    }

    [Fact]
    public void OnBusinessSoftwareGone_AdapterTrackedJob_ResumesViaAdapterOnly_NoOrchestratorResume()
    {
        // Regression guard: a job started via the single-job Run card is
        // adapter-tracked, not orchestrator-tracked. Even when an orchestrator
        // is injected, Gone must NOT dispatch orchestrator.Resume for that job.
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
        // Orchestrator must NOT have been asked to resume the job.
        Assert.Empty(orchestrator.ResumedJobs);
        Assert.Empty(orchestrator.RunAsyncCalls);
    }

    [Fact]
    public async Task PauseCommand_OrchestratorTrackedJob_CallsOrchestratorPause_NotStop()
    {
        // V3.1: a user click on the Pause button for a Run-All-launched job must
        // route through orchestrator.Pause (PauseGate) so Resume picks up at
        // the same offset. Stop+restart is the legacy behaviour we removed.
        var tcs = new TaskCompletionSource<IReadOnlyList<JobResult>>();
        var signals = new StubSignals();
        var adapter = new RecordingAdapter();
        var orchestrator = new BlockingOrchestrator(tcs.Task);
        var sut = new JobsViewModel(adapter, signals, orchestrator);

        var job1 = MakeVm("Job1", UiJobState.Idle);
        sut.Jobs.Add(job1);

        var runAll = sut.RunAllCommand.ExecuteAsync(null);

        sut.PauseJobCommand.Execute(job1);

        Assert.Equal(UiJobState.Paused, job1.UiState);
        Assert.Contains("Job1", orchestrator.PausedJobs);
        Assert.Empty(orchestrator.StoppedJobs);

        tcs.SetResult(Array.Empty<JobResult>());
        await runAll;
    }

    [Fact]
    public async Task ResumeCommand_OrchestratorTrackedJob_CallsOrchestratorResume_NoSecondRunAsync()
    {
        // V3.1: Resume of an orchestrator-tracked job signals the PauseGate
        // — the worker thread continues from the paused file boundary.
        // No second RunAsync is fired, no progress floor is needed.
        var tcs = new TaskCompletionSource<IReadOnlyList<JobResult>>();
        var signals = new StubSignals();
        var adapter = new RecordingAdapter();
        var orchestrator = new BlockingOrchestrator(tcs.Task);
        var sut = new JobsViewModel(adapter, signals, orchestrator);

        var job1 = MakeVm("Job1", UiJobState.Idle);
        sut.Jobs.Add(job1);

        var runAll = sut.RunAllCommand.ExecuteAsync(null);

        sut.PauseJobCommand.Execute(job1);
        sut.ResumeJobCommand.Execute(job1);

        Assert.Equal(UiJobState.Running, job1.UiState);
        Assert.Contains("Job1", orchestrator.ResumedJobs);
        // Only the original Run All — no restart.
        Assert.Single(orchestrator.RunAsyncCalls);

        tcs.SetResult(Array.Empty<JobResult>());
        await runAll;
    }
}

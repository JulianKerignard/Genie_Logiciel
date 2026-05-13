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

    // Records Stop calls and RunAsync invocations.
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
    public void OnBusinessSoftwareGone_ResumesPreviouslyPausedJobs()
    {
        var signals = new StubSignals();
        var adapter = new RecordingAdapter();
        var orchestrator = new RecordingOrchestrator();
        var sut = new JobsViewModel(adapter, signals, orchestrator);

        var job1 = MakeVm("Job1", UiJobState.Running);
        var job2 = MakeVm("Job2", UiJobState.Running);
        sut.Jobs.Add(job1);
        sut.Jobs.Add(job2);

        // Detected registers them in _watcherPausedJobs + _watcherOrchestratorStoppedJobs.
        signals.RaiseDetected("calc");
        // Gone should restart them via the orchestrator.
        signals.RaiseGone();

        var restarted = Assert.Single(orchestrator.RunAsyncCalls);
        Assert.Contains("Job1", restarted);
        Assert.Contains("Job2", restarted);
        // The fake orchestrator returns immediately so the finally block in
        // RunWatcherOrchestratorJobsAsync resets the cards to Idle — that is
        // correct end-state behaviour.  What matters here is that RunAsync
        // was called with both job names (the restart was dispatched).
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
}

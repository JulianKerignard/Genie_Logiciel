namespace EasySave.Services;

// STUB: no-op orchestrator used until Chloé's v3 parallel orchestrator is merged.
// Commands are accepted and silently ignored so the server wiring compiles and runs.
internal sealed class NoOpParallelBackupOrchestrator : IParallelBackupOrchestrator
{
    public Task PauseAsync(string jobName) => Task.CompletedTask;
    public Task ResumeAsync(string jobName) => Task.CompletedTask;
    public Task StopAsync(string jobName) => Task.CompletedTask;
}

using EasySave.Shared;

namespace EasySave.Services;

// Temporary stub — awaiting full implementation from Chloé.
// Replace this class once IParallelBackupOrchestrator is implemented.
public sealed class ParallelBackupOrchestratorStub : IParallelBackupOrchestrator
{
    // Seeded no-op delegate so the field is never null and matches the
    // non-nullable IParallelBackupOrchestrator.ProgressChanged invariant,
    // mirroring the concrete ParallelBackupOrchestrator. The stub never
    // raises the event itself, but consumers that subscribe must still
    // see a non-null delegate.
    public event Action<JobProgressDto> ProgressChanged = static _ => { };

    public Task<IReadOnlyList<JobResult>> RunAsync(IEnumerable<string> jobNames, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<JobResult>>(Array.Empty<JobResult>());

    public void Pause(string jobName) { }
    public void Resume(string jobName) { }
    public void Stop(string jobName) { }
    public void PauseAll() { }
    public void ResumeAll() { }
    public void StopAll() { }
    public void Dispose() { }
}

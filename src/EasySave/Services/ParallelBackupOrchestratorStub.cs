using EasySave.Shared;

namespace EasySave.Services;

// Temporary stub — awaiting full implementation from Chloé.
// Replace this class once IParallelBackupOrchestrator is implemented.
public sealed class ParallelBackupOrchestratorStub : IParallelBackupOrchestrator
{
#pragma warning disable CS0067 // event unused in stub — real impl will fire it
    public event Action<JobProgressDto>? ProgressChanged;
#pragma warning restore CS0067

    public Task<IReadOnlyList<JobResult>> RunAsync(IEnumerable<string> jobNames, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<JobResult>>(Array.Empty<JobResult>());

    public void Pause(string jobName) { }
    public void Resume(string jobName) { }
    public void Stop(string jobName) { }
    public void Dispose() { }
}

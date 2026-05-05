using EasySave.Services;

namespace EasySave.UI.Services;

// Bridges IParallelBackupOrchestrator (v3 contract) to the existing
// IBackupManagerAdapter until the real parallel orchestrator is implemented.
// Stop maps to PauseJob because the current adapter has no hard-stop: the
// job is cancelled at the next file boundary and left as Paused in state.json.
// Replace this class with a proper implementation once the parallel orchestrator
// is available (see IParallelBackupOrchestrator stub in EasySave.Services).
internal sealed class BackupManagerOrchestratorBridge : IParallelBackupOrchestrator
{
    private readonly IBackupManagerAdapter _adapter;

    public BackupManagerOrchestratorBridge(IBackupManagerAdapter adapter)
        => _adapter = adapter;

    public void Pause(string jobName) => _adapter.PauseJob(jobName, "RemoteConsole");

    public void Resume(string jobName) => _adapter.ResumeJob(jobName);

    // Maps Stop to a forced pause — the job stops at the next file boundary
    // and is left in the Paused state. A full stop (Inactive) requires the
    // parallel orchestrator which the parallel-jobs dev will implement.
    public void Stop(string jobName) => _adapter.PauseJob(jobName, "RemoteConsoleStop");
}

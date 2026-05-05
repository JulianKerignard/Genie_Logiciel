namespace EasySave.Services;

// Stub interface for the v3 parallel backup orchestrator (implemented by the
// parallel-jobs dev). TcpRemoteConsoleServer routes Pause/Play/Stop commands
// received from remote consoles through this interface.
// When the real implementation is injected, drop the BackupManagerOrchestratorBridge
// adapter in EasySave.UI and wire the real orchestrator directly.
public interface IParallelBackupOrchestrator
{
    void Pause(string jobName);
    void Resume(string jobName);
    void Stop(string jobName);
}

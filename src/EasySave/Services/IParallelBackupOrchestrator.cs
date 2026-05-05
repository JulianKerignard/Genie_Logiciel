namespace EasySave.Services;

// STUB: temporary interface pending Chloé's v3 parallel orchestrator implementation.
// Defines the remote-control surface consumed by TcpRemoteConsoleServer to pause,
// resume, and stop individual backup jobs upon command from remote clients.
public interface IParallelBackupOrchestrator
{
    Task PauseAsync(string jobName);
    Task ResumeAsync(string jobName);
    Task StopAsync(string jobName);
}

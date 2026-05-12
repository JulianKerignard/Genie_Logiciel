namespace EasyLog;

/// <summary>
/// V3 event categories that <see cref="LogEntry"/> can attach to a daily log
/// row. The default v1/v2 backup-row behaviour is preserved when
/// <see cref="LogEntry.EventType"/> is null — v1 / v2 consumers see the
/// exact same JSON / XML shape they always have.
/// </summary>
public enum LogEvent
{
    /// <summary>A v1 / v2 file-transfer row. Emitted when no specific V3 event applies.</summary>
    FileTransfer = 0,

    /// <summary>A remote operator console opened a session against this host.</summary>
    RemoteConsoleConnected = 1,

    /// <summary>The remote operator console session was closed.</summary>
    RemoteConsoleDisconnected = 2,

    /// <summary>A backup job started in parallel with one or more other running jobs.</summary>
    ParallelJobStarted = 3,

    /// <summary>A backup job that ran in parallel completed (success or failure).</summary>
    ParallelJobFinished = 4,

    /// <summary>A file above the configured big-file threshold was queued behind the big-file gate.</summary>
    BigFileEnqueued = 5,

    /// <summary>A previously enqueued big file was released by the gate and transferred.</summary>
    BigFileTransferred = 6,

    /// <summary>A Pause / Play / Stop command arrived from the remote console.</summary>
    CommandReceived = 7,

    /// <summary>A backup job stalled at a file boundary because its PauseGate was reset by IJobController.Pause(jobName) or PauseAll() (business-software watcher included).</summary>
    JobPaused = 8,

    /// <summary>A previously paused backup job resumed after its PauseGate was signaled by IJobController.Resume(jobName) or ResumeAll().</summary>
    JobResumed = 9,
}

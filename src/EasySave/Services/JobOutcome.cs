namespace EasySave.Services;

// Final disposition of a job after a parallel orchestrator run.
// Explicit numeric values keep telemetry and persisted result logs stable
// across releases.
public enum JobOutcome
{
    Succeeded = 0,
    Failed = 1,
    Cancelled = 2
}

using EasyLog;
using EasySave.Shared;

namespace EasySave.Services;

// Per-job state container used by the v3 parallel orchestrator. One instance
// is created per running job and lives for the duration of that single
// execution; the orchestrator disposes it once the job ends. Each context
// owns its own cancellation source, pause gate, progress snapshot and logger
// reference, so jobs running side by side never share mutable state.
//
// Pause/Resume mechanics: PauseGate is constructed in the signaled (open)
// state. RunJob waits on the gate at every file boundary; resetting the gate
// stalls the job at the next boundary, setting it again resumes. Stop goes
// through Cts.Cancel() and unblocks any thread parked on PauseGate.Wait by
// passing the linked token into Wait.
public sealed class JobExecutionContext : IDisposable
{
    public string JobName { get; }
    public CancellationTokenSource Cts { get; }
    public ManualResetEventSlim PauseGate { get; }
    public IDailyLogger Logger { get; }

    // Backed by a volatile field rather than an auto-property because the job
    // thread writes Progress while a different thread (orchestrator / UI
    // reporter) reads it concurrently. Without a memory barrier the reader
    // can keep a stale reference cached in a register. volatile here is
    // enough because JobProgressDto is an immutable record: once the
    // reference is published, the contents are safe to read without further
    // synchronization.
    private volatile JobProgressDto _progress = null!;
    public JobProgressDto Progress
    {
        get => _progress;
        set => _progress = value;
    }

    public JobExecutionContext(string jobName, IDailyLogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        ArgumentNullException.ThrowIfNull(logger);

        JobName = jobName;
        Logger = logger;
        Cts = new CancellationTokenSource();
        PauseGate = new ManualResetEventSlim(initialState: true);
        Progress = new JobProgressDto(
            JobName: jobName,
            State: JobStateEnum.Done,
            CurrentFile: string.Empty,
            FilesLeft: 0,
            TotalFiles: 0,
            BytesLeft: 0,
            BytesTotal: 0);
    }

    public void Dispose()
    {
        Cts.Dispose();
        PauseGate.Dispose();
    }
}

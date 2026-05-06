using EasySave.Services;
using EasySave.Shared;

namespace EasySave.Infrastructure.Events;

/// <summary>
/// Hooks the engine's <see cref="StateTracker.JobProgressChanged"/> event and
/// republishes each snapshot on the shared <see cref="IEventBus"/> as an
/// <see cref="EventDto"/>. Decouples the engine from the V3 transport layer:
/// the engine never references the remote console, the WebSocket layer, or
/// any other consumer — they all subscribe to the bus.
/// </summary>
/// <remarks>
/// One bridge per <see cref="StateTracker"/> instance. Call <see cref="Start"/>
/// once at app boot, after the bus and the tracker are constructed; the bridge
/// detaches itself on <see cref="Dispose"/>.
/// </remarks>
public sealed class StateTrackerEventBridge : IDisposable
{
    private readonly StateTracker _tracker;
    private readonly IEventBus _bus;
    private bool _started;

    public StateTrackerEventBridge(StateTracker tracker, IEventBus bus)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(bus);
        _tracker = tracker;
        _bus = bus;
    }

    public void Start()
    {
        if (_started) return;
        _started = true;
        _tracker.JobProgressChanged += OnJobProgressChanged;
    }

    private void OnJobProgressChanged(object? sender, StateEntry entry)
    {
        // The handler runs synchronously inside StateTracker.Update (after the
        // _lock is released, see StateTracker comment "outside _lock on
        // purpose"). Publish enqueues and returns immediately so the engine's
        // file copy loop never waits on a consumer — the bus contract.
        var progress = new JobProgressDto(
            JobName: entry.Name,
            State: ToWireState(entry.State),
            CurrentFile: entry.CurrentSource,
            FilesLeft: entry.FilesRemaining,
            TotalFiles: entry.TotalFilesEligible,
            BytesLeft: entry.SizeRemaining,
            BytesTotal: entry.TotalSize);

        _bus.Publish(new EventDto(
            Timestamp: DateTimeOffset.Now,
            Type: EventType.JobProgress,
            Progress: progress));
    }

    private static JobStateEnum ToWireState(JobState state) => state switch
    {
        JobState.Active => JobStateEnum.Running,
        JobState.Paused => JobStateEnum.Paused,
        _ => JobStateEnum.Done,
    };

    public void Dispose()
    {
        if (!_started) return;
        _tracker.JobProgressChanged -= OnJobProgressChanged;
        _started = false;
    }
}

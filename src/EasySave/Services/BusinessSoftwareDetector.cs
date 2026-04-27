namespace EasySave.Services;

/// <summary>
/// Polls the OS process list at a configurable interval and raises
/// <see cref="BusinessSoftwareDetected"/> / <see cref="BusinessSoftwareClosed"/>
/// events whenever a watched executable appears or disappears.
/// Used by <see cref="BackupManager"/> to pause running jobs when an operator
/// opens a business application listed in <c>appsettings.json</c>.
/// </summary>
public sealed class BusinessSoftwareDetector : IDisposable
{
    /// <summary>Default polling cadence when none is supplied to the constructor.</summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(2);

    private readonly IProcessProvider _provider;
    private readonly HashSet<string> _watched;
    private readonly TimeSpan _pollInterval;
    private readonly object _lock = new();

    private System.Threading.Timer? _timer;
    private HashSet<string> _lastDetected = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised once for each watched process that appears between two polls.</summary>
    public event EventHandler<string>? BusinessSoftwareDetected;

    /// <summary>Raised once for each watched process that disappears between two polls.</summary>
    public event EventHandler<string>? BusinessSoftwareClosed;

    /// <param name="provider">Source of the running process names. Required.</param>
    /// <param name="watchedProcessNames">Process names to watch (case-insensitive). Required, but may be empty.</param>
    /// <param name="pollInterval">Polling cadence. Defaults to <see cref="DefaultPollInterval"/> when null.</param>
    public BusinessSoftwareDetector(
        IProcessProvider provider,
        IEnumerable<string> watchedProcessNames,
        TimeSpan? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(watchedProcessNames);

        _provider = provider;
        _watched = new HashSet<string>(watchedProcessNames, StringComparer.OrdinalIgnoreCase);
        _pollInterval = pollInterval ?? DefaultPollInterval;
    }

    /// <summary>True if at least one watched process was running on the latest poll.</summary>
    public bool IsAnyBusinessSoftwareRunning
    {
        get { lock (_lock) return _lastDetected.Count > 0; }
    }

    /// <summary>Snapshot of the watched processes that were running on the latest poll.</summary>
    public IReadOnlyCollection<string> CurrentlyRunning
    {
        get { lock (_lock) return new HashSet<string>(_lastDetected, StringComparer.OrdinalIgnoreCase); }
    }

    /// <summary>
    /// Starts the periodic timer and immediately performs one synchronous
    /// poll so consumers do not wait a full interval before the first event.
    /// Calling Start a second time without Stop is a no-op.
    /// </summary>
    public void Start()
    {
        bool started;
        lock (_lock)
        {
            started = _timer is null;
            if (started)
            {
                _timer = new System.Threading.Timer(_ => Refresh(), null, _pollInterval, _pollInterval);
            }
        }

        // Initial poll outside the lock so subscribers can call Stop or
        // Refresh from inside their event handlers without deadlocking.
        if (started) Refresh();
    }

    /// <summary>Stops the periodic timer. Safe to call multiple times.</summary>
    public void Stop()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    /// <summary>
    /// Performs one polling pass synchronously. Exposed so tests can drive the
    /// detector deterministically without depending on the timer.
    /// </summary>
    public void Refresh()
    {
        HashSet<string> appeared;
        HashSet<string> disappeared;

        lock (_lock)
        {
            (appeared, disappeared) = ComputeTransitions();
        }

        foreach (var name in appeared) BusinessSoftwareDetected?.Invoke(this, name);
        foreach (var name in disappeared) BusinessSoftwareClosed?.Invoke(this, name);
    }

    private (HashSet<string> appeared, HashSet<string> disappeared) ComputeTransitions()
    {
        var running = _provider.GetRunningProcessNames();
        var current = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in running)
        {
            if (_watched.Contains(name)) current.Add(name);
        }

        var appeared = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
        appeared.ExceptWith(_lastDetected);

        var disappeared = new HashSet<string>(_lastDetected, StringComparer.OrdinalIgnoreCase);
        disappeared.ExceptWith(current);

        _lastDetected = current;
        return (appeared, disappeared);
    }

    /// <inheritdoc />
    public void Dispose() => Stop();
}

using Avalonia.Threading;
using EasySave.Services;

namespace EasySave.UI.Services;

public sealed class BusinessWatcherService : IBusinessSoftwareSignals, IDisposable
{
    private BusinessSoftwareDetector? _detector;
    private bool _disposed;
    // Latched on the first OnDetected after the last Gone, reset on Gone.
    // BusinessSoftwareDetector raises BusinessSoftwareClosed once per
    // disappeared process; if N processes close in the same poll tick we
    // would otherwise emit N Gone events (and N BusinessSoftwareAutoResumed
    // log entries downstream). The latch guarantees a single trailing-edge
    // notification per "any watched running" -> "none running" transition,
    // matching the IBusinessSoftwareSignals.BusinessSoftwareGone contract.
    private bool _anyRunning;

    // Raised on the UI thread when a watched process appears.
    public event EventHandler<string>? BusinessSoftwareDetected;

    // Raised on the UI thread when all watched processes are gone.
    public event EventHandler? BusinessSoftwareGone;

    public void Start()
    {
        var softwareList = AppConfig.Instance.Settings.BusinessSoftware;

        // BusinessSoftwareDetector matches process names without extension.
        var processNames = softwareList
            .Select(s => Path.GetFileNameWithoutExtension(s))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        _detector = new BusinessSoftwareDetector(new SystemProcessProvider(), processNames!);
        _detector.BusinessSoftwareDetected += OnDetected;
        _detector.BusinessSoftwareClosed += OnClosed;
        _detector.Start();
    }

    public void Stop() => _detector?.Stop();

    /// <summary>
    /// True when at least one watched business software is currently running.
    /// Polled by SchedulerDispatchService.Tick to skip dispatch — the
    /// edge-triggered Detected/Gone events alone are not sufficient because
    /// they don't fire when a process is already running at watcher startup.
    /// </summary>
    public bool IsBusinessSoftwareRunning => _detector?.IsAnyBusinessSoftwareRunning == true;

    private void OnDetected(object? sender, string softwareName)
    {
        // Latch so the next "all gone" fires exactly once even if multiple
        // processes close in the same poll tick.
        _anyRunning = true;
        Dispatcher.UIThread.Post(() => BusinessSoftwareDetected?.Invoke(this, softwareName));
    }

    private void OnClosed(object? sender, string softwareName)
    {
        // Fire Gone only on the trailing edge: all watched processes are
        // gone AND we previously latched _anyRunning. _anyRunning gates
        // against the N-times race where the detector raises Closed once
        // per disappeared process at the same poll tick.
        if (!_anyRunning) return;
        if (_detector is { IsAnyBusinessSoftwareRunning: false })
        {
            _anyRunning = false;
            Dispatcher.UIThread.Post(() => BusinessSoftwareGone?.Invoke(this, EventArgs.Empty));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_detector is not null)
        {
            _detector.BusinessSoftwareDetected -= OnDetected;
            _detector.BusinessSoftwareClosed -= OnClosed;
            _detector.Dispose();
        }
    }
}

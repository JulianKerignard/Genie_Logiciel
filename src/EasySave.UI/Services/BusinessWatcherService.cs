using Avalonia.Threading;
using EasySave.Services;

namespace EasySave.UI.Services;

public sealed class BusinessWatcherService : IDisposable
{
    private BusinessSoftwareDetector? _detector;
    private bool _disposed;

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

    private void OnDetected(object? sender, string softwareName) =>
        Dispatcher.UIThread.Post(() => BusinessSoftwareDetected?.Invoke(this, softwareName));

    private void OnClosed(object? sender, string softwareName)
    {
        // Fire GoneEvent only when no more watched processes are running.
        if (_detector is { IsAnyBusinessSoftwareRunning: false })
            Dispatcher.UIThread.Post(() => BusinessSoftwareGone?.Invoke(this, EventArgs.Empty));
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

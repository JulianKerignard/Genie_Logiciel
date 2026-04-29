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
        // Guard against a second Start() — without this, the previous detector
        // (and its System.Threading.Timer) would leak: the field is overwritten
        // and the original instance becomes unreachable while still pumping
        // ticks until the GC catches it.
        if (_detector is not null)
            return;

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

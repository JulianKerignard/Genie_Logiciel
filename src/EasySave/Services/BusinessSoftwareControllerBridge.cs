using EasyLog;

namespace EasySave.Services;

// Wires the business-software watcher to the v3 IJobController. When a
// configured process appears (Word / Outlook / calc.exe / …), every job
// currently running in the parallel orchestrator is paused at its next
// file boundary; when the last watched process is gone, every paused job
// resumes automatically. Mirror of the CdC v3 rule:
//
//   « Si le logiciel détecte le fonctionnement d'un logiciel métier, il
//     doit obligatoirement mettre en pause les travaux. Celles-ci
//     redémarrent automatiquement dès que le logiciel métier est arrêté. »
//
// Lives in the engine layer so the UI layer is not on the critical path
// (a headless console deployment can wire this bridge without the UI
// project). The bridge is event-driven only — it never polls anything
// itself, the supplied IBusinessSoftwareSignals owns the polling.
public sealed class BusinessSoftwareControllerBridge : IDisposable
{
    private readonly IBusinessSoftwareSignals _signals;
    private readonly IJobController _controller;
    private readonly IDailyLogger _logger;
    private bool _started;
    private bool _disposed;

    public BusinessSoftwareControllerBridge(
        IBusinessSoftwareSignals signals,
        IJobController controller,
        IDailyLogger logger)
    {
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(logger);
        _signals = signals;
        _controller = controller;
        _logger = logger;
    }

    public void Start()
    {
        if (_started || _disposed) return;
        _started = true;
        _signals.BusinessSoftwareDetected += OnDetected;
        _signals.BusinessSoftwareGone += OnGone;
    }

    private void OnDetected(object? sender, string softwareName)
    {
        // PauseAll is idempotent per the orchestrator contract (Pause on
        // an already-paused job is a no-op), so a transient flicker of
        // the watched process name across two pollings is safe.
        _controller.PauseAll();
        _logger.Append(new LogEntry
        {
            Timestamp = DateTimeOffset.Now.ToString("o"),
            JobName = string.Empty,
            SourceFile = softwareName ?? string.Empty,
            TargetFile = string.Empty,
            FileSize = 0,
            FileTransferTimeMs = 0,
            EventType = LogEvent.BusinessSoftwareAutoPaused,
        });
    }

    private void OnGone(object? sender, EventArgs _)
    {
        _controller.ResumeAll();
        _logger.Append(new LogEntry
        {
            Timestamp = DateTimeOffset.Now.ToString("o"),
            JobName = string.Empty,
            SourceFile = string.Empty,
            TargetFile = string.Empty,
            FileSize = 0,
            FileTransferTimeMs = 0,
            EventType = LogEvent.BusinessSoftwareAutoResumed,
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_started)
        {
            _signals.BusinessSoftwareDetected -= OnDetected;
            _signals.BusinessSoftwareGone -= OnGone;
        }
    }
}

using EasyLog;
using EasySave.Services;

namespace EasySave.Tests.V2;

public class BusinessSoftwareControllerBridgeTests
{
    // Stand-in for BusinessWatcherService that exposes only the events the
    // bridge cares about. Lets the test trigger appear / gone transitions
    // synchronously without standing up a real polling loop or a fake
    // IProcessProvider.
    private sealed class StubSignals : IBusinessSoftwareSignals
    {
        public event EventHandler<string>? BusinessSoftwareDetected;
        public event EventHandler? BusinessSoftwareGone;

        public void RaiseDetected(string name) =>
            BusinessSoftwareDetected?.Invoke(this, name);

        public void RaiseGone() =>
            BusinessSoftwareGone?.Invoke(this, EventArgs.Empty);
    }

    private sealed class RecordingController : IJobController
    {
        public int PauseAllCalls { get; private set; }
        public int ResumeAllCalls { get; private set; }
        public int StopAllCalls { get; private set; }
        public void Pause(string jobName) { }
        public void Resume(string jobName) { }
        public void Stop(string jobName) { }
        public void PauseAll() => PauseAllCalls++;
        public void ResumeAll() => ResumeAllCalls++;
        public void StopAll() => StopAllCalls++;
    }

    private sealed class RecordingLogger : IDailyLogger
    {
        public List<LogEntry> Entries { get; } = new();
        public void Append(LogEntry entry) => Entries.Add(entry);
    }

    [Fact]
    public void Detected_CallsPauseAllAndLogsBusinessSoftwareAutoPaused()
    {
        var signals = new StubSignals();
        var controller = new RecordingController();
        var logger = new RecordingLogger();
        using var bridge = new BusinessSoftwareControllerBridge(signals, controller, logger);
        bridge.Start();

        signals.RaiseDetected("calc");

        Assert.Equal(1, controller.PauseAllCalls);
        Assert.Equal(0, controller.ResumeAllCalls);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogEvent.BusinessSoftwareAutoPaused, entry.EventType);
        Assert.Equal("calc", entry.SourceFile);
    }

    [Fact]
    public void Gone_CallsResumeAllAndLogsBusinessSoftwareAutoResumed()
    {
        var signals = new StubSignals();
        var controller = new RecordingController();
        var logger = new RecordingLogger();
        using var bridge = new BusinessSoftwareControllerBridge(signals, controller, logger);
        bridge.Start();

        signals.RaiseGone();

        Assert.Equal(0, controller.PauseAllCalls);
        Assert.Equal(1, controller.ResumeAllCalls);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogEvent.BusinessSoftwareAutoResumed, entry.EventType);
    }

    [Fact]
    public void FullCycle_DetectedThenGone_PausesThenResumes()
    {
        var signals = new StubSignals();
        var controller = new RecordingController();
        var logger = new RecordingLogger();
        using var bridge = new BusinessSoftwareControllerBridge(signals, controller, logger);
        bridge.Start();

        signals.RaiseDetected("calc");
        signals.RaiseGone();

        Assert.Equal(1, controller.PauseAllCalls);
        Assert.Equal(1, controller.ResumeAllCalls);
        Assert.Equal(2, logger.Entries.Count);
        Assert.Equal(LogEvent.BusinessSoftwareAutoPaused, logger.Entries[0].EventType);
        Assert.Equal(LogEvent.BusinessSoftwareAutoResumed, logger.Entries[1].EventType);
    }

    [Fact]
    public void Start_IsIdempotent_DoesNotDoubleSubscribe()
    {
        // A double-Start must not register the handlers twice — otherwise
        // each appear/gone tick would PauseAll/ResumeAll and emit a log
        // entry twice.
        var signals = new StubSignals();
        var controller = new RecordingController();
        var logger = new RecordingLogger();
        using var bridge = new BusinessSoftwareControllerBridge(signals, controller, logger);
        bridge.Start();
        bridge.Start();

        signals.RaiseDetected("calc");

        Assert.Equal(1, controller.PauseAllCalls);
        Assert.Single(logger.Entries);
    }

    [Fact]
    public void Dispose_UnsubscribesFromSignals()
    {
        var signals = new StubSignals();
        var controller = new RecordingController();
        var logger = new RecordingLogger();
        var bridge = new BusinessSoftwareControllerBridge(signals, controller, logger);
        bridge.Start();
        bridge.Dispose();

        signals.RaiseDetected("calc");

        Assert.Equal(0, controller.PauseAllCalls);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void Constructor_NullArguments_Throw()
    {
        var signals = new StubSignals();
        var controller = new RecordingController();
        var logger = new RecordingLogger();

        Assert.Throws<ArgumentNullException>(() =>
            new BusinessSoftwareControllerBridge(null!, controller, logger));
        Assert.Throws<ArgumentNullException>(() =>
            new BusinessSoftwareControllerBridge(signals, null!, logger));
        Assert.Throws<ArgumentNullException>(() =>
            new BusinessSoftwareControllerBridge(signals, controller, null!));
    }
}

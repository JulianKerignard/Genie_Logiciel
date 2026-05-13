namespace EasySave.Services;

// Minimal events surface that the business-software watcher exposes for
// consumers that don't care about polling or process providers — just
// about the appear / gone transitions. Lets engine-layer wiring
// (BusinessSoftwareControllerBridge) depend on the abstraction without
// pulling in the UI layer's BusinessWatcherService directly, and keeps
// the bridge unit-testable with a tiny stub.
public interface IBusinessSoftwareSignals
{
    // Raised the first time any watched process appears between two polls.
    // The string argument is the canonical process name without .exe.
    event EventHandler<string>? BusinessSoftwareDetected;

    // Raised once when the last watched process disappears (no watched
    // process is running anymore). Not raised on every individual close —
    // only on the trailing edge of the "any watched running" predicate.
    event EventHandler? BusinessSoftwareGone;
}

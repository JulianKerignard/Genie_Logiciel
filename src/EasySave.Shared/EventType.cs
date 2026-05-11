namespace EasySave.Shared;

// Discriminator for EventDto: identifies the kind of notification the engine is
// pushing to the remote console. Explicit numeric values keep the JSON payload
// stable across versions.
public enum EventType
{
    JobList = 0,
    JobProgress = 1,
    JobStarted = 2,
    JobPaused = 3,
    JobResumed = 4,
    JobFinished = 5,
    JobFailed = 6,
    Error = 7,
    LogEvent = 8,
    // Audit broadcast: the engine echoes every incoming CommandDto back to
    // ALL connected clients (including the sender) so multi-console setups
    // stay synchronised. JobName carries the targeted job; Message holds
    // "<sourceIp:port> → <Action>" so the receiving UI can display a
    // command history with the origin.
    CommandReceived = 9
}

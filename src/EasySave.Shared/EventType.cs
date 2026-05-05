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
    Error = 7
}

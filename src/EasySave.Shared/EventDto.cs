namespace EasySave.Shared;

// Notification frame pushed by the engine to the remote console.
// Which payload field is populated depends on Type:
//   JobList     -> Jobs is set, others null
//   JobProgress -> Progress is set, JobName mirrors Progress.JobName
//   JobStarted / JobPaused / JobResumed / JobFinished -> JobName is set
//   JobFailed / Error -> JobName (when scoped to a job) and Message are set
public sealed record EventDto(
    EventType Type,
    string? JobName = null,
    JobProgressDto? Progress = null,
    IReadOnlyList<JobProgressDto>? Jobs = null,
    string? Message = null
);

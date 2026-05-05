namespace EasySave.Shared;

// Notification frame pushed by the engine to the remote console. Timestamp is
// the moment the engine emitted the event (server clock); System.Text.Json
// encodes DateTimeOffset as the round-trip ISO-8601 form.
//
// Which payload field is populated depends on Type. To keep a single source of
// truth for the job identity, JobName is ONLY set on events that don't carry a
// per-job snapshot; when Progress is set, JobName must be null and consumers
// must read Progress.JobName.
//   JobList     -> Jobs is set, others null
//   JobProgress -> Progress is set, JobName is null
//   JobStarted / JobPaused / JobResumed / JobFinished -> JobName is set
//   JobFailed   -> JobName is set, Message carries the failure reason
//   LogEvent    -> Message carries the log line; JobName is set when scoped
//   Error       -> Message is set, JobName is set only if scoped to a job
public sealed record EventDto(
    DateTimeOffset Timestamp,
    EventType Type,
    string? JobName = null,
    JobProgressDto? Progress = null,
    IReadOnlyList<JobProgressDto>? Jobs = null,
    string? Message = null
);

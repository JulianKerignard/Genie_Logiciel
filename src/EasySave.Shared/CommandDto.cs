namespace EasySave.Shared;

// Request frame sent by the remote console to the engine over the v3 socket.
// JobName is required for per-job actions (RunJob, PauseJob, ResumeJob, StopJob)
// and ignored for ListJobs.
public sealed record CommandDto(
    CommandType Type,
    string? JobName = null
);

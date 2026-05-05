namespace EasySave.Shared;

// Discriminator for CommandDto: identifies what action the remote console is
// asking the engine to perform. Explicit numeric values keep the JSON payload
// stable across versions.
public enum CommandType
{
    ListJobs = 0,
    RunJob = 1,
    PauseJob = 2,
    ResumeJob = 3,
    StopJob = 4
}

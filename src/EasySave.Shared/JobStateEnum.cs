namespace EasySave.Shared;

// Wire-format job state for the v3 client/server protocol. Numeric values
// are aligned with EasySave.JobState so a numeric round-trip is loss-free:
//   engine.Inactive (0) -> dto.Done    (the engine has no separate completion
//                                       state; Inactive covers both never-run
//                                       and finished jobs)
//   engine.Active   (1) -> dto.Running
//   engine.Paused   (2) -> dto.Paused
public enum JobStateEnum
{
    Done = 0,
    Running = 1,
    Paused = 2
}

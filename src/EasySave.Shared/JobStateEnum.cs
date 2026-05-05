namespace EasySave.Shared;

// Wire-format mirror of EasySave.JobState for the v3 client/server protocol.
// Explicit numeric values protect the JSON payload against future reordering and
// keep parity with the engine's enum so a numeric round-trip is loss-free.
public enum JobStateEnum
{
    Inactive = 0,
    Active = 1,
    Paused = 2
}

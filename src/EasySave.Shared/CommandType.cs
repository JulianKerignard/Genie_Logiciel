namespace EasySave.Shared;

// Discriminator for CommandDto: identifies what action the remote console is
// asking the engine to perform. Play covers both starting an idle/finished job
// and resuming a paused one — the engine inspects the current JobState to
// decide which path to take.
public enum CommandType
{
    Pause = 0,
    Play = 1,
    Stop = 2
}

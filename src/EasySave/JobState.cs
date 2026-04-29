namespace EasySave;

// Runtime state of a backup job, as persisted in the shared state file.
// Explicit numeric values protect the persisted JSON against future reordering.
public enum JobState
{
    Inactive = 0,
    Active = 1,
    // Active job temporarily held because a business software listed in appsettings.json
    // is running. Resume happens when the business software disappears; the human-readable
    // reason is carried separately on StateEntry.PauseReason.
    Paused = 2
}

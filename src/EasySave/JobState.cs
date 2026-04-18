namespace EasySave;

// Runtime state of a backup job, as persisted in the shared state file.
// Explicit numeric values protect the persisted JSON against future reordering.
public enum JobState
{
    Inactive = 0,
    Active = 1
}

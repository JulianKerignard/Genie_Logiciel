namespace EasySave;

/// <summary>
/// Runtime state of a backup job, as persisted in the shared state file.
/// </summary>
public enum JobState
{
    Inactive,
    Active
}

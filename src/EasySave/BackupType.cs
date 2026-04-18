namespace EasySave;

// Type of backup strategy applied to a job.
// Explicit numeric values protect the persisted JSON against future reordering.
public enum BackupType
{
    Full = 0,
    Differential = 1
}

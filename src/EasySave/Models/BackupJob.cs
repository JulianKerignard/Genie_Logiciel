namespace EasySave.Models;

// POCO representing a user-defined backup job (v1.0 spec).
public sealed class BackupJob
{
    public string Name { get; set; } = string.Empty;

    public string SourcePath { get; set; } = string.Empty;

    public string TargetPath { get; set; } = string.Empty;

    public BackupType Type { get; set; } = BackupType.Full;
}

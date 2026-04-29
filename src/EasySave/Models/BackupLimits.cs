namespace EasySave.Models;

/// <summary>
/// Constants imposed by the cahier des charges v1.0 on the backup engine.
/// Centralised here so consumers (services, CLI parser) share a single
/// source of truth and stay in sync if the spec changes.
/// </summary>
public static class BackupLimits
{
    /// <summary>
    /// Maximum number of backup jobs the application accepts at any time.
    /// Cahier v1.0 caps this at 5; v2.0 is expected to lift the limit.
    /// </summary>
    public const int MaxJobs = 5;
}

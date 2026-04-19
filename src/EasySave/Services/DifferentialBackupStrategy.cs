namespace EasySave.Services;

public class DifferentialBackupStrategy : IBackupStrategy
{
    public bool ShouldCopy(FileInfo source, string targetPath) => true; // TODO Phase 3
}

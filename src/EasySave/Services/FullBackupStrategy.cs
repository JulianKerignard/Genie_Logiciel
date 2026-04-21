namespace EasySave.Services;

public sealed class FullBackupStrategy : IBackupStrategy
{
    public bool ShouldCopy(FileInfo source, string targetPath) => true;
}

namespace EasySave.Services;

public class FullBackupStrategy : IBackupStrategy
{
    public bool ShouldCopy(FileInfo source, string targetPath) => true;
}

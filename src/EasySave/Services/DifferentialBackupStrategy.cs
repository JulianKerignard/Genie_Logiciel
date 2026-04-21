namespace EasySave.Services;

public sealed class DifferentialBackupStrategy : IBackupStrategy
{
    public bool ShouldCopy(FileInfo source, string targetPath)
    {
        if (!File.Exists(targetPath)) return true;
        var target = new FileInfo(targetPath);
        return source.Length != target.Length
            || source.LastWriteTimeUtc > target.LastWriteTimeUtc;
    }
}

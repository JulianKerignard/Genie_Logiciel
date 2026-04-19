namespace EasySave.Services;

public interface IBackupStrategy
{
    bool ShouldCopy(FileInfo source, string targetPath);
}

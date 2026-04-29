namespace EasySave.Services;

/// <summary>
/// Differential backup strategy: re-copies a file only when the source is
/// newer than the previous backup.
/// <para>
/// v2.0 dropped the size comparison so encrypted targets work too: an
/// encrypted file's size never matches its plaintext source, so a size-based
/// gate would re-encrypt every run. The contract is now "trust the source
/// modification time", and <see cref="BackupManager"/> guarantees this works
/// by aligning the target's <c>LastWriteTimeUtc</c> on the source's after
/// every successful copy or encryption.
/// </para>
/// </summary>
public sealed class DifferentialBackupStrategy : IBackupStrategy
{
    /// <inheritdoc />
    public bool ShouldCopy(FileInfo source, string targetPath)
    {
        if (!File.Exists(targetPath)) return true;
        var target = new FileInfo(targetPath);
        return source.LastWriteTimeUtc > target.LastWriteTimeUtc;
    }
}

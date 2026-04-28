using EasySave.Services;

namespace EasySave.Tests;

public class DifferentialBackupStrategyTests : IDisposable
{
    private readonly string _tempDir;
    private readonly DifferentialBackupStrategy _strategy = new();

    public DifferentialBackupStrategyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "diff-strategy-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Diff_NewFile_ShouldCopy()
    {
        var sourcePath = Path.Combine(_tempDir, "source.txt");
        File.WriteAllText(sourcePath, "data");
        var missingTarget = Path.Combine(_tempDir, "missing.txt");

        Assert.True(_strategy.ShouldCopy(new FileInfo(sourcePath), missingTarget));
    }

    [Fact]
    public void Diff_SameSizeSameDate_ShouldNotCopy()
    {
        var sourcePath = Path.Combine(_tempDir, "source.txt");
        var targetPath = Path.Combine(_tempDir, "target.txt");
        File.WriteAllText(sourcePath, "data");
        File.Copy(sourcePath, targetPath);
        File.SetLastWriteTimeUtc(targetPath, File.GetLastWriteTimeUtc(sourcePath));

        Assert.False(_strategy.ShouldCopy(new FileInfo(sourcePath), targetPath));
    }

    [Fact]
    public void Diff_DifferentSizeSameDate_DoesNotCopy_EncryptedTargetCase()
    {
        // v2.0 contract: encrypted targets do not match the plaintext source's
        // size, so the strategy must trust the modification time only. When
        // the source has not been touched since the last backup, the file is
        // skipped even if the target's size differs (because it was encrypted).
        var sourcePath = Path.Combine(_tempDir, "secret.pdf");
        var targetPath = Path.Combine(_tempDir, "secret.pdf");
        Directory.CreateDirectory(Path.Combine(_tempDir, "tgt"));
        targetPath = Path.Combine(_tempDir, "tgt", "secret.pdf");
        File.WriteAllText(sourcePath, "plaintext");
        File.WriteAllText(targetPath, "encrypted-bytes-much-longer-than-source");
        File.SetLastWriteTimeUtc(targetPath, File.GetLastWriteTimeUtc(sourcePath));

        Assert.False(_strategy.ShouldCopy(new FileInfo(sourcePath), targetPath));
    }

    [Fact]
    public void Diff_SourceNewer_ShouldCopy()
    {
        var sourcePath = Path.Combine(_tempDir, "source.txt");
        var targetPath = Path.Combine(_tempDir, "target.txt");
        File.WriteAllText(sourcePath, "data");
        File.WriteAllText(targetPath, "data");
        File.SetLastWriteTimeUtc(targetPath, DateTime.UtcNow.AddHours(-1));

        Assert.True(_strategy.ShouldCopy(new FileInfo(sourcePath), targetPath));
    }
}

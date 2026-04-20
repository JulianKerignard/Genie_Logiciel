using EasySave.Services;

namespace EasySave.Tests;

public class FullBackupStrategyTests : IDisposable
{
    private readonly string _tempDir;

    public FullBackupStrategyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "full-strategy-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void ShouldCopy_TargetMissing_ReturnsTrue()
    {
        var sourcePath = Path.Combine(_tempDir, "source.txt");
        File.WriteAllText(sourcePath, "hello");
        var source = new FileInfo(sourcePath);
        var missingTarget = Path.Combine(_tempDir, "missing.txt");

        IBackupStrategy strategy = new FullBackupStrategy();

        Assert.True(strategy.ShouldCopy(source, missingTarget));
    }

    [Fact]
    public void ShouldCopy_IdenticalTarget_StillReturnsTrue()
    {
        var sourcePath = Path.Combine(_tempDir, "source.txt");
        var targetPath = Path.Combine(_tempDir, "target.txt");
        File.WriteAllText(sourcePath, "data");
        File.WriteAllText(targetPath, "data");

        IBackupStrategy strategy = new FullBackupStrategy();

        Assert.True(strategy.ShouldCopy(new FileInfo(sourcePath), targetPath));
    }
}

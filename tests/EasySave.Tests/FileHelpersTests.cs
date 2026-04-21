using EasySave.Services;

namespace EasySave.Tests;

// Unit tests for the atomic write helper that protects StateTracker, JobRepository
// and future callers from partial writes and cross-process temp-file collisions.
public class FileHelpersTests : IDisposable
{
    private readonly string _tempDir;

    public FileHelpersTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "easysave-tests-" + Guid.NewGuid().ToString("N"));
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
    public void WriteAllTextAtomic_Success_WritesExpectedContent()
    {
        var path = Path.Combine(_tempDir, "payload.json");

        FileHelpers.WriteAllTextAtomic(path, "hello world");

        Assert.Equal("hello world", File.ReadAllText(path));
    }

    [Fact]
    public void WriteAllTextAtomic_Success_LeavesNoTempOrphan()
    {
        var path = Path.Combine(_tempDir, "payload.json");

        FileHelpers.WriteAllTextAtomic(path, "content");

        Assert.Empty(Directory.GetFiles(_tempDir, "*.tmp"));
    }

    [Fact]
    public void WriteAllTextAtomic_MoveFails_DeletesTempFileAndRethrows()
    {
        // Using a directory as the target path forces File.Move to fail after the temp
        // file has been written, which is exactly the code path that must clean up.
        var target = Path.Combine(_tempDir, "target-dir");
        Directory.CreateDirectory(target);

        Assert.ThrowsAny<Exception>(() => FileHelpers.WriteAllTextAtomic(target, "content"));

        Assert.Empty(Directory.GetFiles(_tempDir, "*.tmp"));
    }

    [Fact]
    public void WriteAllTextAtomic_ConcurrentCalls_DoNotCollideOnTempName()
    {
        // Ten threads write to the same target. With a fixed ".tmp" suffix this would
        // race and surface as IOException or interleaved content; with unique GUID
        // names every call owns its own temp file.
        var path = Path.Combine(_tempDir, "shared.json");
        const int threadCount = 10;

        var threads = new Thread[threadCount];
        var exceptions = new List<Exception>();
        var exceptionsLock = new object();

        for (var i = 0; i < threadCount; i++)
        {
            var payload = $"thread-{i}";
            threads[i] = new Thread(() =>
            {
                try { FileHelpers.WriteAllTextAtomic(path, payload); }
                catch (Exception ex)
                {
                    lock (exceptionsLock) { exceptions.Add(ex); }
                }
            });
        }

        foreach (var t in threads) t.Start();
        foreach (var t in threads) t.Join();

        Assert.Empty(exceptions);
        Assert.True(File.Exists(path));
        Assert.Empty(Directory.GetFiles(_tempDir, "*.tmp"));
    }
}

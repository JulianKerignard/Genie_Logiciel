using System.Text.Json;
using EasySave.Services;

namespace EasySave.Tests;

[Collection("StateCollection")]
public class IStateRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _statePath;
    private readonly string _configPath;

    public IStateRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "state-repo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _statePath = Path.Combine(_tempDir, "state.json");

        // Point AppConfig at the temp directory so StateTracker uses our isolated state file.
        _configPath = Path.Combine(_tempDir, "appsettings.json");
        File.WriteAllText(_configPath, JsonSerializer.Serialize(new { StateFilePath = _statePath }));
        AppConfig.Load(_configPath);
    }

    public void Dispose()
    {
        StateTracker.Instance.FlushNow();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void UpdateJob_CreatesMinimalEntry_WhenJobIsUnknown()
    {
        IStateRepository repo = StateTracker.Instance;

        repo.UpdateJob("newjob", JobState.Active);
        StateTracker.Instance.FlushNow();

        var entry = repo.GetJob("newjob");
        Assert.NotNull(entry);
        Assert.Equal(JobState.Active, entry.State);
    }

    [Fact]
    public void UpdateJob_UpdatesStateField_WhenEntryAlreadyExists()
    {
        IStateRepository repo = StateTracker.Instance;

        repo.UpdateJob("job-update", JobState.Active);
        repo.UpdateJob("job-update", JobState.Paused);
        StateTracker.Instance.FlushNow();

        Assert.Equal(JobState.Paused, repo.GetJob("job-update")!.State);
    }

    [Fact]
    public void GetAll_ReturnsEmpty_WhenStateFileAbsent()
    {
        IStateRepository repo = StateTracker.Instance;

        Assert.False(File.Exists(_statePath));
        Assert.Empty(repo.GetAll());
    }

    [Fact]
    public void GetJob_ReturnsNull_ForUnknownName()
    {
        IStateRepository repo = StateTracker.Instance;

        Assert.Null(repo.GetJob("no-such-job-" + Guid.NewGuid()));
    }

    [Fact]
    public void RemoveJob_IsNoOp_WhenEntryAbsent()
    {
        IStateRepository repo = StateTracker.Instance;

        var ex = Record.Exception(() => repo.RemoveJob("ghost-" + Guid.NewGuid()));
        Assert.Null(ex);
    }

    [Fact]
    public async Task UpdateJob_ConcurrentUpdates_ProducesConsistentFile()
    {
        // 4 jobs × 100 updates each, all concurrent. The file must contain exactly
        // 4 entries (one per job) with no truncation or lost-write corruption.
        IStateRepository repo = StateTracker.Instance;
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var jobs = new[] { $"a-{suffix}", $"b-{suffix}", $"c-{suffix}", $"d-{suffix}" };

        await Task.WhenAll(jobs.Select(name => Task.Run(() =>
        {
            for (var i = 0; i < 100; i++)
                repo.UpdateJob(name, i % 2 == 0 ? JobState.Active : JobState.Paused);
        })));

        StateTracker.Instance.FlushNow();

        // In-memory view must be consistent.
        var all = repo.GetAll();
        Assert.Equal(jobs.Length, all.Count(e => jobs.Contains(e.Name)));

        // File must be valid JSON with one entry per job — no truncation.
        var json = File.ReadAllText(_statePath);
        var fileEntries = JsonSerializer.Deserialize<List<StateEntry>>(json)!;
        Assert.All(jobs, job => Assert.Contains(fileEntries, e => e.Name == job));
    }

    [Fact]
    public void UpdateJob_ProcessSurvives_WhenTimerFlushFails()
    {
        // Place a regular file at the path EnsureDirectoryExists would create as a
        // directory — any atomic write attempt throws IOException deterministically
        // on all platforms (no OS file-locking required).
        var obstacle = Path.Combine(_tempDir, "obstacle-a");
        File.WriteAllText(obstacle, "block");
        var badStatePath = Path.Combine(obstacle, "state.json");

        var badConfig = Path.Combine(_tempDir, "bad-appsettings-a.json");
        File.WriteAllText(badConfig, JsonSerializer.Serialize(new { StateFilePath = badStatePath }));
        AppConfig.Load(badConfig);

        IStateRepository repo = StateTracker.Instance;
        repo.UpdateJob("survivor", JobState.Active);

        // Wait for the 200 ms throttled timer to fire and swallow the IOException.
        // If FlushFromTimer had no try/catch, the test host would crash here and the
        // assertion below would never execute — test failure proves the regression.
        Thread.Sleep(400);

        // The fact that execution reaches this line proves the process survived.
        // Restore the good config so FlushNow can repair the dirty cache.
        AppConfig.Load(_configPath);
        StateTracker.Instance.FlushNow();

        var entries = JsonSerializer.Deserialize<List<StateEntry>>(File.ReadAllText(_statePath))!;
        Assert.Contains(entries, e => e.Name == "survivor");
    }

    [Fact]
    public void FlushNow_RepairsCache_AfterTimerFlushFailed()
    {
        // Same bad-path trick as above to make the timer flush throw IOException.
        var obstacle = Path.Combine(_tempDir, "obstacle-b");
        File.WriteAllText(obstacle, "block");
        var badStatePath = Path.Combine(obstacle, "state.json");

        var badConfig = Path.Combine(_tempDir, "bad-appsettings-b.json");
        File.WriteAllText(badConfig, JsonSerializer.Serialize(new { StateFilePath = badStatePath }));
        AppConfig.Load(badConfig);

        IStateRepository repo = StateTracker.Instance;
        repo.UpdateJob("job-repair", JobState.Active);

        // Let the timer fire and fail — _cacheDirty must remain true because the
        // write did not succeed (defect 2 fix: _cacheDirty = false only after write).
        Thread.Sleep(400);

        // Switch back to the valid path: FlushNow must detect _cacheDirty == true
        // and successfully write the entry that the timer flush missed.
        AppConfig.Load(_configPath);
        StateTracker.Instance.FlushNow();

        Assert.True(File.Exists(_statePath));
        var entries = JsonSerializer.Deserialize<List<StateEntry>>(File.ReadAllText(_statePath))!;
        Assert.Contains(entries, e => e.Name == "job-repair" && e.State == JobState.Active);
    }
}

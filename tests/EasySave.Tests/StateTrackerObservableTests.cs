using System.ComponentModel;
using System.Text.Json;
using EasySave;
using EasySave.Models;
using EasySave.Services;

namespace EasySave.Tests;

[Collection("StateCollection")]
public class StateTrackerObservableTests : IDisposable
{
    private readonly string _tempDir;

    public StateTrackerObservableTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "statetracker-obs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        var configPath = Path.Combine(_tempDir, "appsettings.json");
        var payload = new { StateFilePath = Path.Combine(_tempDir, "state.json") };
        File.WriteAllText(configPath, JsonSerializer.Serialize(payload));
        AppConfig.Load(configPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private static StateEntry MakeEntry(string name, int total, int remaining, string current)
    {
        return new StateEntry
        {
            Name = name,
            State = JobState.Active,
            TotalFilesEligible = total,
            FilesRemaining = remaining,
            CurrentSource = current
        };
    }

    [Fact]
    public void Update_ExposesJobProgressInJobsMap()
    {
        var jobName = "obs-" + Guid.NewGuid().ToString("N");

        StateTracker.Instance.Update(MakeEntry(jobName, 10, 4, @"\\share\file.bin"));

        Assert.True(StateTracker.Instance.Jobs.ContainsKey(jobName));
        var progress = StateTracker.Instance.Jobs[jobName];
        Assert.Equal(@"\\share\file.bin", progress.CurrentFile);
        Assert.Equal(4, progress.FilesRemaining);
        Assert.Equal(60.0, progress.Percent);
    }

    [Fact]
    public void Update_ReusesSameJobProgressInstance()
    {
        var jobName = "obs-" + Guid.NewGuid().ToString("N");

        StateTracker.Instance.Update(MakeEntry(jobName, 10, 9, @"\\share\a"));
        var first = StateTracker.Instance.Jobs[jobName];

        StateTracker.Instance.Update(MakeEntry(jobName, 10, 5, @"\\share\b"));
        var second = StateTracker.Instance.Jobs[jobName];

        Assert.Same(first, second);
    }

    [Fact]
    public void Update_RaisesPropertyChangedForChangedFields()
    {
        var jobName = "obs-" + Guid.NewGuid().ToString("N");
        StateTracker.Instance.Update(MakeEntry(jobName, 10, 9, @"\\share\a"));
        var progress = StateTracker.Instance.Jobs[jobName];

        var changed = new List<string>();
        PropertyChangedEventHandler handler = (_, e) => changed.Add(e.PropertyName!);
        progress.PropertyChanged += handler;
        try
        {
            StateTracker.Instance.Update(MakeEntry(jobName, 10, 4, @"\\share\b"));
        }
        finally
        {
            progress.PropertyChanged -= handler;
        }

        Assert.Contains(nameof(JobProgress.CurrentFile), changed);
        Assert.Contains(nameof(JobProgress.FilesRemaining), changed);
        Assert.Contains(nameof(JobProgress.Percent), changed);
    }

    [Fact]
    public void Update_DoesNotRaisePropertyChangedWhenValueUnchanged()
    {
        var jobName = "obs-" + Guid.NewGuid().ToString("N");
        StateTracker.Instance.Update(MakeEntry(jobName, 10, 4, @"\\share\a"));
        var progress = StateTracker.Instance.Jobs[jobName];

        var changed = new List<string>();
        PropertyChangedEventHandler handler = (_, e) => changed.Add(e.PropertyName!);
        progress.PropertyChanged += handler;
        try
        {
            StateTracker.Instance.Update(MakeEntry(jobName, 10, 4, @"\\share\a"));
        }
        finally
        {
            progress.PropertyChanged -= handler;
        }

        Assert.Empty(changed);
    }

    [Fact]
    public void Update_RaisesJobProgressChanged()
    {
        var jobName = "obs-" + Guid.NewGuid().ToString("N");
        StateEntry? received = null;
        EventHandler<StateEntry> handler = (_, entry) => received = entry;
        StateTracker.Instance.JobProgressChanged += handler;
        try
        {
            StateTracker.Instance.Update(MakeEntry(jobName, 10, 4, @"\\share\f"));
        }
        finally
        {
            StateTracker.Instance.JobProgressChanged -= handler;
        }

        Assert.NotNull(received);
        Assert.Equal(jobName, received!.Name);
        Assert.Equal(4, received.FilesRemaining);
    }
}

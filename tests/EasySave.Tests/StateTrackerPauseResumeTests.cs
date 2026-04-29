using System.Text.Json;
using EasySave;
using EasySave.Services;

namespace EasySave.Tests;

[Collection("StateCollection")]
public class StateTrackerPauseResumeTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _stateFilePath;

    public StateTrackerPauseResumeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "statetracker-pause-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _stateFilePath = Path.Combine(_tempDir, "state.json");

        var configPath = Path.Combine(_tempDir, "appsettings.json");
        var payload = new { StateFilePath = _stateFilePath };
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

    private List<StateEntry> ReadStates() =>
        JsonSerializer.Deserialize<List<StateEntry>>(File.ReadAllText(_stateFilePath))!;

    [Fact]
    public void Pause_StoresReason_AndFlipsStateToPaused()
    {
        var name = "pause-" + Guid.NewGuid().ToString("N");
        StateTracker.Instance.Update(new StateEntry { Name = name, State = JobState.Active });

        StateTracker.Instance.Pause(name, "BusinessSoftwareDetected: calc.exe");

        var entry = ReadStates().Single(s => s.Name == name);
        Assert.Equal(JobState.Paused, entry.State);
        Assert.Equal("BusinessSoftwareDetected: calc.exe", entry.PauseReason);
    }

    [Fact]
    public void Resume_FlipsBackToActive_AndClearsReason()
    {
        var name = "resume-" + Guid.NewGuid().ToString("N");
        StateTracker.Instance.Update(new StateEntry { Name = name, State = JobState.Active });
        StateTracker.Instance.Pause(name, "BusinessSoftwareDetected: calc.exe");

        StateTracker.Instance.Resume(name);

        var entry = ReadStates().Single(s => s.Name == name);
        Assert.Equal(JobState.Active, entry.State);
        Assert.Equal(string.Empty, entry.PauseReason);
    }

    [Fact]
    public void Pause_IsCaseInsensitive()
    {
        var name = "CaseTest-" + Guid.NewGuid().ToString("N");
        StateTracker.Instance.Update(new StateEntry { Name = name, State = JobState.Active });

        StateTracker.Instance.Pause(name.ToUpperInvariant(), "BusinessSoftwareDetected: calc.exe");

        Assert.Equal(JobState.Paused, ReadStates().Single(s => s.Name == name).State);
    }

    [Fact]
    public void Pause_IsNoOp_WhenJobAbsent()
    {
        var existing = "kept-" + Guid.NewGuid().ToString("N");
        StateTracker.Instance.Update(new StateEntry { Name = existing, State = JobState.Active });
        var beforeContent = File.ReadAllText(_stateFilePath);

        StateTracker.Instance.Pause("does-not-exist-" + Guid.NewGuid().ToString("N"), "any");

        Assert.Equal(beforeContent, File.ReadAllText(_stateFilePath));
    }

    [Fact]
    public void Resume_IsNoOp_WhenJobNotPaused()
    {
        // Resuming an Active job is a true no-op: state.json is not rewritten.
        var name = "noop-" + Guid.NewGuid().ToString("N");
        StateTracker.Instance.Update(new StateEntry { Name = name, State = JobState.Active });
        var beforeContent = File.ReadAllText(_stateFilePath);

        StateTracker.Instance.Resume(name);

        Assert.Equal(beforeContent, File.ReadAllText(_stateFilePath));
        var entry = ReadStates().Single(s => s.Name == name);
        Assert.Equal(JobState.Active, entry.State);
        Assert.Equal(string.Empty, entry.PauseReason);
    }

    [Fact]
    public void Pause_IsNoOp_WhenJobInactive()
    {
        // An Inactive (finished) job must not be flipped to Paused.
        var name = "inactive-" + Guid.NewGuid().ToString("N");
        StateTracker.Instance.Update(new StateEntry { Name = name, State = JobState.Inactive });
        var beforeContent = File.ReadAllText(_stateFilePath);

        StateTracker.Instance.Pause(name, "BusinessSoftwareDetected: calc.exe");

        Assert.Equal(beforeContent, File.ReadAllText(_stateFilePath));
        Assert.Equal(JobState.Inactive, ReadStates().Single(s => s.Name == name).State);
    }

    [Fact]
    public void Pause_RejectsBlankReason()
    {
        var name = "blank-" + Guid.NewGuid().ToString("N");
        StateTracker.Instance.Update(new StateEntry { Name = name, State = JobState.Active });

        Assert.Throws<ArgumentException>(() => StateTracker.Instance.Pause(name, ""));
        Assert.Throws<ArgumentException>(() => StateTracker.Instance.Pause(name, "   "));
    }

    [Fact]
    public void Resume_RaisesJobProgressChanged()
    {
        var name = "evt-resume-" + Guid.NewGuid().ToString("N");
        StateTracker.Instance.Update(new StateEntry { Name = name, State = JobState.Active });
        StateTracker.Instance.Pause(name, "BusinessSoftwareDetected: calc.exe");

        StateEntry? received = null;
        EventHandler<StateEntry> handler = (_, e) => received = e;
        StateTracker.Instance.JobProgressChanged += handler;
        try
        {
            StateTracker.Instance.Resume(name);
        }
        finally
        {
            StateTracker.Instance.JobProgressChanged -= handler;
        }

        Assert.NotNull(received);
        Assert.Equal(JobState.Active, received!.State);
        Assert.Equal(string.Empty, received.PauseReason);
    }

    [Fact]
    public void Pause_RaisesJobProgressChanged()
    {
        var name = "evt-" + Guid.NewGuid().ToString("N");
        StateTracker.Instance.Update(new StateEntry { Name = name, State = JobState.Active });

        StateEntry? received = null;
        EventHandler<StateEntry> handler = (_, e) => received = e;
        StateTracker.Instance.JobProgressChanged += handler;
        try
        {
            StateTracker.Instance.Pause(name, "BusinessSoftwareDetected: calc.exe");
        }
        finally
        {
            StateTracker.Instance.JobProgressChanged -= handler;
        }

        Assert.NotNull(received);
        Assert.Equal(JobState.Paused, received!.State);
        Assert.Equal("BusinessSoftwareDetected: calc.exe", received.PauseReason);
    }
}

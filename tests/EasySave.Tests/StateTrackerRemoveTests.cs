using System.Text.Json;
using EasySave;
using EasySave.Services;

namespace EasySave.Tests;

[Collection("StateCollection")]
public class StateTrackerRemoveTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _stateFilePath;

    public StateTrackerRemoveTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "statetracker-remove-" + Guid.NewGuid().ToString("N"));
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

    private List<StateEntry> ReadStates()
    {
        var json = File.ReadAllText(_stateFilePath);
        return JsonSerializer.Deserialize<List<StateEntry>>(json) ?? new List<StateEntry>();
    }

    [Fact]
    public void Remove_RemovesEntry_WhenPresent()
    {
        var name = "rm-" + Guid.NewGuid().ToString("N");
        StateTracker.Instance.Update(new StateEntry { Name = name, State = JobState.Inactive });
        Assert.Contains(ReadStates(), s => s.Name == name);

        StateTracker.Instance.Remove(name);

        Assert.DoesNotContain(ReadStates(), s => s.Name == name);
    }

    [Fact]
    public void Remove_IsNoOp_WhenAbsent()
    {
        var kept = "keep-" + Guid.NewGuid().ToString("N");
        StateTracker.Instance.Update(new StateEntry { Name = kept, State = JobState.Inactive });

        StateTracker.Instance.Remove("does-not-exist-" + Guid.NewGuid().ToString("N"));

        Assert.Contains(ReadStates(), s => s.Name == kept);
    }

    [Fact]
    public void Remove_PreservesOtherEntries()
    {
        var a = "a-" + Guid.NewGuid().ToString("N");
        var b = "b-" + Guid.NewGuid().ToString("N");
        StateTracker.Instance.Update(new StateEntry { Name = a, State = JobState.Inactive });
        StateTracker.Instance.Update(new StateEntry { Name = b, State = JobState.Inactive });

        StateTracker.Instance.Remove(a);

        var states = ReadStates();
        Assert.DoesNotContain(states, s => s.Name == a);
        Assert.Contains(states, s => s.Name == b);
    }

    [Fact]
    public void Remove_IsCaseInsensitive()
    {
        var name = "CaseTest-" + Guid.NewGuid().ToString("N");
        StateTracker.Instance.Update(new StateEntry { Name = name, State = JobState.Inactive });

        StateTracker.Instance.Remove(name.ToUpperInvariant());

        Assert.DoesNotContain(ReadStates(), s => s.Name == name);
    }

    [Fact]
    public void Remove_EvictsJobProgressFromMap()
    {
        var name = "evict-" + Guid.NewGuid().ToString("N");
        StateTracker.Instance.Update(new StateEntry { Name = name, State = JobState.Inactive });
        Assert.True(StateTracker.Instance.Jobs.ContainsKey(name));

        StateTracker.Instance.Remove(name);

        Assert.False(StateTracker.Instance.Jobs.ContainsKey(name));
    }

    [Fact]
    public void Remove_NoStateFile_DoesNotThrow()
    {
        var ex = Record.Exception(() => StateTracker.Instance.Remove("anything"));
        Assert.Null(ex);
    }
}

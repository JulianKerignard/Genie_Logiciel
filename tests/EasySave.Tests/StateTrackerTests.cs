using System.Text.Json;
using EasySave.Services;

namespace EasySave.Tests;

// Integration tests on the StateTracker singleton. Each test gets its own temp directory
// and reloads AppConfig to point StateTracker at an isolated state.json.
// Parallelization is disabled because AppConfig.Instance is shared across tests.
[CollectionDefinition("StateCollection", DisableParallelization = true)]
public class StateCollection { }

[Collection("StateCollection")]
public class StateTrackerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _stateFilePath;

    public StateTrackerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "easysave-tests-" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public void Update_NewJob_AddsEntry()
    {
        var entry = new StateEntry { Name = "Job1", State = JobState.Active };

        StateTracker.Instance.Update(entry);

        Assert.True(File.Exists(_stateFilePath));
        var states = ReadStates();
        Assert.Single(states);
        Assert.Equal("Job1", states[0].Name);
    }

    [Fact]
    public void Update_ExistingJob_ReplacesEntry()
    {
        StateTracker.Instance.Update(new StateEntry
        {
            Name = "Job1",
            State = JobState.Active,
            TotalFilesEligible = 10,
            FilesRemaining = 10
        });

        StateTracker.Instance.Update(new StateEntry
        {
            Name = "Job1",
            State = JobState.Active,
            TotalFilesEligible = 10,
            FilesRemaining = 5
        });

        var states = ReadStates();
        Assert.Single(states);
        Assert.Equal(5, states[0].FilesRemaining);
    }

    [Fact]
    public void Update_AtomicWrite_NeverLeavesCorruptedFile()
    {
        for (var i = 0; i < 5; i++)
        {
            StateTracker.Instance.Update(new StateEntry
            {
                Name = $"Job{i}",
                State = JobState.Active
            });

            var content = File.ReadAllText(_stateFilePath);
            var parsed = JsonSerializer.Deserialize<List<StateEntry>>(content);
            Assert.NotNull(parsed);
        }

        Assert.False(File.Exists(_stateFilePath + ".tmp"));
    }

    [Fact]
    public void Update_FromTwoThreads_NoCorruption()
    {
        const int updatesPerThread = 50;

        var jobA = new Thread(() =>
        {
            for (var i = 0; i < updatesPerThread; i++)
            {
                StateTracker.Instance.Update(new StateEntry
                {
                    Name = "JobA",
                    State = JobState.Active,
                    FilesRemaining = i
                });
            }
        });

        var jobB = new Thread(() =>
        {
            for (var i = 0; i < updatesPerThread; i++)
            {
                StateTracker.Instance.Update(new StateEntry
                {
                    Name = "JobB",
                    State = JobState.Active,
                    FilesRemaining = i
                });
            }
        });

        jobA.Start();
        jobB.Start();
        jobA.Join();
        jobB.Join();

        var states = ReadStates();
        Assert.Equal(2, states.Count);
        Assert.Contains(states, s => s.Name == "JobA");
        Assert.Contains(states, s => s.Name == "JobB");
    }

    private List<StateEntry> ReadStates()
    {
        var content = File.ReadAllText(_stateFilePath);
        return JsonSerializer.Deserialize<List<StateEntry>>(content) ?? new List<StateEntry>();
    }
}

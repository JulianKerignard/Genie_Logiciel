using System.Text.Json;
using EasySave;
using EasySave.Infrastructure.Events;
using EasySave.Services;
using EasySave.Shared;

namespace EasySave.Tests.V2;

[Collection("AppConfigMutation")]
public class StateTrackerEventBridgeTests : IDisposable
{
    private readonly string _tempDir;

    public StateTrackerEventBridgeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"easysave-bridge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        // Redirect AppConfig at the temp dir so StateTracker writes its
        // state.json there. AppConfig setters are init-only — must go through
        // AppConfig.Load(configPath) like the other AppConfig-mutating tests.
        var configPath = Path.Combine(_tempDir, "appsettings.json");
        var payload = new
        {
            StateFilePath = Path.Combine(_tempDir, "state.json"),
            LogDirectory = _tempDir,
            JobsFilePath = Path.Combine(_tempDir, "jobs.json"),
        };
        File.WriteAllText(configPath, JsonSerializer.Serialize(payload));
        AppConfig.Load(configPath);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task Bridge_Republishes_StateTrackerUpdate_AsEventDto()
    {
        await using var bus = new ChannelEventBus();
        using var bridge = new StateTrackerEventBridge(StateTracker.Instance, bus);
        bridge.Start();

        var got = new TaskCompletionSource<EventDto>();
        bus.Subscribe<EventDto>(e => got.TrySetResult(e));

        StateTracker.Instance.Update(new StateEntry
        {
            Name = "bridge-test",
            State = JobState.Active,
            TotalFilesEligible = 10,
            FilesRemaining = 7,
            TotalSize = 1024,
            SizeRemaining = 512,
            CurrentSource = @"\\nas\share\src.docx",
        });

        var evt = await got.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(EventType.JobProgress, evt.Type);
        Assert.NotNull(evt.Progress);
        Assert.Equal("bridge-test", evt.Progress!.JobName);
        Assert.Equal(JobStateEnum.Running, evt.Progress.State);
        Assert.Equal(7, evt.Progress.FilesLeft);
        Assert.Equal(10, evt.Progress.TotalFiles);
        Assert.Equal(512, evt.Progress.BytesLeft);
        Assert.Equal(1024, evt.Progress.BytesTotal);
        Assert.Equal(@"\\nas\share\src.docx", evt.Progress.CurrentFile);
    }

    [Fact]
    public async Task Bridge_MapsPausedAndInactive_ToWireStates()
    {
        await using var bus = new ChannelEventBus();
        using var bridge = new StateTrackerEventBridge(StateTracker.Instance, bus);
        bridge.Start();

        var pausedReceived = new TaskCompletionSource<EventDto>();
        var doneReceived = new TaskCompletionSource<EventDto>();
        bus.Subscribe<EventDto>(e =>
        {
            if (e.Progress?.State == JobStateEnum.Paused) pausedReceived.TrySetResult(e);
            if (e.Progress?.State == JobStateEnum.Done) doneReceived.TrySetResult(e);
        });

        StateTracker.Instance.Update(new StateEntry
        {
            Name = "map-test",
            State = JobState.Active,
            TotalFilesEligible = 5,
            FilesRemaining = 5,
        });
        StateTracker.Instance.Pause("map-test", "test-reason");
        StateTracker.Instance.Update(new StateEntry
        {
            Name = "map-test",
            State = JobState.Inactive,
            TotalFilesEligible = 5,
            FilesRemaining = 0,
        });

        var paused = await pausedReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var done = await doneReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(JobStateEnum.Paused, paused.Progress!.State);
        Assert.Equal(JobStateEnum.Done, done.Progress!.State);
    }

    [Fact]
    public async Task Dispose_Detaches_FromStateTrackerEvent()
    {
        await using var bus = new ChannelEventBus();
        using (var bridge = new StateTrackerEventBridge(StateTracker.Instance, bus))
        {
            bridge.Start();
        }

        var hits = 0;
        bus.Subscribe<EventDto>(_ => Interlocked.Increment(ref hits));

        StateTracker.Instance.Update(new StateEntry
        {
            Name = "post-dispose",
            State = JobState.Active,
            TotalFilesEligible = 1,
            FilesRemaining = 1,
        });

        // Wait briefly to let the consumer deliver if anything was republished.
        await Task.Delay(150);
        Assert.Equal(0, hits);
    }
}

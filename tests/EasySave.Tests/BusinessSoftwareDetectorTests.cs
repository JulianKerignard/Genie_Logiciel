using EasySave.Services;

namespace EasySave.Tests;

public class BusinessSoftwareDetectorTests
{
    private sealed class FakeProcessProvider : IProcessProvider
    {
        public List<string> Running { get; } = new();
        public IReadOnlyCollection<string> GetRunningProcessNames() => Running.ToArray();
    }

    private static BusinessSoftwareDetector NewDetector(
        FakeProcessProvider provider, params string[] watched)
        => new(provider, watched, TimeSpan.FromSeconds(1));

    [Fact]
    public void Refresh_ProcessAppears_RaisesDetectedEvent()
    {
        var provider = new FakeProcessProvider();
        var detector = NewDetector(provider, "outlook");
        var detected = new List<string>();
        detector.BusinessSoftwareDetected += (_, name) => detected.Add(name);

        provider.Running.Add("outlook");
        detector.Refresh();

        Assert.Equal(new[] { "outlook" }, detected);
        Assert.True(detector.IsAnyBusinessSoftwareRunning);
    }

    [Fact]
    public void Refresh_ProcessClosesAfterAppearing_RaisesClosedEvent()
    {
        var provider = new FakeProcessProvider { Running = { "outlook" } };
        var detector = NewDetector(provider, "outlook");
        detector.Refresh(); // detected

        var closed = new List<string>();
        detector.BusinessSoftwareClosed += (_, name) => closed.Add(name);

        provider.Running.Clear();
        detector.Refresh();

        Assert.Equal(new[] { "outlook" }, closed);
        Assert.False(detector.IsAnyBusinessSoftwareRunning);
    }

    [Fact]
    public void Refresh_ProcessNotInWatchList_NoEvent()
    {
        var provider = new FakeProcessProvider { Running = { "notepad" } };
        var detector = NewDetector(provider, "outlook");
        var detected = new List<string>();
        var closed = new List<string>();
        detector.BusinessSoftwareDetected += (_, name) => detected.Add(name);
        detector.BusinessSoftwareClosed += (_, name) => closed.Add(name);

        detector.Refresh();

        Assert.Empty(detected);
        Assert.Empty(closed);
    }

    [Fact]
    public void Refresh_NameMatchIsCaseInsensitive()
    {
        var provider = new FakeProcessProvider { Running = { "OUTLOOK" } };
        var detector = NewDetector(provider, "outlook");
        var detected = new List<string>();
        detector.BusinessSoftwareDetected += (_, name) => detected.Add(name);

        detector.Refresh();

        Assert.Single(detected);
    }

    [Fact]
    public void Refresh_WatchedHasExeSuffix_MatchesBareName()
    {
        // Process.ProcessName strips ".exe" on Windows; operators commonly write
        // "calc.exe" in appsettings.json. The detector must accept both forms.
        var provider = new FakeProcessProvider { Running = { "calc" } };
        var detector = NewDetector(provider, "calc.exe");
        var detected = new List<string>();
        detector.BusinessSoftwareDetected += (_, name) => detected.Add(name);

        detector.Refresh();

        Assert.Single(detected);
        Assert.True(detector.IsAnyBusinessSoftwareRunning);
    }

    [Fact]
    public void Refresh_RunningHasExeSuffix_MatchesBareWatch()
    {
        // Symmetric case: a non-Windows or future provider that returns "calc.exe"
        // must still match a "calc" watch entry.
        var provider = new FakeProcessProvider { Running = { "calc.exe" } };
        var detector = NewDetector(provider, "calc");
        var detected = new List<string>();
        detector.BusinessSoftwareDetected += (_, name) => detected.Add(name);

        detector.Refresh();

        Assert.Single(detected);
        Assert.True(detector.IsAnyBusinessSoftwareRunning);
    }

    [Fact]
    public void Refresh_NoTransition_NoDuplicateEvent()
    {
        var provider = new FakeProcessProvider { Running = { "outlook" } };
        var detector = NewDetector(provider, "outlook");
        var detected = new List<string>();
        detector.BusinessSoftwareDetected += (_, name) => detected.Add(name);

        detector.Refresh();
        detector.Refresh(); // same state, must not re-raise

        Assert.Single(detected);
    }

    [Fact]
    public void Constructor_NullProvider_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new BusinessSoftwareDetector(null!, new[] { "outlook" }));
    }

    [Fact]
    public void Constructor_NullWatchList_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new BusinessSoftwareDetector(new FakeProcessProvider(), null!));
    }

    [Fact]
    public void Start_PerformsInitialPollSynchronously()
    {
        var provider = new FakeProcessProvider { Running = { "outlook" } };
        // Long interval so the timer cannot fire before the assertion.
        var detector = new BusinessSoftwareDetector(provider, new[] { "outlook" }, TimeSpan.FromMinutes(5));
        var detected = new List<string>();
        detector.BusinessSoftwareDetected += (_, name) => detected.Add(name);

        detector.Start();
        try
        {
            Assert.Equal(new[] { "outlook" }, detected);
            Assert.True(detector.IsAnyBusinessSoftwareRunning);
        }
        finally
        {
            detector.Stop();
        }
    }
}

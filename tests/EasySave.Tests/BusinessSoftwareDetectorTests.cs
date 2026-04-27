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
}

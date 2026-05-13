using EasyLog;
using EasySave.Services;

namespace EasySave.Tests.V2;

// Locks the factory behaviour so a regression in Program.cs / App.axaml.cs
// (passing LogMode but forgetting the endpoint, or vice versa) is caught
// by CI rather than at the demo. The factory is the single bottleneck for
// "do we ship logs centrally or not" — once it's correct, both entry
// points are correct.
public class DailyLoggerFactoryTests : IDisposable
{
    private readonly string _logDir;

    public DailyLoggerFactoryTests()
    {
        _logDir = Path.Combine(Path.GetTempPath(), "dlf-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_logDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_logDir))
            Directory.Delete(_logDir, recursive: true);
    }

    [Fact]
    public void Create_LocalMode_NoShipper()
    {
        var (logger, shipper) = DailyLoggerFactory.Create(_logDir, "json", LogMode.Local, "http://x:9100/logs");

        Assert.NotNull(logger);
        Assert.Null(shipper);
        if (logger is IDisposable d) d.Dispose();
    }

    [Fact]
    public async Task Create_CentralizedMode_WithEndpoint_BuildsShipper()
    {
        var (logger, shipper) = DailyLoggerFactory.Create(_logDir, "json", LogMode.Centralized, "http://collector:9100/logs");

        Assert.NotNull(logger);
        Assert.NotNull(shipper);
        // Dispose order mirrors production (Program.cs / DisposeServices):
        // logger first so its writer loop forwards pending entries, then
        // shipper so the HTTP queue drains.
        if (logger is IDisposable d) d.Dispose();
        await shipper!.DisposeAsync();
    }

    [Fact]
    public void Create_CentralizedMode_EmptyEndpoint_FallsBackToLocal()
    {
        var (logger, shipper) = DailyLoggerFactory.Create(_logDir, "json", LogMode.Centralized, "");

        Assert.NotNull(logger);
        Assert.Null(shipper);
        if (logger is IDisposable d) d.Dispose();
    }

    [Fact]
    public void Create_CentralizedMode_InvalidEndpoint_FallsBackToLocal()
    {
        var (logger, shipper) = DailyLoggerFactory.Create(_logDir, "json", LogMode.Centralized, "not a uri");

        Assert.NotNull(logger);
        Assert.Null(shipper);
        if (logger is IDisposable d) d.Dispose();
    }

    [Fact]
    public void Create_XmlFormat_ReturnsXmlLogger()
    {
        var (logger, _) = DailyLoggerFactory.Create(_logDir, "xml", LogMode.Local, "");
        Assert.IsType<XmlDailyLogger>(logger);
        if (logger is IDisposable d) d.Dispose();
    }

    [Fact]
    public void Create_JsonFormat_ReturnsJsonLogger()
    {
        var (logger, _) = DailyLoggerFactory.Create(_logDir, "json", LogMode.Local, "");
        Assert.IsType<JsonDailyLogger>(logger);
        if (logger is IDisposable d) d.Dispose();
    }

    [Fact]
    public async Task Create_BothMode_WithEndpoint_BuildsShipper()
    {
        var (logger, shipper) = DailyLoggerFactory.Create(_logDir, "json", LogMode.Both, "http://collector:9100/logs");

        Assert.NotNull(logger);
        Assert.NotNull(shipper);
        await shipper!.DisposeAsync();
        if (logger is IDisposable d) d.Dispose();
    }

    [Fact]
    public void Create_NonHttpScheme_FallsBackToLocal()
    {
        var (logger, shipper) = DailyLoggerFactory.Create(_logDir, "json", LogMode.Centralized, "file:///tmp/logs");

        Assert.NotNull(logger);
        Assert.Null(shipper);
        if (logger is IDisposable d) d.Dispose();
    }
}

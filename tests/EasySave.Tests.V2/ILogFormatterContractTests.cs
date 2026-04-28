using EasyLog;

namespace EasySave.Tests.V2;

public class ILogFormatterContractTests
{
    [Fact]
    public void Interface_HasExpectedPublicMembers()
    {
        // Guards against accidental rename/removal of the v2 contract that
        // EasyLog ships to other ProSoft applications.
        var type = typeof(ILogFormatter);

        Assert.True(type.IsPublic);
        Assert.NotNull(type.GetMethod(nameof(ILogFormatter.Format), new[] { typeof(LogEntry) }));
        Assert.NotNull(type.GetProperty(nameof(ILogFormatter.FileExtension)));
    }

    [Fact]
    public void Implementation_CanBeWrittenInTestAssembly()
    {
        // Concrete formatters (JsonFormatter, XmlFormatter) ship in Phase 3.
        // Until then this fake proves the interface is implementable as documented.
        ILogFormatter formatter = new TextFormatterFake();

        var entry = new LogEntry { JobName = "fake" };
        Assert.Equal("fake", formatter.Format(entry));
        Assert.Equal(".txt", formatter.FileExtension);
    }

    private sealed class TextFormatterFake : ILogFormatter
    {
        public string Format(LogEntry entry) => entry.JobName;
        public string FileExtension => ".txt";
    }
}

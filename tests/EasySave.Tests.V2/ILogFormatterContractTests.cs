using EasyLog;

namespace EasySave.Tests.V2;

public class ILogFormatterContractTests
{
    [Fact]
    public void Interface_IsPublic()
    {
        // Cheap sanity check that the public surface is reachable from outside the assembly,
        // which is the whole point of shipping ILogFormatter to other ProSoft applications.
        Assert.True(typeof(ILogFormatter).IsInterface);
        Assert.True(typeof(ILogFormatter).IsPublic);
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

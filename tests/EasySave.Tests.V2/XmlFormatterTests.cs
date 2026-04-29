using System.Xml.Linq;
using System.Xml.Schema;
using EasyLog;

namespace EasySave.Tests.V2;

public class XmlFormatterTests
{
    [Fact]
    public void FileExtension_IsXml()
    {
        var formatter = new XmlFormatter();
        Assert.Equal(".xml", formatter.FileExtension);
    }

    [Fact]
    public void Format_NullEntry_Throws()
    {
        var formatter = new XmlFormatter();
        Assert.Throws<ArgumentNullException>(() => formatter.Format(null!));
    }

    [Fact]
    public void Format_ProducesParseableEntryFragment()
    {
        var formatter = new XmlFormatter();
        var entry = new LogEntry { JobName = "fragment", FileTransferTimeMs = 1 };

        var xml = formatter.Format(entry);
        var element = XElement.Parse(xml);

        Assert.Equal("Entry", element.Name.LocalName);
    }

    [Fact]
    public void Format_OmitsEncryptionTimeMs_WhenNull()
    {
        // Mirrors the JSON behavior: a v1-style entry must not surface a v2-only field.
        var formatter = new XmlFormatter();
        var entry = new LogEntry { JobName = "no-encrypt", FileTransferTimeMs = 1 };

        var element = XElement.Parse(formatter.Format(entry));

        Assert.Null(element.Element("EncryptionTimeMs"));
    }

    [Fact]
    public void Format_IncludesEncryptionTimeMs_WhenSet()
    {
        var formatter = new XmlFormatter();
        var entry = new LogEntry { JobName = "encrypt", EncryptionTimeMs = 42 };

        var element = XElement.Parse(formatter.Format(entry));

        Assert.Equal("42", element.Element("EncryptionTimeMs")?.Value);
    }

    [Fact]
    public void Format_RoundTrip_PreservesAllFields()
    {
        var formatter = new XmlFormatter();
        var original = new LogEntry
        {
            Timestamp = "2026-04-28T12:00:00+02:00",
            JobName = "roundtrip",
            SourceFile = @"\\nas\share\src.docx",
            TargetFile = @"\\nas\share\dst.docx",
            FileSize = 1024,
            FileTransferTimeMs = 5,
            EncryptionTimeMs = 17,
        };

        var element = XElement.Parse(formatter.Format(original));
        var rebuilt = new LogEntry
        {
            Timestamp = element.Element("Timestamp")!.Value,
            JobName = element.Element("JobName")!.Value,
            SourceFile = element.Element("SourceFile")!.Value,
            TargetFile = element.Element("TargetFile")!.Value,
            FileSize = long.Parse(element.Element("FileSize")!.Value),
            FileTransferTimeMs = long.Parse(element.Element("FileTransferTimeMs")!.Value),
            EncryptionTimeMs = long.Parse(element.Element("EncryptionTimeMs")!.Value),
        };

        Assert.Equal(original.Timestamp, rebuilt.Timestamp);
        Assert.Equal(original.JobName, rebuilt.JobName);
        Assert.Equal(original.SourceFile, rebuilt.SourceFile);
        Assert.Equal(original.TargetFile, rebuilt.TargetFile);
        Assert.Equal(original.FileSize, rebuilt.FileSize);
        Assert.Equal(original.FileTransferTimeMs, rebuilt.FileTransferTimeMs);
        Assert.Equal(original.EncryptionTimeMs, rebuilt.EncryptionTimeMs);
    }

    [Fact]
    public void RoundTrip_NegativeFileTransferTime_PreservedAsErrorSignal()
    {
        // Cahier: FileTransferTimeMs < 0 is the error signal for a failed copy.
        // The XML form must preserve negative values exactly.
        var formatter = new XmlFormatter();
        var entry = new LogEntry { JobName = "copy-failed", FileTransferTimeMs = -1 };

        var element = XElement.Parse(formatter.Format(entry));

        Assert.Equal("-1", element.Element("FileTransferTimeMs")?.Value);
    }

    [Fact]
    public void Format_OutputValidatesAgainstXsd_WhenWrappedInLogs()
    {
        var formatter = new XmlFormatter();
        var entries = new[]
        {
            new LogEntry { JobName = "v1-shape", FileTransferTimeMs = 1 },
            new LogEntry { JobName = "encrypt", EncryptionTimeMs = 42 },
        };

        var doc = WrapInLogs(entries.Select(e => XElement.Parse(formatter.Format(e))));

        // Validate throws on first error — reaching the assertion means valid.
        ValidateAgainstSchema(doc);
        Assert.Equal("Logs", doc.Root!.Name.LocalName);
    }

    [Fact]
    public void Format_EmptyLogs_ValidatesAgainstXsd()
    {
        // A daily file produced before any job has run must still validate;
        // the schema declares Entry minOccurs=0 for that reason.
        var doc = WrapInLogs(Enumerable.Empty<XElement>());

        ValidateAgainstSchema(doc);
    }

    [Fact]
    public void LoadSchema_ReturnsEmbeddedXsd()
    {
        // Guards against the embedded resource being dropped from the csproj.
        var schema = XmlFormatter.LoadSchema();

        Assert.NotNull(schema);
    }

    [Theory]
    [InlineData(@"C:\path\with & ampersand\file.txt")]
    [InlineData("C:\\path\\with <angle> brackets\\file.txt")]
    [InlineData("C:\\path\\with \"quotes\" and 'apos'\\file.txt")]
    [InlineData(@"\\server\share\file with spaces.txt")]
    [InlineData("Café Résumé/Ωmega/файл.txt")]
    public void Format_EscapesSpecialCharactersInFilePaths(string path)
    {
        // Real-world paths can contain XML-meta characters (& < > " ') and
        // non-ASCII glyphs. The formatter must escape them so the output is
        // both valid XML and valid against the schema, and the original
        // string must come back intact after parsing.
        var formatter = new XmlFormatter();
        var entry = new LogEntry { JobName = "special-chars", SourceFile = path, TargetFile = path };

        var xml = formatter.Format(entry);
        var element = XElement.Parse(xml);

        Assert.Equal(path, element.Element("SourceFile")?.Value);
        Assert.Equal(path, element.Element("TargetFile")?.Value);

        var doc = WrapInLogs(new[] { element });
        ValidateAgainstSchema(doc);
    }

    private static XDocument WrapInLogs(IEnumerable<XElement> entries)
    {
        return new XDocument(new XElement("Logs", entries));
    }

    private static void ValidateAgainstSchema(XDocument doc)
    {
        var schemas = new XmlSchemaSet();
        schemas.Add(XmlFormatter.LoadSchema());
        doc.Validate(schemas, (sender, e) =>
            throw new XmlSchemaValidationException(e.Message));
    }
}

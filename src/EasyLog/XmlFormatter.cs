using System.Xml.Linq;
using System.Xml.Schema;

namespace EasyLog;

/// <summary>
/// Serializes a <see cref="LogEntry"/> as an <c>&lt;Entry&gt;</c> XML fragment
/// for the daily XML log file required by EasySave v2.
/// </summary>
/// <remarks>
/// The fragment is meant to be appended under a <c>&lt;Logs&gt;</c> root element
/// produced by the daily logger. The accompanying schema (embedded as
/// <c>EasyLog.Schemas.easysave-log.xsd</c>) describes the full document.
/// </remarks>
public sealed class XmlFormatter : ILogFormatter
{
    private const string EmbeddedSchemaResourceName = "EasyLog.Schemas.easysave-log.xsd";

    /// <inheritdoc />
    public string FileExtension => ".xml";

    /// <inheritdoc />
    public string Format(LogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        XElement element = new("Entry",
            new XElement("Timestamp", entry.Timestamp),
            new XElement("JobName", entry.JobName),
            new XElement("SourceFile", entry.SourceFile),
            new XElement("TargetFile", entry.TargetFile),
            new XElement("FileSize", entry.FileSize),
            new XElement("FileTransferTimeMs", entry.FileTransferTimeMs));

        // Mirror the JSON formatter: omit the v2-only field when unset so that
        // v1 consumers reading the file see the v1 element set exactly.
        if (entry.EncryptionTimeMs.HasValue)
        {
            element.Add(new XElement("EncryptionTimeMs", entry.EncryptionTimeMs.Value));
        }

        return element.ToString(SaveOptions.None);
    }

    /// <summary>
    /// Loads the XSD schema describing the daily XML log document
    /// (<c>&lt;Logs&gt;</c> with zero or more <c>&lt;Entry&gt;</c> children).
    /// </summary>
    /// <remarks>
    /// Exposed so daily loggers and external consumers of EasyLog can validate
    /// the on-disk log without shipping their own copy of the schema.
    /// </remarks>
    /// <returns>The parsed <see cref="XmlSchema"/> embedded in the assembly.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the embedded resource is missing or the schema cannot be parsed.
    /// </exception>
    public static XmlSchema LoadSchema()
    {
        using Stream stream = typeof(XmlFormatter).Assembly
            .GetManifestResourceStream(EmbeddedSchemaResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded XSD resource '{EmbeddedSchemaResourceName}' was not found.");

        return XmlSchema.Read(stream, validationEventHandler: null)
            ?? throw new InvalidOperationException("Failed to parse the embedded XSD.");
    }
}

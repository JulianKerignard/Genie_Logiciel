using System.Diagnostics;
using System.Xml;
using System.Xml.Linq;

namespace EasyLog;

/// <summary>
/// Writes <see cref="LogEntry"/> instances to a daily XML file
/// named "yyyy-MM-dd.xml" inside a configurable directory.
/// Each file is a valid document with a <c>&lt;Logs&gt;</c> root element and
/// one <c>&lt;Entry&gt;</c> child per logged operation, conforming to the
/// schema in <c>EasyLog.Schemas.easysave-log.xsd</c>.
/// Writes are serialized with a lock and performed atomically via a temporary
/// file so concurrent backup jobs never corrupt the daily file.
/// </summary>
public sealed class XmlDailyLogger : IDailyLogger
{
    private readonly string _logDirectory;
    private readonly XmlFormatter _formatter = new();
    private readonly object _writeLock = new();

    /// <summary>
    /// Initializes a new logger writing to <paramref name="logDirectory"/>.
    /// The directory is created if it does not exist.
    /// </summary>
    /// <param name="logDirectory">Absolute or UNC path where daily XML files are stored.</param>
    /// <exception cref="ArgumentException">Thrown when the path is null or empty.</exception>
    public XmlDailyLogger(string logDirectory)
    {
        if (string.IsNullOrWhiteSpace(logDirectory))
            throw new ArgumentException("Log directory must be provided.", nameof(logDirectory));
        _logDirectory = logDirectory;
        Directory.CreateDirectory(_logDirectory);
    }

    /// <summary>Absolute path of the directory where daily log files are written.</summary>
    public string LogDirectory => _logDirectory;

    /// <inheritdoc />
    public void Append(LogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        // Local date is intentional: daily files must align with the business day
        // seen by the operator, not UTC.
        string filePath = Path.Combine(_logDirectory, $"{DateTime.Now:yyyy-MM-dd}.xml");

        LogEntry normalized = new()
        {
            Timestamp = entry.Timestamp,
            JobName = entry.JobName,
            SourceFile = ToNormalizedPath(entry.SourceFile),
            TargetFile = ToNormalizedPath(entry.TargetFile),
            FileSize = entry.FileSize,
            FileTransferTimeMs = entry.FileTransferTimeMs,
            EncryptionTimeMs = entry.EncryptionTimeMs,
        };

        lock (_writeLock)
        {
            XDocument doc = ReadExisting(filePath);
            doc.Root!.Add(XElement.Parse(_formatter.Format(normalized)));
            WriteAtomic(filePath, doc);
        }
    }

    private static string ToNormalizedPath(string path) =>
        LogPathHelper.ToNormalizedPath(path);

    private static XDocument ReadExisting(string filePath)
    {
        if (!File.Exists(filePath))
            return new XDocument(new XElement("Logs"));

        try
        {
            return XDocument.Load(filePath);
        }
        catch (XmlException ex)
        {
            // Genuinely malformed XML — preserve the file as evidence and start fresh.
            string backupPath = $"{filePath}.corrupted-{DateTime.Now:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
            File.Move(filePath, backupPath);
            Trace.TraceWarning($"[EasyLog] Corrupted XML log moved to {backupPath} - {ex.Message}");
            return new XDocument(new XElement("Logs"));
        }
        // IOException / UnauthorizedAccessException intentionally propagated.
        // A transient lock (antivirus, OneDrive sync, log viewer holding the file
        // briefly) used to flow through the same arm and quarantine the live
        // daily file, splitting the day's entries across multiple files. The
        // convention now matches JsonDailyLogger and StateTracker / JobRepository
        // / SettingsRepository: only the format-specific parse exception triggers
        // quarantine; IO failures bubble up and the caller decides.
    }

    private static void WriteAtomic(string filePath, XDocument doc)
    {
        string tmpPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            doc.Save(tmpPath);
            File.Move(tmpPath, filePath, overwrite: true);
        }
        catch (Exception)
        {
            try { File.Delete(tmpPath); } catch { }
            throw;
        }
    }
}

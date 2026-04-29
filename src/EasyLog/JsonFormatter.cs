using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyLog;

/// <summary>
/// Serializes a <see cref="LogEntry"/> to its JSON representation for the
/// daily JSON log file. Symmetric to <see cref="XmlFormatter"/>: both honor
/// <see cref="ILogFormatter"/> so callers can swap formats by configuration.
/// </summary>
/// <remarks>
/// <para>
/// The output is a single JSON object — the daily logger is responsible for
/// assembling these objects into the JSON array stored in the daily file.
/// </para>
/// <para>
/// Backward compatibility with v1 is guaranteed by <see cref="LogEntry"/>:
/// <see cref="LogEntry.EncryptionTimeMs"/> is annotated to be omitted when
/// null, so a v2 entry without encryption produces the exact same JSON
/// shape as a v1 entry.
/// </para>
/// </remarks>
public sealed class JsonFormatter : ILogFormatter
{
    // Indented to match JsonDailyLogger's existing daily-file style and keep
    // hand-readable logs (cahier requirement, no jq needed at the customer site).
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <inheritdoc />
    public string FileExtension => ".json";

    /// <inheritdoc />
    public string Format(LogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return JsonSerializer.Serialize(entry, SerializerOptions);
    }
}

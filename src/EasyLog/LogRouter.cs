namespace EasyLog;

/// <summary>
/// Shared helpers used by <see cref="JsonDailyLogger"/> and
/// <see cref="XmlDailyLogger"/> for the V3 <see cref="LogMode"/> routing
/// rules and for the canonical normalization of an incoming
/// <see cref="LogEntry"/>. Centralizing these here keeps the per-format
/// Append paths free of duplicated bookkeeping (CLAUDE.md zero-duplication
/// criterion) and guarantees both formats follow the exact same fall-back
/// semantics when a misconfigured host wires a centralized mode with a
/// null shipper.
/// </summary>
internal static class LogRouter
{
    /// <summary>
    /// Returns the effective routing mode for a given shipper / requested
    /// mode pair. A null shipper forces <see cref="LogMode.Local"/> so a
    /// host with <c>log_mode=Centralized</c> but no endpoint never silently
    /// drops entries.
    /// </summary>
    public static LogMode Effective(ILogShipper? shipper, LogMode requested) =>
        shipper is null ? LogMode.Local : requested;

    /// <summary>True when the entry should be forwarded to the centralized shipper.</summary>
    public static bool ShouldShip(LogMode mode) =>
        mode is LogMode.Centralized or LogMode.Both;

    /// <summary>True when the entry should be persisted to the local daily file.</summary>
    public static bool ShouldWriteLocal(LogMode mode) =>
        mode is LogMode.Local or LogMode.Both;

    /// <summary>
    /// Returns a normalized copy of <paramref name="entry"/> ready for
    /// persistence: source / target paths run through
    /// <see cref="LogPathHelper.ToNormalizedPath"/>, and host fields are
    /// stamped from <see cref="Environment"/> when the caller left them
    /// null. Caller-provided values are preserved untouched so a central
    /// collector relaying entries from remote hosts never overwrites the
    /// original sender's identity.
    /// </summary>
    public static LogEntry Normalize(LogEntry entry) => new()
    {
        Timestamp = entry.Timestamp,
        JobName = entry.JobName,
        SourceFile = LogPathHelper.ToNormalizedPath(entry.SourceFile),
        TargetFile = LogPathHelper.ToNormalizedPath(entry.TargetFile),
        FileSize = entry.FileSize,
        FileTransferTimeMs = entry.FileTransferTimeMs,
        EncryptionTimeMs = entry.EncryptionTimeMs,
        EventType = entry.EventType,
        MachineName = entry.MachineName ?? Environment.MachineName,
        UserName = entry.UserName ?? Environment.UserName,
    };
}

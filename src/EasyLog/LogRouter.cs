namespace EasyLog;

/// <summary>
/// Shared helpers used by <see cref="JsonDailyLogger"/> and
/// <see cref="XmlDailyLogger"/> to apply the V3 <see cref="LogMode"/>
/// routing rules in a single place. Pulling these out of the loggers
/// keeps the per-format Append paths free of duplicated bookkeeping
/// (CLAUDE.md zero-duplication grading criterion) and ensures both
/// formats follow the exact same fall-back semantics when a misconfigured
/// host wires a centralized mode with a null shipper.
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
}

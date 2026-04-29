namespace EasySave.Services;

/// <summary>
/// Abstraction over the OS process list, kept narrow so tests can swap in a
/// deterministic fake without spinning up real processes.
/// </summary>
public interface IProcessProvider
{
    /// <summary>
    /// Returns a snapshot of the currently running process names. Comparisons
    /// against the watch list are case-insensitive, so the implementation
    /// does not need to normalise casing.
    /// </summary>
    IReadOnlyCollection<string> GetRunningProcessNames();
}

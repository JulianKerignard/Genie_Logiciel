using System.ComponentModel;
using System.Diagnostics;

namespace EasySave.Services;

/// <summary>
/// Production <see cref="IProcessProvider"/> backed by
/// <see cref="Process.GetProcesses()"/>. Disposes the snapshot Process
/// instances so handles are not leaked between polls.
/// </summary>
public sealed class SystemProcessProvider : IProcessProvider
{
    /// <inheritdoc />
    public IReadOnlyCollection<string> GetRunningProcessNames()
    {
        var processes = Process.GetProcesses();
        try
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in processes)
            {
                try
                {
                    names.Add(p.ProcessName);
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                    // InvalidOperationException: the process exited between
                    // GetProcesses() and ProcessName access.
                    // Win32Exception: the OS denied access (protected/elevated process).
                    // An unhandled exception here would crash the Timer thread and
                    // terminate the host on .NET 8, so swallow both deliberately.
                }
            }
            return names;
        }
        finally
        {
            foreach (var p in processes)
            {
                p.Dispose();
            }
        }
    }
}

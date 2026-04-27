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
                catch (InvalidOperationException)
                {
                    // The process exited between GetProcesses() and ProcessName access.
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

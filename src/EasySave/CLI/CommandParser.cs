using EasySave.Models;

namespace EasySave.CLI;

/// <summary>Parses raw console input into structured command arguments.</summary>
public sealed class CommandParser
{
    // Cahier v1.0 caps the job count; any range or list wider than this is invalid input.
    // The cap also prevents an out-of-memory if a user types something like "1-99999999".
    // Single source of truth lives in BackupLimits.MaxJobs.
    private const int MaxIndex = BackupLimits.MaxJobs;

    /// <summary>
    /// Parses a job selection string into a sorted, deduplicated list of 1-based job indices.
    /// Supported formats: "1", "1-3", "1;3", "1-3;5".
    /// Returns an empty list if the input is malformed, contains indices below 1, or contains
    /// any index above 5 (cahier v1.0 cap).
    /// </summary>
    public List<int> ParseJobSelection(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new List<int>();

        var result = new HashSet<int>();

        foreach (var segment in input.Split(';'))
        {
            var trimmed = segment.Trim();
            if (trimmed.Contains('-'))
            {
                var parts = trimmed.Split('-');
                if (parts.Length != 2
                    || !int.TryParse(parts[0].Trim(), out var from)
                    || !int.TryParse(parts[1].Trim(), out var to)
                    || from < 1 || to < from || to > MaxIndex)
                    return new List<int>();

                for (var i = from; i <= to; i++)
                    result.Add(i);
            }
            else
            {
                if (!int.TryParse(trimmed, out var index) || index < 1 || index > MaxIndex)
                    return new List<int>();

                result.Add(index);
            }
        }

        return result.OrderBy(x => x).ToList();
    }
}

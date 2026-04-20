namespace EasySave.CLI;

/// <summary>Parses raw console input into structured command arguments.</summary>
public sealed class CommandParser
{
    /// <summary>
    /// Parses a job selection string into a sorted, deduplicated list of 1-based job indices.
    /// Supported formats: "1", "1-3", "1;3", "1-3;5".
    /// Returns an empty list if the input is malformed or contains indices below 1.
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
                    || from < 1 || to < from)
                    return new List<int>();

                for (var i = from; i <= to; i++)
                    result.Add(i);
            }
            else
            {
                if (!int.TryParse(trimmed, out var index) || index < 1)
                    return new List<int>();

                result.Add(index);
            }
        }

        return result.OrderBy(x => x).ToList();
    }
}

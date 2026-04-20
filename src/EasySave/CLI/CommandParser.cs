namespace EasySave.CLI;

/// <summary>Parses raw console input into structured command arguments.</summary>
public sealed class CommandParser
{
    /// <summary>Parses a job selection string (e.g. "1,2" or "1-3") into a list of 1-based job indices.</summary>
    public List<int> ParseJobSelection(string input) => new();
}

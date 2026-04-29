namespace EasyLog;

// Shared UNC / extended-length path normalisation used by every daily logger.
// Extracted to avoid duplication between JsonDailyLogger and XmlDailyLogger.
internal static class LogPathHelper
{
    internal static string ToNormalizedPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return path;
        string full = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows() && full.Length > 1 && full[1] == ':')
            return @"\\?\" + full;
        return full;
    }
}

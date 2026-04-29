namespace EasySave.UI.ViewModels;

/// <summary>
/// Single daily log file entry shown in the LogsView list.
/// </summary>
public sealed class LogFileItem
{
    public string FullPath { get; }
    public string DisplayName => Path.GetFileName(FullPath);
    public string SizeDisplay
    {
        get
        {
            try
            {
                var bytes = new FileInfo(FullPath).Length;
                return bytes < 1024 ? $"{bytes} B" : $"{bytes / 1024.0:0.#} KB";
            }
            catch (IOException)
            {
                return string.Empty;
            }
        }
    }

    public LogFileItem(string fullPath) => FullPath = fullPath;
}

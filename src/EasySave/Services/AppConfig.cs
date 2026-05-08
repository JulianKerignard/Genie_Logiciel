using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasySave.Services;

// Singleton holding the application configuration loaded from appsettings.json.
// Any service that needs a file path or a user-facing setting reads from AppConfig.Instance.
public sealed class AppConfig
{
    // Per-user application data root. Resolves to %AppData%\ProSoft\EasySave on Windows
    // and ~/.config/ProSoft/EasySave on Linux / macOS. Avoids C:\temp or the install
    // directory (which would require UAC under C:\Program Files).
    private static readonly string DataRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ProSoft",
        "EasySave");

    // Current configuration. Replaced once at startup by Load().
    public static AppConfig Instance { get; private set; } = new AppConfig();

    // Directory where daily log files are written.
    public string LogDirectory { get; init; } = Path.Combine(DataRoot, "Logs");

    // Full path of the real-time state file.
    public string StateFilePath { get; init; } = Path.Combine(DataRoot, "state.json");

    // Full path of the backup jobs definitions file.
    public string JobsFilePath { get; init; } = Path.Combine(DataRoot, "jobs.json");

    // UI language code (ISO 639-1), e.g. "en" or "fr".
    public string Language { get; init; } = "en";

    [JsonConstructor]
    private AppConfig() { }

    // Loads the configuration from the given JSON file, or keeps the defaults if the file is missing or invalid.
    // When path is null, appsettings.json is read from the executable directory.
    public static void Load(string? path = null)
    {
        path ??= Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

        if (!File.Exists(path))
        {
            Instance = new AppConfig();
        }
        else
        {
            try
            {
                var json = File.ReadAllText(path);
                Instance = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                Instance = new AppConfig();
            }
        }

        EnsurePathsWritable(Instance);
    }

    // Best-effort directory creation so JsonDailyLogger / StateTracker /
    // JobRepository never crash on the very first write with a cryptic
    // DirectoryNotFoundException when the configured directory just hasn't
    // been created yet. On unwritable paths we surface a stderr warning at
    // startup so the operator sees the configuration problem before the
    // first backup runs, rather than mid-job.
    private static void EnsurePathsWritable(AppConfig config)
    {
        TryEnsureDir(config.LogDirectory, nameof(LogDirectory));
        TryEnsureDir(Path.GetDirectoryName(config.StateFilePath), nameof(StateFilePath));
        TryEnsureDir(Path.GetDirectoryName(config.JobsFilePath), nameof(JobsFilePath));
    }

    private static void TryEnsureDir(string? dir, string source)
    {
        if (string.IsNullOrEmpty(dir)) return;
        try
        {
            Directory.CreateDirectory(dir);
        }
        catch (Exception ex)
            when (ex is IOException
                or UnauthorizedAccessException
                or PathTooLongException
                or NotSupportedException
                or ArgumentException)
        {
            Console.Error.WriteLine(
                $"[AppConfig] Could not create directory for {source} at '{dir}': {ex.Message}");
        }
    }
}

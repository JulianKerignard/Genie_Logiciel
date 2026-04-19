using System.Text.Json;

namespace EasySave.Services;

// Singleton holding the application configuration loaded from appsettings.json.
// Any service that needs a file path or a user-facing setting reads from AppConfig.Instance.
public sealed class AppConfig
{
    // Current configuration. Replaced once at startup by Load().
    public static AppConfig Instance { get; private set; } = new AppConfig();

    // Directory where daily log files are written.
    public string LogDirectory { get; set; } = "data/logs";

    // Full path of the real-time state file.
    public string StateFilePath { get; set; } = "data/state.json";

    // Full path of the backup jobs definitions file.
    public string JobsFilePath { get; set; } = "data/jobs.json";

    // UI language code (ISO 639-1), e.g. "en" or "fr".
    public string Language { get; set; } = "en";

    // Loads the configuration from the given JSON file, or keeps the defaults if the file is missing or invalid.
    public static void Load(string path = "appsettings.json")
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!File.Exists(path))
        {
            Instance = new AppConfig();
            return;
        }

        var json = File.ReadAllText(path);
        Instance = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasySave.Services;

// Singleton holding the application configuration loaded from appsettings.json.
// Any service that needs a file path or a user-facing setting reads from AppConfig.Instance.
public sealed class AppConfig
{
    // Current configuration. Replaced once at startup by Load().
    public static AppConfig Instance { get; private set; } = new AppConfig();

    // Directory where daily log files are written. Anchored to the executable directory.
    public string LogDirectory { get; init; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "logs");

    // Full path of the real-time state file. Anchored to the executable directory.
    public string StateFilePath { get; init; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "state.json");

    // Full path of the backup jobs definitions file. Anchored to the executable directory.
    public string JobsFilePath { get; init; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "jobs.json");

    // UI language code (ISO 639-1), e.g. "en" or "fr".
    public string Language { get; init; } = "en";

    [JsonConstructor]
    private AppConfig() { }

    // Loads the configuration from the given JSON file, or keeps the defaults if the file is missing or invalid.
    public static void Load(string path = "appsettings.json")
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!File.Exists(path))
        {
            Instance = new AppConfig();
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            Instance = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
        }
        catch (JsonException)
        {
            Instance = new AppConfig();
        }
    }
}

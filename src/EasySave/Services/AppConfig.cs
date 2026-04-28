using System.Text.Json;
using System.Text.Json.Serialization;
using EasySave.Models;

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

    // Full path of the user-managed settings file persisted by SettingsRepository.
    public string SettingsFilePath { get; init; } = Path.Combine(DataRoot, "settings.json");

    // User-managed v2.0 settings parsed from the same appsettings.json.
    // The keys live at the top level (encrypted_extensions, business_software, language, log_format, crypto_soft).
    // Language now lives inside Settings — there is no AppConfig.Language anymore.
    public AppSettings Settings { get; private set; } = new();

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
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            config.Settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            Instance = config;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            Instance = new AppConfig();
        }
    }
}

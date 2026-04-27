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

    // Absolute path to the CryptoSoft executable used for v2.0 file encryption.
    // Empty string means "not deployed yet"; consumers must skip encryption rather than fail.
    public string CryptoSoftPath { get; init; } = string.Empty;

    // Comma-separated list of file extensions (e.g. ".docx,.pdf") that must be encrypted
    // when CryptoSoftPath is set. An empty list means no file is encrypted.
    public string CryptoSoftExtensions { get; init; } = string.Empty;

    // Per-file timeout (in milliseconds) for the CryptoSoft child process.
    // Beyond this delay EasySave kills the process and logs the file as a failure.
    public int CryptoSoftTimeoutMs { get; init; } = 30000;

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
            Instance = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            Instance = new AppConfig();
        }
    }
}

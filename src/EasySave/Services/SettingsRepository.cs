using System.Text.Json;
using EasySave.Models;

namespace EasySave.Services;

// Singleton repository that persists user-managed settings to settings.json.
// SettingsChanged is raised after every successful Save so SettingsViewModel
// (or any other observer) can react to two-way binding edits.
public sealed class SettingsRepository
{
    private static readonly Lazy<SettingsRepository> _instance = new(() => new SettingsRepository());
    public static SettingsRepository Instance => _instance.Value;

    private readonly object _lock = new();

    public event EventHandler<GeneralSettings>? SettingsChanged;

    private SettingsRepository() { }

    // Loads the persisted settings from disk, or defaults if the file is missing.
    // A corrupted file is moved aside to a timestamped ".corrupted" copy so the user's
    // values are not lost silently.
    public GeneralSettings Load()
    {
        lock (_lock)
        {
            var path = AppConfig.Instance.SettingsFilePath;
            if (!File.Exists(path))
            {
                return new GeneralSettings();
            }

            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<GeneralSettings>(json) ?? new GeneralSettings();
            }
            catch (JsonException ex)
            {
                FileHelpers.QuarantineCorruptedFile(path, ex, "SettingsRepository");
                return new GeneralSettings();
            }
            catch (IOException)
            {
                return new GeneralSettings();
            }
        }
    }

    // Persists the given settings atomically and notifies subscribers.
    public void Save(GeneralSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_lock)
        {
            var path = AppConfig.Instance.SettingsFilePath;
            FileHelpers.EnsureDirectoryExists(path);
            FileHelpers.WriteAllTextAtomic(path, JsonSerializer.Serialize(settings, FileHelpers.IndentedJsonOptions));
        }

        SettingsChanged?.Invoke(this, settings);
    }
}

using System.Text.Json.Serialization;

namespace EasySave.Models;

// User-managed runtime settings persisted to settings.json under the data root.
// Distinct from AppSettings (bootstrap appsettings.json): GeneralSettings is read/write
// at runtime and edited from the GUI via SettingsViewModel two-way binding.
public sealed class GeneralSettings
{
    [JsonPropertyName("encrypted_extensions")]
    public IReadOnlyList<string> EncryptedExtensions { get; init; } = Array.Empty<string>();

    [JsonPropertyName("business_software")]
    public IReadOnlyList<string> BusinessSoftware { get; init; } = Array.Empty<string>();

    [JsonPropertyName("language")]
    public string Language { get; init; } = "en";

    [JsonPropertyName("log_format")]
    public string LogFormat { get; init; } = "json";
}

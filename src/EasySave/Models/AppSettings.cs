using System.Text.Json.Serialization;

namespace EasySave.Models;

// User-managed application settings persisted in appsettings.json.
// Bound at startup and injected into the services that need them.
public sealed class AppSettings
{
    [JsonPropertyName("encrypted_extensions")]
    public IReadOnlyList<string> EncryptedExtensions { get; init; } = Array.Empty<string>();

    [JsonPropertyName("business_software_list")]
    public IReadOnlyList<string> BusinessSoftwareList { get; init; } = Array.Empty<string>();

    [JsonPropertyName("log_format")]
    public string LogFormat { get; init; } = "json";

    [JsonPropertyName("crypto_soft")]
    public CryptoSoftSettings CryptoSoft { get; init; } = new();
}

public sealed class CryptoSoftSettings
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;
}

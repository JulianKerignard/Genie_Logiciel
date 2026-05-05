using System.Text.Json.Serialization;

namespace EasySave.Models;

// User-managed application settings. Bootstrapped from appsettings.json (read-only) and
// persisted at runtime by SettingsRepository to settings.json (read/write via the GUI).
// Single source of truth — no parallel DTO.
public sealed class AppSettings
{
    [JsonPropertyName("encrypted_extensions")]
    public IReadOnlyList<string> EncryptedExtensions { get; init; } = Array.Empty<string>();

    [JsonPropertyName("business_software")]
    public IReadOnlyList<string> BusinessSoftware { get; init; } = Array.Empty<string>();

    [JsonPropertyName("language")]
    public string Language { get; init; } = "en";

    [JsonPropertyName("log_format")]
    public string LogFormat { get; init; } = "json";

    [JsonPropertyName("crypto_soft")]
    public CryptoSoftSettings CryptoSoft { get; init; } = new();

    [JsonPropertyName("large_file_threshold_kb")]
    public int LargeFileThresholdKb { get; init; } = 4096;

    [JsonPropertyName("remote_console_enabled")]
    public bool RemoteConsoleEnabled { get; init; } = false;

    [JsonPropertyName("remote_console_port")]
    public int RemoteConsolePort { get; init; } = 9000;

    [JsonPropertyName("max_parallel_jobs")]
    public int MaxParallelJobs { get; init; } = 4;
}

public sealed class CryptoSoftSettings
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    // Per-file timeout for the CryptoSoft child process. EasySave kills the
    // process and logs the file as a failure once this delay elapses.
    [JsonPropertyName("timeout_ms")]
    public int TimeoutMs { get; init; } = 30000;
}

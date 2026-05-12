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

    // Wraps the v3 remote-console TCP socket in TLS (SslStream) using a
    // self-signed certificate generated on first run. Off by default —
    // existing deployments keep working unchanged. Recommended when the
    // console is reachable from an untrusted network (shared VPN, public
    // Wi-Fi). See docs/v3-remote-console-tls.md for cert handling.
    [JsonPropertyName("remote_console_tls_enabled")]
    public bool RemoteConsoleTlsEnabled { get; init; } = false;

    // V3 centralized logging. Default Local keeps v1/v2 deployments
    // untouched. Centralized / Both require LogCentralizedEndpoint to be
    // set: the wiring code (Program.cs / App.axaml.cs) is responsible
    // for skipping HttpLogShipper construction when the endpoint is
    // empty, in which case the logger constructors fall back to Local.
    [JsonPropertyName("log_mode")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EasyLog.LogMode LogMode { get; init; } = EasyLog.LogMode.Local;

    [JsonPropertyName("log_centralized_endpoint")]
    public string LogCentralizedEndpoint { get; init; } = string.Empty;

    // V3 priority extensions, per the cahier rule:
    //   « Aucune sauvegarde d'un fichier non prioritaire ne peut se faire
    //     tant qu'il y a des extensions prioritaires en attente sur au
    //     moins un travail. »
    // (« No non-priority file from any job may be backed up while
    // priority-extension files are still pending on at least one job. »)
    // Expected shape: [".docx", ".xlsx"]. Matching is case-insensitive
    // and expects the leading dot returned by Path.GetExtension.
    [JsonPropertyName("priority_extensions")]
    public IReadOnlyList<string> PriorityExtensions { get; init; } = Array.Empty<string>();
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

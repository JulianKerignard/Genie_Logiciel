using System.Collections.ObjectModel;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasySave.Models;
using EasySave.Services;
using EasySave.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Application = Avalonia.Application;

namespace EasySave.UI.ViewModels;

/// <summary>
/// View model for the application settings screen.
/// Exposes editable collections and scalar settings for encrypted extensions,
/// business software, log format, and the CryptoSoft executable path.
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    /// <summary>File extensions that must be encrypted during backup (e.g. ".docx").</summary>
    [ObservableProperty]
    private ObservableCollection<string> _encryptedExtensions = new();

    /// <summary>Process names that trigger an automatic backup pause (e.g. "calc.exe").</summary>
    [ObservableProperty]
    private ObservableCollection<string> _businessSoftwareList = new();

    /// <summary>Selected log serialization format. Accepted: "json", "xml".</summary>
    [ObservableProperty]
    private string _logFormat = "json";

    /// <summary>Absolute path to the CryptoSoft executable.</summary>
    [ObservableProperty]
    private string _cryptosoftPath = string.Empty;

    /// <summary>
    /// Large-file threshold in megabytes. Files at or above this size are
    /// transferred one at a time across parallel jobs (BigFileGate). The
    /// JSON key on disk is <c>large_file_threshold_kb</c>; the conversion
    /// happens in <see cref="LoadFromRepository"/> and <see cref="Save"/>.
    /// </summary>
    [ObservableProperty]
    private double _largeFileThresholdMb = DefaultThresholdKb / 1024.0;

    /// <summary>Confirmation message shown after a successful save.</summary>
    [ObservableProperty]
    private string _saveConfirmation = string.Empty;

    /// <summary>Input buffer for a new extension entry.</summary>
    [ObservableProperty]
    private string _newExtensionInput = string.Empty;

    /// <summary>Input buffer for a new business-software entry.</summary>
    [ObservableProperty]
    private string _newSoftwareInput = string.Empty;

    /// <summary>Available log format options for the ComboBox.</summary>
    public IReadOnlyList<string> LogFormatOptions { get; } = new[] { "json", "xml" };

    // Bounds enforced on Save (defense in depth — the NumericUpDown already
    // clamps spinner clicks, but a typed value can land outside the range).
    // 64 KB floor stops pathological "every file is large" serialization;
    // 10 GB ceiling stops the silent "gate is now useless" footgun.
    internal const int MinThresholdKb = 64;
    internal const int MaxThresholdKb = 10 * 1024 * 1024; // 10 GB
    internal const int DefaultThresholdKb = 4096;

    private readonly SettingsRepository _repository;
    private readonly IBigFileGate? _gate;

    public SettingsViewModel() : this(SettingsRepository.Instance, ResolveGate()) { }

    // Test seam: lets unit tests inject a repository pointed at a temp file
    // and a fake gate to assert the hot-reload push.
    internal SettingsViewModel(SettingsRepository repository, IBigFileGate? gate = null)
    {
        _repository = repository;
        _gate = gate;
        LoadFromRepository();
    }

    // Best-effort lookup of the live gate from the GUI service provider.
    // Tests construct the VM with the internal ctor and pass null/fake;
    // the parameterless ctor is used only by Avalonia's DataContext wiring
    // in production where App.Services is set.
    private static IBigFileGate? ResolveGate()
    {
        try { return App.Services?.GetService<IBigFileGate>(); }
        catch { return null; }
    }

    private void LoadFromRepository()
    {
        // settings.json is seeded from appsettings.json by App.OnFrameworkInitializationCompleted
        // on first run, so reading from the repository is enough — no boot-time fallback needed.
        var settings = _repository.Load();
        EncryptedExtensions = new ObservableCollection<string>(settings.EncryptedExtensions);
        BusinessSoftwareList = new ObservableCollection<string>(settings.BusinessSoftware);
        LogFormat = string.IsNullOrWhiteSpace(settings.LogFormat) ? "json" : settings.LogFormat;
        CryptosoftPath = settings.CryptoSoft.Path;
        // Display in MB; clamp on read so a hand-edited out-of-range value
        // surfaces as the closest valid value in the spinner instead of
        // tripping the NumericUpDown's own bounds.
        int kbOnDisk = Math.Clamp(settings.LargeFileThresholdKb, MinThresholdKb, MaxThresholdKb);
        LargeFileThresholdMb = kbOnDisk / 1024.0;
    }

    // ── Extension commands ────────────────────────────────────────────────────

    /// <summary>Adds <paramref name="ext"/> to the encrypted extensions list.</summary>
    [RelayCommand]
    private void AddExtension(string ext)
    {
        var value = ext.Trim();
        if (!string.IsNullOrWhiteSpace(value) && !EncryptedExtensions.Contains(value))
            EncryptedExtensions.Add(value);
        NewExtensionInput = string.Empty;
    }

    /// <summary>Removes <paramref name="ext"/> from the encrypted extensions list.</summary>
    [RelayCommand]
    private void RemoveExtension(string ext) => EncryptedExtensions.Remove(ext);

    // ── Business-software commands ────────────────────────────────────────────

    /// <summary>Adds <paramref name="name"/> to the business-software list.</summary>
    [RelayCommand]
    private void AddBusinessSoftware(string name)
    {
        var value = name.Trim();
        if (!string.IsNullOrWhiteSpace(value) && !BusinessSoftwareList.Contains(value))
            BusinessSoftwareList.Add(value);
        NewSoftwareInput = string.Empty;
    }

    /// <summary>Removes <paramref name="name"/> from the business-software list.</summary>
    [RelayCommand]
    private void RemoveBusinessSoftware(string name) => BusinessSoftwareList.Remove(name);

    // ── Persistence commands ──────────────────────────────────────────────────

    /// <summary>Persists all settings.</summary>
    [RelayCommand]
    private void Save()
    {
        // Validate the new threshold before touching disk: a 0 or negative
        // value crashes BigFileGate at the next boot (the ctor throws), and
        // anything > 10 GB silently disables the gate.
        int thresholdKb = (int)Math.Round(LargeFileThresholdMb * 1024);
        if (thresholdKb < MinThresholdKb || thresholdKb > MaxThresholdKb)
        {
            SaveConfirmation = TranslationSource.Instance["settings.large_file.invalid"];
            return;
        }

        // Preserve fields not surfaced by the GUI by reading them back from
        // the on-disk source of truth before overwriting. Without this, every
        // Save would silently reset MaxParallelJobs, RemoteConsole*, LogMode,
        // PriorityExtensions, … to their AppSettings defaults — a regression
        // that would wipe operator config the next time they touch a checkbox.
        // Load and Save can both throw IOException (PR #102 made the
        // propagation explicit to avoid silent data loss); surface the
        // failure to the user through the same banner instead of letting
        // [RelayCommand] propagate.
        try
        {
            var current = _repository.Load();
            var settings = new AppSettings
            {
                EncryptedExtensions = EncryptedExtensions.ToList(),
                BusinessSoftware = BusinessSoftwareList.ToList(),
                Language = current.Language,
                LogFormat = LogFormat,
                CryptoSoft = new CryptoSoftSettings
                {
                    Path = CryptosoftPath,
                    TimeoutMs = current.CryptoSoft.TimeoutMs,
                },
                LargeFileThresholdKb = thresholdKb,
                RemoteConsoleEnabled = current.RemoteConsoleEnabled,
                RemoteConsolePort = current.RemoteConsolePort,
                MaxParallelJobs = current.MaxParallelJobs,
                RemoteConsoleTlsEnabled = current.RemoteConsoleTlsEnabled,
                LogMode = current.LogMode,
                LogCentralizedEndpoint = current.LogCentralizedEndpoint,
                PriorityExtensions = current.PriorityExtensions,
            };

            _repository.Save(settings);

            // Hot-reload the live BigFileGate so the new threshold applies
            // to the next file boundary across every running job — no app
            // restart required. Tests inject a fake gate; the production
            // path resolves the singleton from App.Services.
            _gate?.SetThreshold(thresholdKb * 1024L);

            SaveConfirmation = TranslationSource.Instance["settings.saved"];
        }
        catch (IOException)
        {
            SaveConfirmation = TranslationSource.Instance["settings.save_failed"];
        }
    }

    /// <summary>Opens a file picker to set <see cref="CryptosoftPath"/>.</summary>
    [RelayCommand]
    private async Task BrowseCryptosoftPathAsync()
    {
        // TODO: inject a proper ITopLevelProvider in Phase 3
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow?.StorageProvider is { } sp)
        {
            var results = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select CryptoSoft executable",
                AllowMultiple = false,
            });
            if (results.Count > 0)
                CryptosoftPath = results[0].Path.LocalPath;
        }
    }
}

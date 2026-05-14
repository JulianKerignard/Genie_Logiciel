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
    /// Large-file threshold value, expressed in <see cref="LargeFileThresholdUnit"/>.
    /// Files at or above this size are transferred one at a time across
    /// parallel jobs (BigFileGate). The JSON key on disk is
    /// <c>large_file_threshold_kb</c>; the conversion happens in
    /// <see cref="LoadFromRepository"/> and <see cref="Save"/>.
    /// </summary>
    [ObservableProperty]
    private double _largeFileThresholdValue = DefaultThresholdKb / 1024.0;

    /// <summary>
    /// Unit shown next to <see cref="LargeFileThresholdValue"/>: "KB", "MB", or "GB".
    /// Switching the unit re-scales the displayed value so the underlying byte
    /// count stays the same (4 MB → switch to GB → 0.0039 GB).
    /// </summary>
    [ObservableProperty]
    private string _largeFileThresholdUnit = UnitMb;

    /// <summary>Confirmation message shown after a successful save.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSaveSuccessMessage))]
    [NotifyPropertyChangedFor(nameof(ShowSaveErrorMessage))]
    private string _saveConfirmation = string.Empty;

    /// <summary>
    /// True when <see cref="SaveConfirmation"/> carries a validation /
    /// failure message. Bound by the XAML to switch the message colour from
    /// success-green to error-red so a rejected save is not displayed in
    /// the same green as a successful one.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSaveSuccessMessage))]
    [NotifyPropertyChangedFor(nameof(ShowSaveErrorMessage))]
    private bool _saveIsError;

    /// <summary>True when the success-green message TextBlock should be visible.</summary>
    public bool ShowSaveSuccessMessage => !SaveIsError && !string.IsNullOrEmpty(SaveConfirmation);

    /// <summary>True when the error-red message TextBlock should be visible.</summary>
    public bool ShowSaveErrorMessage => SaveIsError && !string.IsNullOrEmpty(SaveConfirmation);

    /// <summary>Input buffer for a new extension entry.</summary>
    [ObservableProperty]
    private string _newExtensionInput = string.Empty;

    /// <summary>Input buffer for a new business-software entry.</summary>
    [ObservableProperty]
    private string _newSoftwareInput = string.Empty;

    /// <summary>Available log format options for the ComboBox.</summary>
    public IReadOnlyList<string> LogFormatOptions { get; } = new[] { "json", "xml" };

    /// <summary>Available units for the large-file threshold ComboBox.</summary>
    public IReadOnlyList<string> LargeFileThresholdUnitOptions { get; } = new[] { UnitKb, UnitMb, UnitGb };

    // Unit names used both as ComboBox options and dictionary keys for
    // KB-conversion factors. Kept as constants so XAML and tests can
    // reference them without typo risk.
    internal const string UnitKb = "KB";
    internal const string UnitMb = "MB";
    internal const string UnitGb = "GB";

    // Multiplier from each unit into KB (the on-disk storage unit).
    private static readonly Dictionary<string, double> UnitToKb = new(StringComparer.OrdinalIgnoreCase)
    {
        [UnitKb] = 1.0,
        [UnitMb] = 1024.0,
        [UnitGb] = 1024.0 * 1024.0,
    };

    // Bounds enforced on Save (defense in depth — the NumericUpDown already
    // clamps spinner clicks, but a typed value can land outside the range).
    // 64 KB floor stops pathological "every file is large" serialization;
    // 10 GB ceiling stops the silent "gate is now useless" footgun.
    internal const int MinThresholdKb = 64;
    internal const int MaxThresholdKb = 10 * 1024 * 1024; // 10 GB
    internal const int DefaultThresholdKb = 4096;

    // Set while LargeFileThresholdUnit is converting LargeFileThresholdValue
    // so the partial Value/Unit state does not double-fire conversions.
    private bool _suppressUnitConversion;

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
        // Pick the largest unit where the value rounds cleanly. Clamp on read
        // so a hand-edited out-of-range value surfaces as the closest valid
        // value instead of tripping the NumericUpDown's own bounds.
        int kbOnDisk = Math.Clamp(settings.LargeFileThresholdKb, MinThresholdKb, MaxThresholdKb);
        var (value, unit) = PickNaturalUnit(kbOnDisk);
        _suppressUnitConversion = true;
        LargeFileThresholdUnit = unit;
        LargeFileThresholdValue = value;
        _suppressUnitConversion = false;
    }

    // Picks the friendliest unit for the on-disk KB value: GB if it divides
    // evenly into 1024*1024, else MB if it divides into 1024, else KB. Avoids
    // showing "0.00390625 GB" by default for the canonical 4 MB threshold.
    private static (double value, string unit) PickNaturalUnit(int kb)
    {
        if (kb >= 1024 * 1024 && kb % (1024 * 1024) == 0)
            return (kb / (1024.0 * 1024.0), UnitGb);
        if (kb >= 1024 && kb % 1024 == 0)
            return (kb / 1024.0, UnitMb);
        return (kb, UnitKb);
    }

    // CommunityToolkit.Mvvm partial method invoked by the generated
    // LargeFileThresholdUnit setter. Re-scales the displayed value so the
    // underlying byte count survives the unit switch (4 MB → switch GB →
    // 0.00390625 GB, not "4 GB"). Suppressed during LoadFromRepository so
    // the initial assignment doesn't try to convert from a stale unit.
    partial void OnLargeFileThresholdUnitChanged(string? oldValue, string newValue)
    {
        if (_suppressUnitConversion) return;
        if (string.Equals(oldValue, newValue, StringComparison.OrdinalIgnoreCase)) return;
        if (!UnitToKb.TryGetValue(oldValue ?? string.Empty, out var oldFactor)) return;
        if (!UnitToKb.TryGetValue(newValue ?? string.Empty, out var newFactor)) return;

        double kb = LargeFileThresholdValue * oldFactor;
        _suppressUnitConversion = true;
        LargeFileThresholdValue = kb / newFactor;
        _suppressUnitConversion = false;
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
        if (!UnitToKb.TryGetValue(LargeFileThresholdUnit ?? string.Empty, out var unitFactor))
        {
            SaveIsError = true;
            SaveConfirmation = TranslationSource.Instance["settings.large_file.invalid"];
            return;
        }
        double kbExact = LargeFileThresholdValue * unitFactor;
        if (double.IsNaN(kbExact) || kbExact < MinThresholdKb || kbExact > MaxThresholdKb)
        {
            SaveIsError = true;
            SaveConfirmation = TranslationSource.Instance["settings.large_file.invalid"];
            return;
        }
        int thresholdKb = (int)Math.Round(kbExact);

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

            SaveIsError = false;
            SaveConfirmation = TranslationSource.Instance["settings.saved"];
        }
        catch (IOException)
        {
            SaveIsError = true;
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

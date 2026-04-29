using System.Collections.ObjectModel;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasySave.Services;
using EasySave.UI.Services;
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

    public SettingsViewModel()
    {
        LoadFromRepository();
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
        var current = SettingsRepository.Instance.Load();
        var settings = new EasySave.Models.AppSettings
        {
            LogFormat = LogFormat,
            CryptoSoft = new EasySave.Models.CryptoSoftSettings
            {
                Path = CryptosoftPath,
                TimeoutMs = current.CryptoSoft.TimeoutMs,
            },
            EncryptedExtensions = EncryptedExtensions.ToList(),
            BusinessSoftware = BusinessSoftwareList.ToList(),
            Language = current.Language,
        };
        SettingsRepository.Instance.Save(settings);
        SaveConfirmation = TranslationSource.Instance["settings.saved"];
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

    private void LoadFromRepository()
    {
        var stored = SettingsRepository.Instance.Load();
        var boot = EasySave.Services.AppConfig.Instance.Settings;

        LogFormat = stored.LogFormat;
        CryptosoftPath = stored.CryptoSoft.Path.Length > 0 ? stored.CryptoSoft.Path : boot.CryptoSoft.Path;

        // Prefer user-saved lists; fall back to appsettings.json defaults on first run.
        var exts = stored.EncryptedExtensions.Count > 0 ? stored.EncryptedExtensions : boot.EncryptedExtensions;
        foreach (var ext in exts) EncryptedExtensions.Add(ext);

        var sw = stored.BusinessSoftware.Count > 0 ? stored.BusinessSoftware : boot.BusinessSoftware;
        foreach (var s in sw) BusinessSoftwareList.Add(s);
    }
}

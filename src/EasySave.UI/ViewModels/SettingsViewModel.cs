using System.Collections.ObjectModel;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasySave.Models;
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

    private readonly SettingsRepository _repository;

    public SettingsViewModel() : this(SettingsRepository.Instance) { }

    // Test seam: lets unit tests inject a repository pointed at a temp file.
    internal SettingsViewModel(SettingsRepository repository)
    {
        _repository = repository;
        LoadFromRepository();
    }

    private void LoadFromRepository()
    {
        var settings = _repository.Load();
        EncryptedExtensions = new ObservableCollection<string>(settings.EncryptedExtensions);
        BusinessSoftwareList = new ObservableCollection<string>(settings.BusinessSoftware);
        LogFormat = string.IsNullOrWhiteSpace(settings.LogFormat) ? "json" : settings.LogFormat;
        CryptosoftPath = settings.CryptoSoft.Path;
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
        // Preserve fields not surfaced by the GUI (Language, CryptoSoft.TimeoutMs)
        // by reading them back from the on-disk source of truth before overwriting.
        // Load and Save can both throw IOException (PR #102 made the propagation
        // explicit to avoid silent data loss); surface the failure to the user
        // through the same banner instead of letting [RelayCommand] propagate.
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
            };

            _repository.Save(settings);
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

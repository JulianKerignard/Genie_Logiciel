using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasySave.Models;
using EasySave.UI.Services;
using Application = Avalonia.Application;

namespace EasySave.UI.ViewModels;

/// <summary>
/// View model for the job create/edit form.
/// Receives a nullable <see cref="BackupJob"/>: <c>null</c> means creation,
/// non-null means edit. Calls <paramref name="onDone"/> on Save or Cancel.
/// </summary>
public sealed partial class JobEditViewModel : ViewModelBase
{
    private readonly Action _onDone;
    private readonly BackupJob? _originalJob;

    /// <summary>Backup job name.</summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>Absolute path to the source directory.</summary>
    [ObservableProperty]
    private string _sourcePath = string.Empty;

    /// <summary>Absolute path to the destination directory.</summary>
    [ObservableProperty]
    private string _destinationPath = string.Empty;

    /// <summary>Selected backup strategy type.</summary>
    [ObservableProperty]
    private BackupType _backupType = BackupType.Full;

    /// <summary>Available backup types for the ComboBox.</summary>
    public IReadOnlyList<BackupType> BackupTypes { get; } =
        Enum.GetValues<BackupType>().ToArray();

    /// <summary>True when editing an existing job; false for creation.</summary>
    public bool IsEditing => _originalJob is not null;

    /// <summary>Form title in the active locale, adapted to creation vs edit context.</summary>
    public string Title => IsEditing
        ? TranslationSource.Instance["edit.title_edit"]
        : TranslationSource.Instance["edit.title_create"];

    public JobEditViewModel(BackupJob? job, Action onDone)
    {
        _onDone = onDone;
        _originalJob = job;

        if (job is not null)
        {
            Name = job.Name;
            SourcePath = job.SourcePath;
            DestinationPath = job.TargetPath;
            BackupType = job.Type;
        }
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    /// <summary>Validates the form and persists the job.</summary>
    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(Name)
            || string.IsNullOrWhiteSpace(SourcePath)
            || string.IsNullOrWhiteSpace(DestinationPath))
        {
            return;
        }

        // TODO: call BackupManager.AddJob() or UpdateJob() once wired
        _onDone();
    }

    /// <summary>Discards changes and navigates back.</summary>
    [RelayCommand]
    private void Cancel() => _onDone();

    /// <summary>Opens a folder picker to set <see cref="SourcePath"/>.</summary>
    [RelayCommand]
    private async Task BrowseSourceAsync()
    {
        var folder = await PickFolderAsync("Select source folder");
        if (folder is not null)
            SourcePath = folder;
    }

    /// <summary>Opens a folder picker to set <see cref="DestinationPath"/>.</summary>
    [RelayCommand]
    private async Task BrowseDestinationAsync()
    {
        var folder = await PickFolderAsync("Select destination folder");
        if (folder is not null)
            DestinationPath = folder;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task<string?> PickFolderAsync(string title)
    {
        // TODO: inject a proper ITopLevelProvider in Phase 3 for multi-window support
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow?.StorageProvider is { } sp)
        {
            var results = await sp.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
            });
            return results.Count > 0 ? results[0].Path.LocalPath : null;
        }
        return null;
    }
}

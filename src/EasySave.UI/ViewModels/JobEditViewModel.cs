using System.ComponentModel;
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
    private readonly IBackupManagerAdapter? _backup;
    private readonly Action<BackupJob, BackupJob?>? _onSaved;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

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

    /// <summary>
    /// Localized options shown in the BackupType ComboBox. Selecting an option
    /// writes its <see cref="BackupTypeOption.Type"/> back into <see cref="BackupType"/>
    /// via <c>SelectedValueBinding</c>, and each option re-raises PropertyChanged
    /// on its DisplayName when the active locale flips.
    /// </summary>
    public IReadOnlyList<BackupTypeOption> BackupTypes { get; } =
        Enum.GetValues<BackupType>().Select(t => new BackupTypeOption(t)).ToArray();

    /// <summary>True when editing an existing job; false for creation.</summary>
    public bool IsEditing => _originalJob is not null;

    /// <summary>Form title in the active locale, adapted to creation vs edit context.</summary>
    public string Title => IsEditing
        ? TranslationSource.Instance["edit.title_edit"]
        : TranslationSource.Instance["edit.title_create"];

    public JobEditViewModel(
        BackupJob? job,
        Action onDone,
        IBackupManagerAdapter? backup = null,
        Action<BackupJob, BackupJob?>? onSaved = null)
    {
        _onDone = onDone;
        _originalJob = job;
        _backup = backup;
        _onSaved = onSaved;

        if (job is not null)
        {
            Name = job.Name;
            SourcePath = job.SourcePath;
            DestinationPath = job.TargetPath;
            BackupType = job.Type;
        }

        // Refresh the form title when the user toggles FR↔EN while the JobEdit
        // view is open. The ComboBox items refresh themselves through their own
        // BackupTypeOption subscription.
        TranslationSource.Instance.PropertyChanged += OnLocaleChanged;
    }

    private void OnLocaleChanged(object? sender, PropertyChangedEventArgs e)
        => OnPropertyChanged(nameof(Title));

    // ── Commands ─────────────────────────────────────────────────────────────

    /// <summary>Validates the form and persists the job.</summary>
    [RelayCommand]
    private void Save()
    {
        ErrorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(Name)
            || string.IsNullOrWhiteSpace(SourcePath)
            || string.IsNullOrWhiteSpace(DestinationPath))
        {
            ErrorMessage = TranslationSource.Instance["edit.error_required"];
            return;
        }

        var job = new BackupJob
        {
            Name = Name.Trim(),
            SourcePath = SourcePath.Trim(),
            TargetPath = DestinationPath.Trim(),
            Type = BackupType,
        };

        try
        {
            // Persistence happens against the running BackupManager (singleton)
            // so the next View navigation sees the new job. Edit = remove + add
            // because BackupManager exposes no UpdateJob; AddJob throws on a
            // duplicate name, so we always remove the old entry first.
            if (_backup is not null)
            {
                if (_originalJob is not null)
                    _backup.RemoveJob(_originalJob.Name);
                _backup.AddJob(job);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            ErrorMessage = ex.Message;
            // Restore the deleted entry on a duplicate-add failure so the user
            // doesn't lose the original definition silently.
            if (_originalJob is not null && _backup is not null)
            {
                try { _backup.AddJob(_originalJob); } catch { /* best effort */ }
            }
            return;
        }

        _onSaved?.Invoke(job, _originalJob);
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

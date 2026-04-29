using System.Collections.ObjectModel;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasySave.Models;
using EasySave.UI.Models;
using EasySave.UI.Services;
using Application = Avalonia.Application;

namespace EasySave.UI.ViewModels;

public sealed partial class RestoreViewModel : ViewModelBase
{
    private readonly IBackupManagerAdapter _backup;
    private readonly IRestoreService _restoreService;

    public ObservableCollection<BackupJob> Jobs { get; } = new();
    public ObservableCollection<RestorePoint> RestorePoints { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRestorePoints))]
    private BackupJob? _selectedJob;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    private RestorePoint? _selectedRestorePoint;

    [ObservableProperty] private string _alternativeDestination = string.Empty;
    [ObservableProperty] private int _restoreProgress;
    [ObservableProperty] private bool _isRestoring;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private string _successMessage = string.Empty;

    public bool HasRestorePoints => RestorePoints.Count > 0;

    public RestoreViewModel(IBackupManagerAdapter backup, IRestoreService restoreService)
    {
        _backup = backup;
        _restoreService = restoreService;
        LoadJobs();
    }

    private void LoadJobs()
    {
        foreach (var job in _backup.GetJobs())
            Jobs.Add(job);
    }

    partial void OnSelectedJobChanged(BackupJob? value)
    {
        RestorePoints.Clear();
        SelectedRestorePoint = null;
        ErrorMessage = string.Empty;
        SuccessMessage = string.Empty;

        if (value is null) return;
        foreach (var point in _restoreService.GetRestorePoints(value))
            RestorePoints.Add(point);
        OnPropertyChanged(nameof(HasRestorePoints));
    }

    [RelayCommand]
    private async Task BrowseDestinationAsync()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow?.StorageProvider is { } sp)
        {
            var result = await sp.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = TranslationSource.Instance["restore.browse_title"],
                AllowMultiple = false,
            });
            if (result.Count > 0)
                AlternativeDestination = result[0].Path.LocalPath;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRestore))]
    private async Task RestoreAsync()
    {
        if (SelectedRestorePoint is null) return;

        ErrorMessage = string.Empty;
        SuccessMessage = string.Empty;
        IsRestoring = true;
        RestoreProgress = 0;

        var dest = string.IsNullOrWhiteSpace(AlternativeDestination) ? null : AlternativeDestination;
        var progress = new Progress<int>(p => RestoreProgress = p);

        try
        {
            await _restoreService.RestoreAsync(SelectedRestorePoint, dest, progress).ConfigureAwait(true);
            SuccessMessage = TranslationSource.Instance["restore.success"];
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsRestoring = false;
        }
    }

    private bool CanRestore() => SelectedRestorePoint is not null && !IsRestoring;
}

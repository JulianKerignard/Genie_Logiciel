using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasySave.Models;

namespace EasySave.UI.ViewModels;

/// <summary>
/// Root view model for the application shell. Owns the active-view state
/// and exposes navigation commands bound to the sidebar buttons.
/// </summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    /// <summary>The view model currently rendered in the main content area.</summary>
    [ObservableProperty]
    private ViewModelBase _currentView = null!;

    public MainWindowViewModel()
    {
        NavigateToJobs();
    }

    // ── Navigation ──────────────────────────────────────────────────────────

    /// <summary>Switches the content area to the backup jobs list.</summary>
    [RelayCommand]
    private void NavigateToJobs()
    {
        CurrentView = new JobsViewModel(openEdit: job => ShowJobEdit(job));
    }

    /// <summary>Switches the content area to the settings screen.</summary>
    [RelayCommand]
    private void NavigateToSettings()
    {
        CurrentView = new SettingsViewModel();
    }

    /// <summary>Switches the content area to the logs viewer.</summary>
    [RelayCommand]
    private void NavigateToLogs()
    {
        CurrentView = new LogsViewModel();
    }

    // ── Sub-navigation ───────────────────────────────────────────────────────

    /// <summary>
    /// Navigates to the job create/edit form.
    /// Pass <c>null</c> for a new job, or an existing <see cref="BackupJob"/> to edit.
    /// </summary>
    public void ShowJobEdit(BackupJob? job)
    {
        CurrentView = new JobEditViewModel(job, onDone: NavigateToJobs);
    }
}

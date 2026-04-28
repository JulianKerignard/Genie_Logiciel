using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasySave.Models;

namespace EasySave.UI.ViewModels;

/// <summary>
/// View model for the backup jobs list screen.
/// Exposes the job collection and commands to add, edit, delete, and run jobs.
/// </summary>
public sealed partial class JobsViewModel : ViewModelBase
{
    private readonly Action<BackupJob?> _openEdit;

    /// <summary>The list of configured backup jobs shown in the UI.</summary>
    public ObservableCollection<BackupJob> Jobs { get; } = new();

    /// <summary>True when the jobs collection is empty (drives the empty-state message).</summary>
    public bool IsEmpty => Jobs.Count == 0;

    public JobsViewModel(Action<BackupJob?> openEdit)
    {
        _openEdit = openEdit;
        Jobs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
        // TODO: replace with BackupManager.ListJobs() once wired
        LoadMockJobs();
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    /// <summary>Opens the job creation form.</summary>
    [RelayCommand]
    private void AddJob() => _openEdit(null);

    /// <summary>Opens the job edit form for the given job.</summary>
    [RelayCommand]
    private void EditJob(BackupJob job) => _openEdit(job);

    /// <summary>Removes the given job from the list.</summary>
    [RelayCommand]
    private void DeleteJob(BackupJob job)
    {
        Jobs.Remove(job);
        // TODO: call BackupManager.RemoveJob(job.Name)
    }

    /// <summary>Runs a single job.</summary>
    [RelayCommand]
    private void RunJob(BackupJob job)
    {
        // TODO: call BackupManager.ExecuteJob(job.Name)
    }

    /// <summary>Runs all configured jobs sequentially.</summary>
    [RelayCommand]
    private void RunAll()
    {
        // TODO: call BackupManager.ExecuteAll()
    }

    private void LoadMockJobs()
    {
        Jobs.Add(new BackupJob
        {
            Name = "Documents",
            SourcePath = @"C:\Users\user\Documents",
            TargetPath = @"D:\Backup\Documents",
            Type = BackupType.Full
        });
        Jobs.Add(new BackupJob
        {
            Name = "Photos",
            SourcePath = @"C:\Users\user\Pictures",
            TargetPath = @"\\server\backup\Photos",
            Type = BackupType.Differential
        });
    }
}

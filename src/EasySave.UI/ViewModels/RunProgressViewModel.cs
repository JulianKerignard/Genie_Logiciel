using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace EasySave.UI.ViewModels;

public sealed partial class RunProgressViewModel : ViewModelBase, IDisposable
{
    private readonly JobsViewModel _jobsVm;
    private bool _disposed;

    public ObservableCollection<BackupJobVM> Jobs => _jobsVm.Jobs;

    public bool IsBusinessSoftwareDetected => _jobsVm.IsBusinessSoftwareDetected;
    public string DetectedSoftwareName => _jobsVm.DetectedSoftwareName;

    // Set by MainWindowViewModel so this VM can trigger navigation back.
    public Action? CloseRequested { get; set; }

    public RunProgressViewModel(JobsViewModel jobsVm)
    {
        _jobsVm = jobsVm;
        _jobsVm.PropertyChanged += OnJobsVmPropertyChanged;
    }

    private void OnJobsVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(JobsViewModel.IsBusinessSoftwareDetected))
            OnPropertyChanged(nameof(IsBusinessSoftwareDetected));
        if (e.PropertyName is nameof(JobsViewModel.DetectedSoftwareName))
            OnPropertyChanged(nameof(DetectedSoftwareName));
    }

    [RelayCommand]
    private void CloseProgress() => CloseRequested?.Invoke();

    // Defensive cleanup. MainWindowViewModel currently caches this VM as a
    // navigation singleton and never disposes it (the app lives until process
    // exit, where the OS reclaims everything), so this is not a runtime leak
    // today. Implementing IDisposable keeps the contract symmetric with
    // BackupJobVM / JobEditViewModel and lets a future reset/teardown path
    // release the JobsViewModel subscription cleanly.
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _jobsVm.PropertyChanged -= OnJobsVmPropertyChanged;
    }
}

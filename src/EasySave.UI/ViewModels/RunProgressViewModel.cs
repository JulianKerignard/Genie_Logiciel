using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace EasySave.UI.ViewModels;

public sealed partial class RunProgressViewModel : ViewModelBase
{
    private readonly JobsViewModel _jobsVm;

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
}

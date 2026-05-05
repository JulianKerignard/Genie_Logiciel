using CommunityToolkit.Mvvm.ComponentModel;

namespace EasySave.RemoteConsole.ViewModels;

/// <summary>View-model that mirrors a single job's progress as reported by the server.</summary>
public sealed partial class JobProgressVm : ObservableObject
{
    /// <summary>Unique name of the backup job.</summary>
    [ObservableProperty] private string _jobName = string.Empty;

    /// <summary>Completion percentage in the range [0.0, 100.0].</summary>
    [ObservableProperty] private double _progressPercent;

    /// <summary>Human-readable status string (e.g. "Running", "Paused", "Done").</summary>
    [ObservableProperty] private string _status = string.Empty;

    /// <summary>Number of files not yet copied.</summary>
    [ObservableProperty] private long _filesRemaining;
}

using CommunityToolkit.Mvvm.ComponentModel;
using EasySave.Shared;

namespace EasySave.RemoteConsole.ViewModels;

public partial class JobProgressVm : ObservableObject
{
    [ObservableProperty] private string _jobName = string.Empty;

    // 0–100 percentage derived from BytesTotal and BytesLeft.
    [ObservableProperty] private double _progress;

    [ObservableProperty] private string _status = string.Empty;

    [ObservableProperty] private int _filesLeft;

    [ObservableProperty] private int _totalFiles;

    [ObservableProperty] private string _currentFile = string.Empty;

    public static JobProgressVm FromDto(JobProgressDto dto)
    {
        var pct = dto.BytesTotal > 0
            ? 100.0 * (dto.BytesTotal - dto.BytesLeft) / dto.BytesTotal
            : 0.0;

        return new JobProgressVm
        {
            JobName = dto.JobName,
            Progress = Math.Clamp(pct, 0, 100),
            Status = dto.State.ToString(),
            FilesLeft = dto.FilesLeft,
            TotalFiles = dto.TotalFiles,
            CurrentFile = dto.CurrentFile,
        };
    }

    public void UpdateFromDto(JobProgressDto dto)
    {
        var pct = dto.BytesTotal > 0
            ? 100.0 * (dto.BytesTotal - dto.BytesLeft) / dto.BytesTotal
            : 0.0;

        Progress = Math.Clamp(pct, 0, 100);
        Status = dto.State.ToString();
        FilesLeft = dto.FilesLeft;
        TotalFiles = dto.TotalFiles;
        CurrentFile = dto.CurrentFile;
    }
}

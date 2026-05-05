using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasySave.Shared;

namespace EasySave.RemoteConsole.ViewModels;

public partial class JobProgressVm : ObservableObject
{
    [ObservableProperty] private string _jobName = string.Empty;

    // 0–100 percentage derived from BytesTotal and BytesLeft.
    [ObservableProperty] private double _progress;

    [ObservableProperty] private string _status = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilesSummary))]
    private int _filesLeft;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilesSummary))]
    private int _totalFiles;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilesSummary))]
    private string _currentFile = string.Empty;

    // Pre-bound per-item commands injected by ConsoleViewModel.FromDto so the
    // DataTemplate can bind directly without referencing the parent ViewModel.
    public ICommand? PauseCommand { get; init; }
    public ICommand? PlayCommand  { get; init; }
    public ICommand? StopCommand  { get; init; }

    public string FilesSummary => $"{FilesLeft} / {TotalFiles} files — {CurrentFile}";

    public static JobProgressVm FromDto(JobProgressDto dto, Func<CommandDto, Task> sendCommand)
        => new()
        {
            JobName     = dto.JobName,
            Progress    = BytePercent(dto.BytesTotal, dto.BytesLeft),
            Status      = dto.State.ToString(),
            FilesLeft   = dto.FilesLeft,
            TotalFiles  = dto.TotalFiles,
            CurrentFile = dto.CurrentFile,
            PauseCommand = new AsyncRelayCommand(() => sendCommand(new CommandDto(dto.JobName, CommandType.Pause))),
            PlayCommand  = new AsyncRelayCommand(() => sendCommand(new CommandDto(dto.JobName, CommandType.Play))),
            StopCommand  = new AsyncRelayCommand(() => sendCommand(new CommandDto(dto.JobName, CommandType.Stop))),
        };

    public void UpdateFromDto(JobProgressDto dto)
    {
        Progress    = BytePercent(dto.BytesTotal, dto.BytesLeft);
        Status      = dto.State.ToString();
        FilesLeft   = dto.FilesLeft;
        TotalFiles  = dto.TotalFiles;
        CurrentFile = dto.CurrentFile;
    }

    private static double BytePercent(long bytesTotal, long bytesLeft)
        => bytesTotal > 0 ? Math.Clamp(100.0 * (bytesTotal - bytesLeft) / bytesTotal, 0, 100) : 0.0;
}

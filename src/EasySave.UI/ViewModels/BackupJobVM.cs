using CommunityToolkit.Mvvm.ComponentModel;
using EasySave.Models;
using EasySave.UI.Services;

namespace EasySave.UI.ViewModels;

public sealed partial class BackupJobVM : ObservableObject
{
    public BackupJob Model { get; }

    public string Name       => Model.Name;
    public string SourcePath => Model.SourcePath;
    public string TargetPath => Model.TargetPath;

    public string BackupTypeName => Model.Type == EasySave.BackupType.Full
        ? TranslationSource.Instance["edit.type_full"]
        : TranslationSource.Instance["edit.type_diff"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyPropertyChangedFor(nameof(StateDisplayName))]
    private UiJobState _uiState = UiJobState.Idle;

    [ObservableProperty] private int _progress;
    [ObservableProperty] private string _currentFile = string.Empty;
    [ObservableProperty] private int _filesRemaining;

    public bool IsRunning => UiState == UiJobState.Running;

    public string StateDisplayName => UiState switch
    {
        UiJobState.Running => TranslationSource.Instance["jobs.state.active"],
        UiJobState.Paused  => TranslationSource.Instance["jobs.state.paused"],
        _                  => TranslationSource.Instance["jobs.state.idle"],
    };

    public BackupJobVM(BackupJob model) => Model = model;
}

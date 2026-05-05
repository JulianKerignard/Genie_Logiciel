using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasySave.RemoteConsole.Abstractions;

namespace EasySave.RemoteConsole.ViewModels;

/// <summary>Root view-model for the remote console window.</summary>
public sealed partial class ConsoleViewModel : ObservableObject
{
    /// <summary>Live list of backup jobs reported by the server.</summary>
    [ObservableProperty] private ObservableCollection<JobProgressVm> _jobs = new();

    /// <summary>Current TCP connection state.</summary>
    [ObservableProperty] private RemoteConnectionState _connection = RemoteConnectionState.Disconnected;

    /// <summary>Server host or IP address to connect to.</summary>
    [ObservableProperty] private string _host = "127.0.0.1";

    /// <summary>Server TCP port.</summary>
    [ObservableProperty] private int _port = 9000;

    /// <summary>Initiates a connection to the configured host and port.</summary>
    [RelayCommand]
    private void Connect() => throw new NotImplementedException();

    /// <summary>Closes the current server connection.</summary>
    [RelayCommand]
    private void Disconnect() => throw new NotImplementedException();

    /// <summary>Sends a Pause command for <paramref name="jobName"/> to the server.</summary>
    [RelayCommand]
    private void PauseJob(string jobName) => throw new NotImplementedException();

    /// <summary>Sends a Play (resume) command for <paramref name="jobName"/> to the server.</summary>
    [RelayCommand]
    private void PlayJob(string jobName) => throw new NotImplementedException();

    /// <summary>Sends a Stop command for <paramref name="jobName"/> to the server.</summary>
    [RelayCommand]
    private void StopJob(string jobName) => throw new NotImplementedException();
}

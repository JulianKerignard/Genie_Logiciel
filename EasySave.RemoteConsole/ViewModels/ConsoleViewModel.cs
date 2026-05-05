using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasySave.RemoteConsole.Abstractions;

namespace EasySave.RemoteConsole.ViewModels;

/// <summary>Root view-model for the remote console window.</summary>
public sealed partial class ConsoleViewModel : ObservableObject, IDisposable
{
    private readonly IRemoteConsoleClient _client;
    private readonly IDisposable _stateSubscription;

    public ConsoleViewModel(IRemoteConsoleClient client)
    {
        _client = client;
        _stateSubscription = _client.ConnectionState
            .Subscribe(new StateObserver(s => Connection = s));
    }

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
    private Task ConnectAsync(CancellationToken ct) => Task.FromException(new NotImplementedException());

    /// <summary>Closes the current server connection.</summary>
    [RelayCommand]
    private Task DisconnectAsync() => Task.FromException(new NotImplementedException());

    /// <summary>Sends a Pause command for <paramref name="jobName"/> to the server.</summary>
    [RelayCommand]
    private Task PauseJobAsync(string jobName) => Task.FromException(new NotImplementedException());

    /// <summary>Sends a Play (resume) command for <paramref name="jobName"/> to the server.</summary>
    [RelayCommand]
    private Task PlayJobAsync(string jobName) => Task.FromException(new NotImplementedException());

    /// <summary>Sends a Stop command for <paramref name="jobName"/> to the server.</summary>
    [RelayCommand]
    private Task StopJobAsync(string jobName) => Task.FromException(new NotImplementedException());

    /// <inheritdoc/>
    public void Dispose() => _stateSubscription.Dispose();

    // Bridges IObservable<RemoteConnectionState> (BCL) to an Action without a Rx dependency.
    private sealed class StateObserver(Action<RemoteConnectionState> onNext)
        : IObserver<RemoteConnectionState>
    {
        public void OnNext(RemoteConnectionState value) => onNext(value);
        public void OnError(Exception error) => onNext(RemoteConnectionState.Error);
        public void OnCompleted() { }
    }
}

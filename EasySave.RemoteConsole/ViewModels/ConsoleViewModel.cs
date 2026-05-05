using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasySave.RemoteConsole.Abstractions;
using EasySave.Shared;

namespace EasySave.RemoteConsole.ViewModels;

/// <summary>Root view-model for the remote console window.</summary>
public sealed partial class ConsoleViewModel : ObservableObject, IDisposable
{
    private readonly IRemoteConsoleClient _client;
    private readonly IDisposable _stateSubscription;
    private readonly Func<EventDto, Task> _eventHandler;

    public ConsoleViewModel(IRemoteConsoleClient client)
    {
        _client = client;
        _stateSubscription = _client.ConnectionState
            .Subscribe(new StateObserver(s => Connection = s));
        _eventHandler = OnEventReceived;
        _client.EventReceived += _eventHandler;
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
    private async Task ConnectAsync(CancellationToken ct)
    {
        try
        {
            await _client.ConnectAsync(Host, Port, ct);
        }
        catch (Exception)
        {
            // Connection state is updated via the observable stream.
        }
    }

    /// <summary>Closes the current server connection.</summary>
    [RelayCommand]
    private Task DisconnectAsync() => _client.DisconnectAsync();

    /// <summary>Sends a Pause command for <paramref name="jobName"/> to the server.</summary>
    [RelayCommand]
    private Task PauseJobAsync(string jobName) =>
        _client.SendCommandAsync(new CommandDto(jobName, CommandType.Pause));

    /// <summary>Sends a Play (resume) command for <paramref name="jobName"/> to the server.</summary>
    [RelayCommand]
    private Task PlayJobAsync(string jobName) =>
        _client.SendCommandAsync(new CommandDto(jobName, CommandType.Play));

    /// <summary>Sends a Stop command for <paramref name="jobName"/> to the server.</summary>
    [RelayCommand]
    private Task StopJobAsync(string jobName) =>
        _client.SendCommandAsync(new CommandDto(jobName, CommandType.Stop));

    /// <inheritdoc/>
    public void Dispose()
    {
        _stateSubscription.Dispose();
        _client.EventReceived -= _eventHandler;
    }

    private Task OnEventReceived(EventDto evt)
    {
        switch (evt.Type)
        {
            case EventType.JobList when evt.Jobs is not null:
                Dispatcher.UIThread.Post(() =>
                {
                    Jobs.Clear();
                    foreach (var j in evt.Jobs)
                        Jobs.Add(BuildVm(j));
                });
                break;

            case EventType.JobProgress when evt.Progress is not null:
                var progress = evt.Progress;
                Dispatcher.UIThread.Post(() =>
                {
                    var existing = Jobs.FirstOrDefault(j => j.JobName == progress.JobName);
                    if (existing is not null)
                    {
                        existing.ProgressPercent = ComputePercent(progress);
                        existing.Status = progress.State.ToString();
                        existing.FilesRemaining = progress.FilesLeft;
                    }
                    else
                    {
                        Jobs.Add(BuildVm(progress));
                    }
                });
                break;
        }
        return Task.CompletedTask;
    }

    private static JobProgressVm BuildVm(JobProgressDto dto) => new()
    {
        JobName = dto.JobName,
        ProgressPercent = ComputePercent(dto),
        Status = dto.State.ToString(),
        FilesRemaining = dto.FilesLeft,
    };

    private static double ComputePercent(JobProgressDto dto) =>
        dto.TotalFiles == 0 ? 0.0 : 100.0 * (dto.TotalFiles - dto.FilesLeft) / dto.TotalFiles;

    // Bridges IObservable<RemoteConnectionState> (BCL) to an Action without a Rx dependency.
    private sealed class StateObserver(Action<RemoteConnectionState> onNext)
        : IObserver<RemoteConnectionState>
    {
        public void OnNext(RemoteConnectionState value) => onNext(value);
        public void OnError(Exception error) => onNext(RemoteConnectionState.Error);
        public void OnCompleted() { }
    }
}

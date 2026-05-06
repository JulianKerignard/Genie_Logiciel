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
    private async Task ConnectAsync(CancellationToken ct)
    {
        _client.EventReceived += OnEventReceivedAsync;
        await _client.ConnectAsync(Host, Port, ct);
    }

    /// <summary>Closes the current server connection.</summary>
    [RelayCommand]
    private async Task DisconnectAsync()
    {
        _client.EventReceived -= OnEventReceivedAsync;
        await _client.DisconnectAsync();
    }

    /// <summary>Sends a Pause command for <paramref name="jobName"/> to the server.</summary>
    [RelayCommand]
    private Task PauseJobAsync(string jobName)
        => _client.SendCommandAsync(new CommandDto(jobName, CommandType.Pause));

    /// <summary>Sends a Play (resume) command for <paramref name="jobName"/> to the server.</summary>
    [RelayCommand]
    private Task PlayJobAsync(string jobName)
        => _client.SendCommandAsync(new CommandDto(jobName, CommandType.Play));

    /// <summary>Sends a Stop command for <paramref name="jobName"/> to the server.</summary>
    [RelayCommand]
    private Task StopJobAsync(string jobName)
        => _client.SendCommandAsync(new CommandDto(jobName, CommandType.Stop));

    private Task OnEventReceivedAsync(EventDto evt)
    {
        if (evt.Type == EventType.JobProgress && evt.Progress is { } p)
            Dispatcher.UIThread.Post(() => ApplyProgress(p));
        else if (evt.Type == EventType.JobList && evt.Jobs is { } jobs)
            Dispatcher.UIThread.Post(() => ApplyJobList(jobs));
        return Task.CompletedTask;
    }

    private void ApplyProgress(JobProgressDto p)
    {
        var vm = Jobs.FirstOrDefault(j => j.JobName == p.JobName);
        if (vm is null)
        {
            vm = new JobProgressVm { JobName = p.JobName };
            Jobs.Add(vm);
        }
        vm.ProgressPercent = p.TotalFiles == 0
            ? 0
            : (p.TotalFiles - p.FilesLeft) * 100.0 / p.TotalFiles;
        vm.Status = p.State.ToString();
        vm.FilesRemaining = p.FilesLeft;
    }

    private void ApplyJobList(IReadOnlyList<JobProgressDto> jobs)
    {
        Jobs.Clear();
        foreach (var j in jobs)
        {
            Jobs.Add(new JobProgressVm
            {
                JobName = j.JobName,
                ProgressPercent = j.TotalFiles == 0
                    ? 0
                    : (j.TotalFiles - j.FilesLeft) * 100.0 / j.TotalFiles,
                Status = j.State.ToString(),
                FilesRemaining = j.FilesLeft,
            });
        }
    }

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

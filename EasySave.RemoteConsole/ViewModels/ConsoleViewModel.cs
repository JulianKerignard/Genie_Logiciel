using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasySave.RemoteConsole.Abstractions;
using EasySave.RemoteConsole.Services;
using EasySave.Shared;

namespace EasySave.RemoteConsole.ViewModels;

/// <summary>Root view-model for the remote console window.</summary>
public sealed partial class ConsoleViewModel : ObservableObject, IDisposable
{
    private readonly IRemoteConsoleClient _client;
    private readonly LanguageService _lang;
    private readonly IDisposable _stateSubscription;

    public ConsoleViewModel(IRemoteConsoleClient client, LanguageService lang)
    {
        _client = client;
        _lang = lang;
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

    public string LabelConnect => _lang.T("btn.connect");
    public string LabelDisconnect => _lang.T("btn.disconnect");

    /// <summary>Initiates a connection to the configured host and port.</summary>
    [RelayCommand]
    private async Task ConnectAsync(CancellationToken ct)
    {
        // Ensure the handler is registered exactly once even if ConnectAsync is called again.
        _client.EventReceived -= OnEventReceivedAsync;
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
        vm.ProgressPercent = ToPercent(p);
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
                ProgressPercent = ToPercent(j),
                Status = j.State.ToString(),
                FilesRemaining = j.FilesLeft,
            });
        }
    }

    private static double ToPercent(JobProgressDto p)
        => p.TotalFiles == 0 ? 0 : (p.TotalFiles - p.FilesLeft) * 100.0 / p.TotalFiles;

    /// <inheritdoc/>
    public void Dispose()
    {
        _stateSubscription.Dispose();
        if (_client is IAsyncDisposable ad)
            _ = ad.DisposeAsync().AsTask();
        else if (_client is IDisposable d)
            d.Dispose();
    }

    // Bridges IObservable<RemoteConnectionState> (BCL) to an Action without a Rx dependency.
    private sealed class StateObserver(Action<RemoteConnectionState> onNext)
        : IObserver<RemoteConnectionState>
    {
        public void OnNext(RemoteConnectionState value) => onNext(value);
        public void OnError(Exception error) => onNext(RemoteConnectionState.Error);
        public void OnCompleted() { }
    }
}

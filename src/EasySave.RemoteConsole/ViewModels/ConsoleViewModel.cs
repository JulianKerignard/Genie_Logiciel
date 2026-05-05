using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasySave.RemoteConsole.Infrastructure;
using EasySave.Shared;

namespace EasySave.RemoteConsole.ViewModels;

public sealed partial class ConsoleViewModel : ObservableObject, IDisposable
{
    private readonly IRemoteConsoleClient _client;
    private readonly IDisposable _stateSubscription;
    private CancellationTokenSource? _connectCts;

    [ObservableProperty] private string _host = "localhost";
    [ObservableProperty] private int _port = 9000;
    [ObservableProperty] private ConnectionState _connection = ConnectionState.Disconnected;

    public ObservableCollection<JobProgressVm> Jobs { get; } = new();

    public ConsoleViewModel(IRemoteConsoleClient client)
    {
        _client = client;

        _stateSubscription = _client.ConnectionState.Subscribe(
            new LambdaObserver<ConnectionState>(state =>
                Dispatcher.UIThread.Post(() => Connection = state)));

        _client.EventReceived += evt =>
            Dispatcher.UIThread.Post(() => HandleEvent(evt));
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        _connectCts?.Cancel();
        _connectCts = new CancellationTokenSource();
        await _client.ConnectAsync(Host, Port, _connectCts.Token);
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        _connectCts?.Cancel();
        await _client.DisconnectAsync();
    }

    [RelayCommand]
    private async Task PauseJob(string? jobName)
    {
        if (jobName is null) return;
        await _client.SendCommandAsync(new CommandDto(jobName, CommandType.Pause));
    }

    [RelayCommand]
    private async Task PlayJob(string? jobName)
    {
        if (jobName is null) return;
        await _client.SendCommandAsync(new CommandDto(jobName, CommandType.Play));
    }

    [RelayCommand]
    private async Task StopJob(string? jobName)
    {
        if (jobName is null) return;
        await _client.SendCommandAsync(new CommandDto(jobName, CommandType.Stop));
    }

    private void HandleEvent(EventDto evt)
    {
        switch (evt.Type)
        {
            case EventType.JobList when evt.Jobs is not null:
                RebuildJobList(evt.Jobs);
                break;

            case EventType.JobProgress when evt.Progress is not null:
                UpsertJob(evt.Progress);
                break;

            case EventType.JobStarted:
            case EventType.JobResumed:
                SetJobStatus(evt.JobName, JobStateEnum.Running.ToString());
                break;

            case EventType.JobPaused:
                SetJobStatus(evt.JobName, JobStateEnum.Paused.ToString());
                break;

            case EventType.JobFinished:
            case EventType.JobFailed:
                SetJobStatus(evt.JobName, JobStateEnum.Done.ToString());
                break;
        }
    }

    private void RebuildJobList(IReadOnlyList<JobProgressDto> dtos)
    {
        Jobs.Clear();
        foreach (var dto in dtos)
            Jobs.Add(JobProgressVm.FromDto(dto));
    }

    private void UpsertJob(JobProgressDto dto)
    {
        var existing = Jobs.FirstOrDefault(j => j.JobName == dto.JobName);
        if (existing is not null)
            existing.UpdateFromDto(dto);
        else
            Jobs.Add(JobProgressVm.FromDto(dto));
    }

    private void SetJobStatus(string? jobName, string status)
    {
        if (jobName is null) return;
        var vm = Jobs.FirstOrDefault(j => j.JobName == jobName);
        if (vm is not null) vm.Status = status;
    }

    public void Dispose()
    {
        _stateSubscription.Dispose();
        _connectCts?.Cancel();
        _connectCts?.Dispose();
    }

    private sealed class LambdaObserver<T>(Action<T> onNext) : IObserver<T>
    {
        public void OnNext(T value) => onNext(value);
        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }
}

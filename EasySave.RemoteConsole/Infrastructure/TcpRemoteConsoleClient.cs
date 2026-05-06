using System.Net.Sockets;
using System.Text.Json;
using EasySave.RemoteConsole.Abstractions;
using EasySave.Shared;

namespace EasySave.RemoteConsole.Infrastructure;

public sealed class TcpRemoteConsoleClient : IRemoteConsoleClient, IDisposable
{
    private readonly ConnectionStateSubject _stateSubject = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private CancellationTokenSource _cts = new();
    private TcpClient? _tcp;
    private StreamWriter? _writer;
    private string _host = string.Empty;
    private int _port;
    private int _reconnectAttempt;

    private static readonly int[] BackoffDelaysMs = [1000, 2000, 5000, 10000];

    public event Func<EventDto, Task> EventReceived = _ => Task.CompletedTask;
    public IObservable<RemoteConnectionState> ConnectionState => _stateSubject;

    public async Task ConnectAsync(string host, int port, CancellationToken ct)
    {
        _host = host;
        _port = port;
        _reconnectAttempt = 0;

        _cts.Cancel();
        _cts.Dispose();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            await ConnectInternalAsync(_cts.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Publish so the UI exits the Connecting state even on the first attempt.
            _stateSubject.Publish(RemoteConnectionState.Disconnected);
            throw;
        }
    }

    public Task DisconnectAsync()
    {
        _cts.Cancel();
        try { _tcp?.Close(); } catch (Exception) { }
        _stateSubject.Publish(RemoteConnectionState.Disconnected);
        return Task.CompletedTask;
    }

    public async Task SendCommandAsync(CommandDto cmd)
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_writer is null) return;
            var json = JsonSerializer.Serialize(cmd);
            await _writer.WriteLineAsync(json).ConfigureAwait(false);
            await _writer.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ConnectInternalAsync(CancellationToken ct)
    {
        _stateSubject.Publish(RemoteConnectionState.Connecting);

        var tcp = new TcpClient();
        await tcp.ConnectAsync(_host, _port, ct).ConfigureAwait(false);

        var stream = tcp.GetStream();
        var writer = new StreamWriter(stream, leaveOpen: true) { AutoFlush = false };

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _tcp?.Close();
            _tcp = tcp;
            _writer = writer;
        }
        finally
        {
            _writeLock.Release();
        }

        _stateSubject.Publish(RemoteConnectionState.Connected);

        _ = Task.Run(() => ReadLoopAsync(new StreamReader(stream, leaveOpen: true), ct), ct);
    }

    private async Task ReadLoopAsync(StreamReader reader, CancellationToken ct)
    {
        using (reader)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                    if (line is null) break;

                    EventDto? evt;
                    try { evt = JsonSerializer.Deserialize<EventDto>(line); }
                    catch (JsonException) { continue; }

                    if (evt is null) continue;

                    foreach (var handler in EventReceived.GetInvocationList().Cast<Func<EventDto, Task>>())
                    {
                        try { await handler(evt).ConfigureAwait(false); }
                        catch (Exception) { }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
        }

        if (ct.IsCancellationRequested) return;

        _stateSubject.Publish(RemoteConnectionState.Disconnected);
        _ = Task.Run(() => ReconnectAsync(ct), ct);
    }

    private async Task ReconnectAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var delay = BackoffDelaysMs[Math.Min(_reconnectAttempt, BackoffDelaysMs.Length - 1)];
            _reconnectAttempt++;

            try { await Task.Delay(delay, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }

            try
            {
                await ConnectInternalAsync(ct).ConfigureAwait(false);
                _reconnectAttempt = 0;
                return;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) when (ex is SocketException or IOException or ObjectDisposedException)
            {
                _stateSubject.Publish(RemoteConnectionState.Disconnected);
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _tcp?.Dispose();
        _writeLock.Dispose();
        _cts.Dispose();
    }

    // Minimal IObservable<T> subject — no System.Reactive dependency.
    private sealed class ConnectionStateSubject : IObservable<RemoteConnectionState>
    {
        private readonly List<IObserver<RemoteConnectionState>> _observers = [];
        private readonly object _lock = new();
        private RemoteConnectionState _current = RemoteConnectionState.Disconnected;

        public void Publish(RemoteConnectionState state)
        {
            IObserver<RemoteConnectionState>[] snapshot;
            lock (_lock)
            {
                _current = state;
                snapshot = [.. _observers];
            }
            foreach (var o in snapshot)
                try { o.OnNext(state); } catch (Exception) { }
        }

        public IDisposable Subscribe(IObserver<RemoteConnectionState> observer)
        {
            RemoteConnectionState current;
            lock (_lock)
            {
                _observers.Add(observer);
                current = _current;
            }
            // Call outside the lock: observer.OnNext may set Avalonia properties
            // that trigger re-entrant Subscribe calls, causing a deadlock if called
            // while _lock is held.
            observer.OnNext(current);
            return new Subscription(() =>
            {
                lock (_lock) { _observers.Remove(observer); }
            });
        }

        private sealed class Subscription(Action onDispose) : IDisposable
        {
            public void Dispose() => onDispose();
        }
    }
}

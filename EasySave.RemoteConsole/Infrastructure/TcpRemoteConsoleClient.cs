using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using EasySave.RemoteConsole.Abstractions;
using EasySave.Shared;

namespace EasySave.RemoteConsole.Infrastructure;

public sealed class TcpRemoteConsoleClient : IRemoteConsoleClient, IAsyncDisposable
{
    private static readonly int[] BackoffSeconds = [1, 2, 5, 10, 10, 10];

    private readonly SimpleSubject<RemoteConnectionState> _stateSubject = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly KnownHostsStore _knownHosts;
    private CancellationTokenSource? _cts;
    private TcpClient? _tcp;
    private SslStream? _sslStream;
    private StreamWriter? _writer;
    private string _host = string.Empty;
    private int _port;

    public event Func<EventDto, Task>? EventReceived;
    public IObservable<RemoteConnectionState> ConnectionState => _stateSubject;
    public bool UseTls { get; set; }

    public TcpRemoteConsoleClient() : this(new KnownHostsStore(KnownHostsStore.DefaultPath())) { }
    public TcpRemoteConsoleClient(KnownHostsStore knownHosts) => _knownHosts = knownHosts;

    public async Task ConnectAsync(string host, int port, CancellationToken ct)
    {
        // Cancel and release any existing connection before opening a new one.
        _cts?.Cancel();
        _cts?.Dispose();
        _sslStream?.Dispose();
        _sslStream = null;
        _tcp?.Close();
        _tcp = null;

        _host = host;
        _port = port;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _stateSubject.OnNext(RemoteConnectionState.Connecting);
        try
        {
            _tcp = new TcpClient();
            await _tcp.ConnectAsync(_host, _port, _cts.Token);

            Stream stream = await UpgradeStreamAsync(_tcp.GetStream(), _cts.Token);

            _writer = new StreamWriter(stream, Encoding.UTF8, bufferSize: 4096, leaveOpen: true)
            { AutoFlush = false };
            _stateSubject.OnNext(RemoteConnectionState.Connected);
            _ = ReadLoopAsync(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            // The caller (or DisposeAsync) cancelled the initial attempt —
            // surface Disconnected, not Error, so the UI stays neutral.
            _stateSubject.OnNext(RemoteConnectionState.Disconnected);
            await CleanupAfterFailedConnectAsync();
            throw;
        }
        catch
        {
            // TCP connect refused / unreachable, TLS handshake failure,
            // TOFU thumbprint mismatch — same Error state as the reconnect
            // loop. Without this catch the state subject would sit on
            // Connecting forever and the UI label would freeze.
            _stateSubject.OnNext(RemoteConnectionState.Error);
            await CleanupAfterFailedConnectAsync();
            throw;
        }
    }

    // Releases the half-built TCP / TLS / writer state after a failed
    // ConnectAsync so the next attempt starts from a clean baseline and the
    // disposed _tcp does not leak its socket handle until the next call.
    // _writer is disposed under _writeLock to match DisconnectAsync — a
    // failed initial connect with an in-flight SendCommandAsync would
    // otherwise race the dispose against the write, even if narrow.
    private async Task CleanupAfterFailedConnectAsync()
    {
        await _writeLock.WaitAsync();
        try
        {
            try { _writer?.Dispose(); } catch { }
            _writer = null;
            try { _sslStream?.Dispose(); } catch { }
            _sslStream = null;
        }
        finally { _writeLock.Release(); }
        try { _tcp?.Close(); } catch { }
        _tcp = null;
    }

    // Upgrades the raw network stream to TLS when UseTls is set, applying
    // the TOFU known_hosts policy. Returns either the SslStream (TLS) or
    // the original NetworkStream (plain). A handshake failure or a TOFU
    // mismatch surfaces as an exception out of the caller so ConnectAsync
    // does not silently fall back to plain TCP.
    private async Task<Stream> UpgradeStreamAsync(NetworkStream raw, CancellationToken ct)
    {
        if (!UseTls) return raw;

        // Authenticate on a local SslStream first and only publish it to the
        // _sslStream field on success. A failed handshake (TOFU mismatch,
        // protocol issue, network error) disposes the stream right here so
        // we don't carry an unusable SslStream until the next Connect /
        // Disconnect runs the cleanup at the top of this class.
        var ssl = new SslStream(raw, leaveInnerStreamOpen: false,
            userCertificateValidationCallback: ValidateServerCertificate);
        try
        {
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = _host,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            }, ct);
        }
        catch
        {
            ssl.Dispose();
            throw;
        }
        _sslStream = ssl;
        return ssl;
    }

    // SslStream certificate callback applying trust-on-first-use:
    //   - first contact for (_host, _port) → pin the cert thumbprint and
    //     accept.
    //   - subsequent contact → accept iff the thumbprint still matches.
    //     A mismatch returns false, which SslStream surfaces as
    //     AuthenticationException out of AuthenticateAsClientAsync, and
    //     the operator can investigate via the known_hosts file.
    // sslPolicyErrors are ignored on purpose for the self-signed case —
    // the thumbprint comparison is the trust anchor, not the CA chain.
    private bool ValidateServerCertificate(
        object sender,
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors sslPolicyErrors)
    {
        if (certificate is null) return false;
        var current = certificate.GetCertHashString();

        if (_knownHosts.TryGetThumbprint(_host, _port, out var pinned))
            return string.Equals(pinned, current, StringComparison.OrdinalIgnoreCase);

        _knownHosts.Add(_host, _port, current);
        return true;
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        int attempt = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Read from the same upgraded stream we wrote to — if TLS
                // is on the reader must speak SslStream, not the raw socket.
                Stream readStream = _sslStream is not null ? _sslStream : _tcp!.GetStream();
                using var reader = new StreamReader(
                    readStream, Encoding.UTF8, false, -1, leaveOpen: true);
                while (!ct.IsCancellationRequested)
                {
                    string? line;
                    try { line = await reader.ReadLineAsync(ct); }
                    catch (OperationCanceledException) { return; }
                    if (line is null) break;

                    attempt = 0;
                    EventDto? evt;
                    try { evt = JsonSerializer.Deserialize<EventDto>(line); }
                    catch { continue; }
                    if (evt is null) continue;

                    await FireEventReceivedAsync(evt);
                }
            }
            catch (OperationCanceledException) { return; }
            catch { /* network disconnect — fall through to reconnect */ }

            if (ct.IsCancellationRequested) return;
            _stateSubject.OnNext(RemoteConnectionState.Disconnected);

            var delay = BackoffSeconds[Math.Min(attempt, BackoffSeconds.Length - 1)];
            attempt++;
            try { await Task.Delay(TimeSpan.FromSeconds(delay), ct); }
            catch (OperationCanceledException) { return; }

            _stateSubject.OnNext(RemoteConnectionState.Connecting);
            try
            {
                _sslStream?.Dispose();
                _sslStream = null;
                _tcp?.Close();
                _tcp = new TcpClient();
                await _tcp.ConnectAsync(_host, _port, ct);

                Stream stream = await UpgradeStreamAsync(_tcp.GetStream(), ct);
                await _writeLock.WaitAsync(ct);
                try
                {
                    _writer?.Dispose();
                    _writer = new StreamWriter(stream, Encoding.UTF8, 4096, leaveOpen: true)
                    { AutoFlush = false };
                }
                finally { _writeLock.Release(); }
                _stateSubject.OnNext(RemoteConnectionState.Connected);
            }
            catch (OperationCanceledException) { return; }
            catch (AuthenticationException)
            {
                // TOFU mismatch or handshake failure — non-retriable. No
                // amount of backoff fixes a pinned thumbprint that no
                // longer matches; the operator must edit known_hosts.txt
                // (or accept the new server cert in any other way). Exit
                // the loop so the UI sticks on Error and the user can
                // intervene.
                _stateSubject.OnNext(RemoteConnectionState.Error);
                return;
            }
            catch { _stateSubject.OnNext(RemoteConnectionState.Error); }
        }
    }

    private async Task FireEventReceivedAsync(EventDto evt)
    {
        var handler = EventReceived;
        if (handler is null) return;
        foreach (var h in handler.GetInvocationList().Cast<Func<EventDto, Task>>())
        {
            try { await h(evt); } catch { }
        }
    }

    public async Task SendCommandAsync(CommandDto cmd)
    {
        var json = JsonSerializer.Serialize(cmd);
        await _writeLock.WaitAsync();
        try
        {
            if (_writer is null) return;
            await _writer.WriteLineAsync(json);
            await _writer.FlushAsync();
        }
        finally { _writeLock.Release(); }
    }

    public async Task DisconnectAsync()
    {
        _cts?.Cancel();
        await _writeLock.WaitAsync();
        try
        {
            _writer?.Dispose();
            // Dispose the SslStream before closing the TcpClient so the
            // TLS shutdown alert is written cleanly (SslStream.Dispose
            // sends close_notify when possible).
            _sslStream?.Dispose();
            _sslStream = null;
        }
        finally { _writeLock.Release(); }
        _tcp?.Close();
        _stateSubject.OnNext(RemoteConnectionState.Disconnected);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _cts?.Dispose();
        _writeLock.Dispose();
        _stateSubject.OnCompleted();
    }
}

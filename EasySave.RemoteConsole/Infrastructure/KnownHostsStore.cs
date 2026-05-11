namespace EasySave.RemoteConsole.Infrastructure;

// TOFU (trust-on-first-use) registry for the v3 TLS handshake, mirroring
// the spirit of OpenSSH's ~/.ssh/known_hosts. One line per host of the
// form "<host>:<port> <thumbprint>" — the thumbprint is the server
// certificate's SHA-1 hex string. On every connection the client either:
//   • finds the host and asserts the thumbprint still matches (rejects
//     the connection on mismatch — a different cert means either the
//     server was reissued or the client is talking to an impostor), or
//   • does not find the host and appends a new pin (first contact).
// File layout matches the OpenSSH precedent rather than JSON so the
// operator can edit / wipe entries with any text editor.
public sealed class KnownHostsStore
{
    private readonly string _path;

    public KnownHostsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ProSoft", "EasySave.RemoteConsole", "known_hosts.txt");

    public bool TryGetThumbprint(string host, int port, out string thumbprint)
    {
        thumbprint = string.Empty;
        if (!File.Exists(_path)) return false;

        var key = Key(host, port);
        foreach (var rawLine in File.ReadAllLines(_path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            // "host:port thumbprint" — split once so a malformed thumbprint
            // with embedded whitespace still rejects cleanly.
            int space = line.IndexOf(' ');
            if (space <= 0 || space == line.Length - 1) continue;

            if (string.Equals(line[..space], key, StringComparison.OrdinalIgnoreCase))
            {
                thumbprint = line[(space + 1)..].Trim();
                return thumbprint.Length > 0;
            }
        }
        return false;
    }

    public void Add(string host, int port, string thumbprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(thumbprint);
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.AppendAllText(_path, $"{Key(host, port)} {thumbprint}{Environment.NewLine}");
    }

    private static string Key(string host, int port) => $"{host}:{port}";
}

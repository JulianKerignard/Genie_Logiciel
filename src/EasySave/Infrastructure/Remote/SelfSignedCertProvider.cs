using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace EasySave.Infrastructure.Remote;

// Loads (or generates on first run) the self-signed X.509 certificate the v3
// remote-console server uses for TLS. The certificate lives on disk as a
// password-less PFX — protection comes from filesystem ACLs rather than a
// key passphrase, which matches the "self-signed dev cert" use case. To
// switch to a real CA-issued cert, drop the PFX with the same filename
// (operator's responsibility) and EasySave will load it as-is.
public static class SelfSignedCertProvider
{
    // Default location: %AppData%\ProSoft\EasySave\cert.pfx on Windows, the
    // platform-equivalent XDG / Library path on Linux / macOS.
    public static string DefaultCertPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ProSoft", "EasySave", "cert.pfx");

    // Loads the PFX at certPath, or creates a fresh self-signed cert and
    // writes it there if the file does not exist. The returned certificate
    // is exportable so SslStream can use the private key for the TLS
    // handshake.
    public static X509Certificate2 LoadOrCreate(string certPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(certPath);

        if (File.Exists(certPath))
        {
            return new X509Certificate2(
                certPath, password: (string?)null,
                keyStorageFlags: X509KeyStorageFlags.EphemeralKeySet
                                 | X509KeyStorageFlags.Exportable);
        }

        var dir = Path.GetDirectoryName(certPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var cert = Generate();
        var pfx = cert.Export(X509ContentType.Pfx);
        File.WriteAllBytes(certPath, pfx);
        return cert;
    }

    // RSA 2048 with a 5-year validity, subject "CN=EasySave RemoteConsole
    // Server". 2048 bits is the SslStream minimum on .NET 8 and matches what
    // makecert / openssl produce for development certificates. EphemeralKeySet
    // keeps the private key in memory only (no machine-wide CNG store entry
    // and no admin requirement on first run); the PFX file is the only
    // persistence boundary.
    public static X509Certificate2 Generate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=EasySave RemoteConsole Server",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        var now = DateTimeOffset.UtcNow;
        // Dispose the intermediate certificate — its key handle wraps an
        // unmanaged CNG resource that the re-imported copy below does not
        // share, so leaking it accumulates handles per restart.
        using var cert = request.CreateSelfSigned(notBefore: now.AddMinutes(-5),
                                                  notAfter: now.AddYears(5));

        // CreateSelfSigned returns a cert whose key is not directly
        // exportable on Windows. Re-import via PFX bytes so the returned
        // certificate carries the private key in an Exportable form, which
        // is what SslStream and File.WriteAllBytes both need.
        var bytes = cert.Export(X509ContentType.Pfx);
        return new X509Certificate2(
            bytes, password: (string?)null,
            keyStorageFlags: X509KeyStorageFlags.EphemeralKeySet
                             | X509KeyStorageFlags.Exportable);
    }
}

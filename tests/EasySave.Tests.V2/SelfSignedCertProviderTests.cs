using System.Security.Cryptography.X509Certificates;
using EasySave.Infrastructure.Remote;

namespace EasySave.Tests.V2;

public class SelfSignedCertProviderTests : IDisposable
{
    private readonly string _dir;

    public SelfSignedCertProviderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cert-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Generate_ProducesUsableCertificateWithPrivateKey()
    {
        using var cert = SelfSignedCertProvider.Generate();

        Assert.True(cert.HasPrivateKey);
        Assert.Contains("CN=EasySave RemoteConsole Server", cert.Subject);
        Assert.True(cert.NotAfter > DateTime.UtcNow.AddYears(4));
    }

    [Fact]
    public void LoadOrCreate_FirstCall_CreatesPfxFile()
    {
        var path = Path.Combine(_dir, "cert.pfx");
        Assert.False(File.Exists(path));

        using var cert = SelfSignedCertProvider.LoadOrCreate(path);

        Assert.True(File.Exists(path));
        Assert.True(cert.HasPrivateKey);
    }

    [Fact]
    public void LoadOrCreate_SecondCall_ReturnsSameCertificate()
    {
        // The TOFU contract on the client side hinges on the server cert
        // not rotating silently — LoadOrCreate must reuse the existing PFX
        // across restarts so pinned thumbprints stay valid.
        var path = Path.Combine(_dir, "cert.pfx");

        using var first = SelfSignedCertProvider.LoadOrCreate(path);
        using var second = SelfSignedCertProvider.LoadOrCreate(path);

        Assert.Equal(first.Thumbprint, second.Thumbprint);
    }

    [Fact]
    public void LoadOrCreate_CreatesDirectoryStructureIfMissing()
    {
        var nested = Path.Combine(_dir, "a", "b", "cert.pfx");
        Assert.False(Directory.Exists(Path.GetDirectoryName(nested)!));

        using var cert = SelfSignedCertProvider.LoadOrCreate(nested);

        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void LoadOrCreate_NullOrWhitespacePath_Throws()
    {
        Assert.Throws<ArgumentException>(() => SelfSignedCertProvider.LoadOrCreate(""));
        Assert.Throws<ArgumentException>(() => SelfSignedCertProvider.LoadOrCreate("   "));
    }
}

namespace EasySave.Services;

/// <summary>
/// No-op <see cref="IEncryptionService"/> used when CryptoSoft is not
/// configured (empty <c>crypto_soft.path</c>) or in tests that do not
/// exercise encryption. Always returns a failure with the canonical
/// <c>-1</c> error code so the caller falls back to a plain copy.
/// </summary>
public sealed class NoOpEncryptionService : IEncryptionService
{
    /// <inheritdoc />
    public EncryptResult Encrypt(string source, string dest) => EncryptResult.Failed();
}

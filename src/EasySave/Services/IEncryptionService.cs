namespace EasySave.Services;

/// <summary>
/// Abstraction over the file-encryption side-channel. Decouples
/// <see cref="BackupManager"/> from the concrete CryptoSoft executable so
/// tests, alternate implementations (no-op, in-memory, future engines) and
/// the production wrapper can be swapped behind the same surface.
/// </summary>
public interface IEncryptionService
{
    /// <summary>
    /// Reads <paramref name="source"/>, encrypts its content, and writes the
    /// encrypted bytes to <paramref name="dest"/>. Implementations are
    /// expected to be synchronous and to surface failures via the returned
    /// <see cref="EncryptResult"/> rather than by throwing.
    /// </summary>
    /// <param name="source">Absolute path to the plaintext file. Must exist.</param>
    /// <param name="dest">Absolute path where the encrypted file is written. Parent directory must exist.</param>
    EncryptResult Encrypt(string source, string dest);
}

/// <summary>
/// Outcome of a single <see cref="IEncryptionService.Encrypt"/> call.
/// <paramref name="EncryptionTimeMs"/> follows the v2.0 logging convention:
/// non-negative value = elapsed encryption time, negative value = failure.
/// </summary>
public record EncryptResult(bool Success, long EncryptionTimeMs);

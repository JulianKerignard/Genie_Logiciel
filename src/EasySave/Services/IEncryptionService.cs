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
    /// True when the underlying encryption tool is reachable and configured.
    /// When false, callers must fall back to a plain copy and log
    /// <c>EncryptionTimeMs = 0</c> per the v2.0 logging convention
    /// (<c>docs/cryptosoft-integration.md</c>): the path-empty case is
    /// "encryption not performed", not "encryption failed".
    /// </summary>
    bool IsAvailable { get; }

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

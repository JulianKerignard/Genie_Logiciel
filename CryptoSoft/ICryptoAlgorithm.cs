namespace CryptoSoft;

/// <summary>
/// Pure-byte transformation strategy. Lets <see cref="CryptoSoftRunner"/>
/// stay agnostic of the encryption primitive (XOR for demo, AES later
/// without touching the orchestrator).
/// </summary>
internal interface ICryptoAlgorithm
{
    /// <summary>
    /// Returns the encrypted (or decrypted, for a symmetric algorithm)
    /// byte representation of <paramref name="source"/>. Must not mutate
    /// the input.
    /// </summary>
    byte[] Transform(byte[] source);
}

namespace CryptoSoft;

/// <summary>
/// Single-byte repeating XOR cipher. Symmetric (Transform applied twice
/// returns the original content), stream-friendly, and zero-allocation
/// except for the output buffer. Demo-grade only — not a real
/// cryptographic primitive. A production deployment would substitute an
/// AES-backed <see cref="ICryptoAlgorithm"/> without touching the
/// orchestrator.
/// </summary>
internal sealed class XorCryptoAlgorithm : ICryptoAlgorithm
{
    private readonly byte _key;

    public XorCryptoAlgorithm(byte key) => _key = key;

    public byte[] Transform(byte[] source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var output = new byte[source.Length];
        for (int i = 0; i < source.Length; i++)
            output[i] = (byte)(source[i] ^ _key);
        return output;
    }
}

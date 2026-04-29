namespace EasySave.Services;

/// <summary>
/// Outcome of a single <see cref="IEncryptionService.Encrypt"/> call.
/// <para>
/// <c>EncryptionTimeMs</c> follows the v2.0 logging convention: a non-negative
/// value is the elapsed encryption time in milliseconds; a negative value is
/// an opaque error code. The convention matches <c>FileTransferTimeMs</c>
/// from EasyLog v1.0 and the CryptoSoft exit-code contract documented in
/// <c>docs/cryptosoft-integration.md</c>.
/// </para>
/// <para>
/// Invariant: <c>Success == true</c> if and only if
/// <c>EncryptionTimeMs &gt;= 0</c>. The public positional constructor cannot
/// enforce this — prefer the <see cref="Succeeded"/> and <see cref="Failed"/>
/// factories, which both validate their argument and produce a consistent
/// result.
/// </para>
/// </summary>
public sealed record EncryptResult(bool Success, long EncryptionTimeMs)
{
    /// <summary>
    /// Builds a success result. Throws when <paramref name="elapsedMs"/> is
    /// negative, since a negative time would conflict with the failure
    /// convention.
    /// </summary>
    public static EncryptResult Succeeded(long elapsedMs)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(elapsedMs);
        return new EncryptResult(true, elapsedMs);
    }

    /// <summary>
    /// Builds a failure result. The default <c>-1</c> error code matches the
    /// v1.0 / v2.0 convention used by EasyLog. Throws when
    /// <paramref name="errorCode"/> is non-negative — a non-negative value
    /// would be parsed as a successful elapsed time by downstream consumers.
    /// </summary>
    public static EncryptResult Failed(long errorCode = -1)
    {
        if (errorCode >= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(errorCode),
                "Failure error codes must be negative to match the v2.0 logging convention.");
        }
        return new EncryptResult(false, errorCode);
    }
}

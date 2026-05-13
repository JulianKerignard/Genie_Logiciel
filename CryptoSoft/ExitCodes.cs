namespace CryptoSoft;

/// <summary>
/// Reserved exit codes returned by CryptoSoft to its parent process (EasySave).
/// The integration contract reuses the exit code for two purposes:
/// non-negative values carry the elapsed encryption time in ms; negative
/// values are stable, mutually-exclusive error codes. See
/// <c>docs/cryptosoft-integration.md</c> for the full contract.
/// </summary>
internal static class ExitCodes
{
    public const int InvalidArguments = -1;

    /// <summary>Another CryptoSoft is already running on this machine (cahier V3 mono-instance).</summary>
    public const int AlreadyRunning = -2;

    public const int SourceNotFound = -3;

    public const int IoFailure = -4;
}

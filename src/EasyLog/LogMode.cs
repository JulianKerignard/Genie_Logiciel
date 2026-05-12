namespace EasyLog;

/// <summary>
/// Selects where <see cref="IDailyLogger.Append"/> calls land.
/// V3 introduces a centralized HTTP shipper; existing v1/v2 deployments
/// keep the local-only behaviour as the implicit default.
/// </summary>
public enum LogMode
{
    /// <summary>
    /// Write the daily file on the local host only. v1/v2 behaviour, default.
    /// </summary>
    Local = 0,

    /// <summary>
    /// Ship to the centralized HTTP collector only. The local daily file is
    /// skipped so a single source of truth lives in the central service.
    /// </summary>
    Centralized = 1,

    /// <summary>
    /// Ship to the central collector AND keep writing the local daily file.
    /// Useful during cut-over while operators learn the central dashboard.
    /// </summary>
    Both = 2,
}

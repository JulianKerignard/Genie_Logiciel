using EasyLog;
using EasySave.Models;

namespace EasySave.Services;

// Builds an IDailyLogger from AppSettings, wiring an HttpLogShipper when
// LogMode is Centralized / Both AND LogCentralizedEndpoint is set. Both
// CLI (Program.cs) and UI (App.axaml.cs) call into this so the centralized
// shipping feature behaves identically regardless of entry point. Returns
// the shipper alongside so callers can keep it alive for IDisposable
// teardown — JsonDailyLogger / XmlDailyLogger do not own the shipper.
public static class DailyLoggerFactory
{
    public static (IDailyLogger Logger, HttpLogShipper? Shipper) Create(
        string logDirectory,
        string logFormat,
        LogMode logMode,
        string centralizedEndpoint)
    {
        HttpLogShipper? shipper = null;

        // Only build a shipper when the operator opted in AND gave us a
        // reachable endpoint. An empty endpoint with mode=Centralized would
        // otherwise silently drop every entry: the logger ctors guard for
        // this (LogRouter.Effective falls back to Local) but we prefer to
        // never construct the shipper at all in that case.
        // Uri.TryCreate accepts file://, ftp://, etc. — HttpLogShipper's
        // ctor would then throw ArgumentException. Filter to http/https
        // here so a typo in appsettings.json silently falls back to Local
        // instead of crashing the entry point.
        if (logMode != LogMode.Local
            && !string.IsNullOrWhiteSpace(centralizedEndpoint)
            && Uri.TryCreate(centralizedEndpoint, UriKind.Absolute, out var endpoint)
            && (endpoint.Scheme == Uri.UriSchemeHttp || endpoint.Scheme == Uri.UriSchemeHttps))
        {
            shipper = new HttpLogShipper(endpoint);
        }

        var effectiveMode = shipper is null ? LogMode.Local : logMode;

        IDailyLogger logger = logFormat.Equals("xml", StringComparison.OrdinalIgnoreCase)
            ? new XmlDailyLogger(logDirectory, shipper, effectiveMode)
            : new JsonDailyLogger(logDirectory, shipper, effectiveMode);

        return (logger, shipper);
    }
}

namespace LogCentralizer.Tests;

// Named time constants used across the LogCentralizer test suite. Centralising
// them here avoids magic-number drift between the in-process and e2e suites
// (a 20 s timeout that means "wait for 150 entries to flush" is conceptually
// different from a 20 s timeout that means "wait for /health to respond" —
// the names below preserve that distinction without sprinkling raw seconds
// across call sites).
internal static class TestTimeouts
{
    // Default poll deadline for LogFilePoller.WaitForLinesAsync. Generous on
    // purpose: a slow CI runner can take a few seconds just to flush a
    // single entry through Channel + File.AppendAllTextAsync.
    public static readonly TimeSpan FilePollDefault = TimeSpan.FromSeconds(15);

    // Container e2e: wait for ~150 entries to flush through the real container's
    // filesystem layer (bind-mount has more latency than native fs).
    public static readonly TimeSpan ContainerFlushTimeout = TimeSpan.FromSeconds(20);

    // Container e2e: overall budget for /health to respond after the
    // container is started. Bounds the bug class where the in-image app
    // crashes at startup (Testcontainers .NET issue #1639 documented in
    // LogCentralizerE2EFixture).
    public static readonly TimeSpan HealthProbeDeadline = TimeSpan.FromSeconds(30);

    // Container e2e: per-request HTTP timeout for the readiness probe loop.
    // Short enough to fail fast on a stuck app, long enough to ride out a
    // momentarily slow Kestrel boot.
    public static readonly TimeSpan HealthProbeRequestTimeout = TimeSpan.FromSeconds(2);

    // Container e2e: spacing between consecutive readiness probes.
    public static readonly TimeSpan HealthProbePollInterval = TimeSpan.FromMilliseconds(250);
}

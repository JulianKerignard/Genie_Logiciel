namespace LogCentralizer.Tests;

// Shared polling helper for tests that need to wait until the LogCentralizer
// background writer has flushed N entries to disk. Used by both the
// in-process suite (LogCentralizerTests) and the Testcontainers e2e suite
// (LogCentralizerE2ETests) — the writer is async in both setups, so an
// assertion fired right after PostAsync would race the channel drain.
//
// FileShare.ReadWrite is intentional: the writer keeps the file open in
// append mode while polling reads it, and we never want the polling side
// to block writes. Splitting on '\r' AND '\n' covers Windows CI runners
// where Environment.NewLine = "\r\n" — without that, the trailing '\r'
// would slip into the token and JsonSerializer.Deserialize would throw.
internal static class LogFilePoller
{
    public static async Task<string[]> WaitForLinesAsync(
        string logsDir, int expected, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (DateTime.UtcNow < deadline)
        {
            var files = Directory.GetFiles(logsDir, "*.jsonl");
            if (files.Length > 0)
            {
                using var fs = new FileStream(
                    files[0], FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                var content = await sr.ReadToEndAsync();
                var lines = content.Split(
                    new[] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length >= expected) return lines;
            }
            await Task.Delay(100);
        }
        throw new Xunit.Sdk.XunitException(
            $"Expected {expected} persisted lines under {logsDir} within timeout.");
    }
}

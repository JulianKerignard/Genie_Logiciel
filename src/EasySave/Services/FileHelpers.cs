using System.Text.Json;

namespace EasySave.Services;

// Shared filesystem and JSON utilities for services that persist state on disk.
internal static class FileHelpers
{
    // JSON serializer settings shared by every persistence call so files stay uniform and human-readable.
    public static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

    // Creates the parent directory of the given path if it does not already exist.
    public static void EnsureDirectoryExists(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    // Writes the given text to the target path via a unique temp file, then renames it
    // over the target in a single OS call. The GUID-suffixed temp name avoids collisions
    // between concurrent processes writing to the same target, and a failed write deletes
    // the temp file instead of leaving an orphan on disk.
    public static void WriteAllTextAtomic(string path, string contents)
    {
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(tempPath, contents);
            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception)
        {
            try { File.Delete(tempPath); }
            catch { /* best effort; do not mask the original exception */ }
            throw;
        }
    }

    // Renames a corrupted persistence file to a timestamped backup so operators can inspect
    // it later. Used by JobRepository and StateTracker to avoid silently dropping user data
    // when jobs.json or state.json cannot be deserialized. If the rename itself fails, the
    // caller keeps running with empty state (best effort, we never mask the original error).
    public static void QuarantineCorruptedFile(string path, Exception reason, string loggerTag)
    {
        try
        {
            var quarantinePath = $"{path}.corrupted-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
            File.Move(path, quarantinePath);
            Console.Error.WriteLine(
                $"[{loggerTag}] {Path.GetFileName(path)} was unreadable and has been moved to " +
                $"{Path.GetFileName(quarantinePath)}. Reason: {reason.Message}");
        }
        catch
        {
            // best effort — if the rename fails the caller continues with empty state
        }
    }
}

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
}

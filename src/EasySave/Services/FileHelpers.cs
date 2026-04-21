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
}

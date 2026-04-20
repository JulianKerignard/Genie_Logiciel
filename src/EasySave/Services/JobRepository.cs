using System.Text.Json;
using EasySave.Models;

namespace EasySave.Services;

// Singleton repository that persists the list of user-defined backup jobs to jobs.json.
// The full list is rewritten atomically on each Save to avoid partial files on crash.
public sealed class JobRepository
{
    // Lazy, thread-safe singleton instance.
    private static readonly Lazy<JobRepository> _instance = new(() => new JobRepository());
    public static JobRepository Instance => _instance.Value;

    // Serialises concurrent Load and Save calls.
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions _serializerOptions = new() { WriteIndented = true };

    private JobRepository() { }

    // Loads the persisted list of backup jobs from disk, or an empty list if the file is missing or corrupted.
    public IReadOnlyList<BackupJob> Load()
    {
        lock (_lock)
        {
            var path = AppConfig.Instance.JobsFilePath;
            if (!File.Exists(path))
            {
                return new List<BackupJob>();
            }

            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<List<BackupJob>>(json) ?? new List<BackupJob>();
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // Treat a corrupted or transiently unreadable jobs file as empty
                // so the application can still start.
                return new List<BackupJob>();
            }
        }
    }

    // Persists the given list of backup jobs atomically to disk.
    public void Save(IReadOnlyList<BackupJob> jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);

        lock (_lock)
        {
            var path = AppConfig.Instance.JobsFilePath;
            EnsureDirectoryExists(path);

            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(jobs, _serializerOptions));
            File.Move(tempPath, path, overwrite: true);
        }
    }

    private static void EnsureDirectoryExists(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}

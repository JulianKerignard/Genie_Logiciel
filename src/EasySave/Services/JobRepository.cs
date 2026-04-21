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

    private JobRepository() { }

    // Loads the persisted list of backup jobs from disk, or an empty list if the file is missing.
    // If the file is present but corrupted, it is moved aside to a timestamped ".corrupted" copy
    // so the user's definitions are not lost silently.
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
                QuarantineCorruptedFile(path, ex);
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
            FileHelpers.EnsureDirectoryExists(path);

            FileHelpers.WriteAllTextAtomic(path, JsonSerializer.Serialize(jobs, FileHelpers.IndentedJsonOptions));
        }
    }

    // Renames a corrupted jobs file so the user can inspect or recover it later,
    // instead of silently dropping their backup job definitions.
    private static void QuarantineCorruptedFile(string path, Exception reason)
    {
        try
        {
            var quarantinePath = $"{path}.corrupted.{DateTime.UtcNow:yyyyMMddHHmmss}";
            File.Move(path, quarantinePath);
            Console.Error.WriteLine(
                $"[JobRepository] {Path.GetFileName(path)} was unreadable and has been moved to " +
                $"{Path.GetFileName(quarantinePath)}. Reason: {reason.Message}");
        }
        catch
        {
            // If the rename itself fails, fall back to returning an empty list so the app keeps running.
        }
    }
}

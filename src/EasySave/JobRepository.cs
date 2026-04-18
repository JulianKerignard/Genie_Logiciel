using EasySave.Models;

namespace EasySave;

// Singleton repository that persists the list of user-defined backup jobs.
public sealed class JobRepository
{
    // Lazy, thread-safe singleton instance.
    private static readonly Lazy<JobRepository> _instance = new(() => new JobRepository());
    public static JobRepository Instance => _instance.Value;

    private JobRepository() { }

    // Loads the persisted list of backup jobs from disk.
    public IReadOnlyList<BackupJob> Load()
    {
        throw new NotImplementedException();
    }

    // Persists the given list of backup jobs to disk.
    public void Save(IReadOnlyList<BackupJob> jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        throw new NotImplementedException();
    }
}

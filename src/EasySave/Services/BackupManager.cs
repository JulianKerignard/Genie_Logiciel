using EasyLog;
using EasySave.Models;

namespace EasySave.Services;

// Manages backup jobs. Stubs only — implementation in Phase 3.
public class BackupManager
{
    private readonly IDailyLogger _logger;
    private readonly FullBackupStrategy _fullStrategy;
    private readonly DifferentialBackupStrategy _diffStrategy;
    private readonly StateTracker _stateTracker;
    private readonly JobRepository _jobRepository;

    public BackupManager(
        IDailyLogger logger,
        FullBackupStrategy fullStrategy,
        DifferentialBackupStrategy diffStrategy,
        StateTracker stateTracker,
        JobRepository jobRepository)
    {
        _logger = logger;
        _fullStrategy = fullStrategy;
        _diffStrategy = diffStrategy;
        _stateTracker = stateTracker;
        _jobRepository = jobRepository;
    }

    public void AddJob(BackupJob job) => throw new NotImplementedException();

    public void RemoveJob(string name) => throw new NotImplementedException();

    public IReadOnlyList<BackupJob> ListJobs() => throw new NotImplementedException();

    public void ExecuteJob(string name) => throw new NotImplementedException();

    public void ExecuteAll() => throw new NotImplementedException();
}

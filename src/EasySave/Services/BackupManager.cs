using EasyLog;
using EasySave.Models;

namespace EasySave.Services;

// Manages backup jobs. Stubs only — implementation in Phase 3.
public class BackupManager
{
    private readonly IDailyLogger _logger;
    private readonly IBackupStrategy _fullStrategy;
    private readonly IBackupStrategy _diffStrategy;
    private readonly StateTracker _stateTracker;
    private readonly JobRepository _jobRepository;

    public BackupManager(
        IDailyLogger logger,
        IBackupStrategy fullStrategy,
        IBackupStrategy diffStrategy,
        StateTracker stateTracker,
        JobRepository jobRepository)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(fullStrategy);
        ArgumentNullException.ThrowIfNull(diffStrategy);
        ArgumentNullException.ThrowIfNull(stateTracker);
        ArgumentNullException.ThrowIfNull(jobRepository);

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

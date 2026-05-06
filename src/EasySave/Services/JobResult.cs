namespace EasySave.Services;

// Outcome of a single job run, returned by IParallelBackupOrchestrator
// .RunAsync once every job in the batch has finished, failed or been
// cancelled. Timestamps use the server clock; Message carries the failure
// reason when Outcome is Failed and is null otherwise.
public sealed record JobResult(
    string JobName,
    JobOutcome Outcome,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    string? Message = null
);

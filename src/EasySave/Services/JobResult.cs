namespace EasySave.Services;

// Outcome of a single job run, returned by IParallelBackupOrchestrator
// .RunAsync once every job in the batch has finished, failed or been
// cancelled. Timestamps use the server clock. Message is human-readable
// context: typically the failure reason when Outcome is Failed and the
// cancellation source when Outcome is Cancelled (e.g. "stopped via remote
// console", "batch token cancelled"); always null when Outcome is
// Succeeded.
public sealed record JobResult(
    string JobName,
    JobOutcome Outcome,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    string? Message = null
);

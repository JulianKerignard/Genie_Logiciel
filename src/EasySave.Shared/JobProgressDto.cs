namespace EasySave.Shared;

// Snapshot of a single job's progress, serialized to JSON and pushed by the
// engine to the remote console as the Progress payload of a JobProgress event.
// Field names match the v3 protocol spec verbatim so the JSON wire format is
// stable across client and server. Byte counters are int64 to support files
// larger than 2 GiB.
public sealed record JobProgressDto(
    string JobName,
    JobStateEnum State,
    string CurrentFile,
    int FilesLeft,
    int TotalFiles,
    long BytesLeft,
    long BytesTotal
);

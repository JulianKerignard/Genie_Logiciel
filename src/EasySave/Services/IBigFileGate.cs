namespace EasySave.Services;

// Global gate that limits concurrent transfers of "large" files to one at a
// time across the whole engine, regardless of how many jobs are running in
// parallel. Without it, two parallel jobs each starting a multi-GB copy would
// saturate the disk or the network and slow each other down. Files smaller
// than LargeFileThresholdBytes are not gated and copy freely.
//
// Usage:
//
//     using (await gate.AcquireAsync(fileSize, ct))
//     {
//         CopyFile(source, target);
//     }
//
// The IDisposable returned by AcquireAsync releases the slot for the next
// waiter when Dispose() runs.
public interface IBigFileGate
{
    // Threshold above which (>=) a file is considered "large" and gated.
    // Expressed in bytes for direct comparison with fileSizeBytes; the
    // concrete implementation is responsible for reading AppSettings
    // .LargeFileThresholdKb and multiplying by 1024 to land here. long
    // matches the parameter type below and the rest of the codebase
    // (FileInfo.Length, LogEntry.FileSize) so the comparison never loses
    // bits on multi-GB thresholds.
    long LargeFileThresholdBytes { get; }

    // Acquires a slot for the file about to be transferred. Returns
    // immediately if fileSizeBytes < LargeFileThresholdBytes (small file —
    // never gated). Otherwise waits asynchronously until the single large-
    // file slot is free.
    //
    // The returned IDisposable must be kept alive for the duration of the
    // copy and released (Dispose) once the file has finished transferring.
    // Cancelling the token while waiting throws OperationCanceledException
    // and does not consume the slot.
    Task<IDisposable> AcquireAsync(long fileSizeBytes, CancellationToken ct);
}

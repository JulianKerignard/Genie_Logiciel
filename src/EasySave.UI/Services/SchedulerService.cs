using System.Text.Json;
using EasySave.Services;
using EasySave.UI.Models;

namespace EasySave.UI.Services;

/// <summary>
/// File-backed scheduler service. Schedules are persisted to
/// <c>%AppData%\ProSoft\EasySave\schedules.json</c> alongside jobs.json.
/// </summary>
public sealed class SchedulerService : ISchedulerService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private string SchedulesFilePath =>
        Path.Combine(
            Path.GetDirectoryName(AppConfig.Instance.JobsFilePath)
                ?? AppDomain.CurrentDomain.BaseDirectory,
            "schedules.json");

    /// <inheritdoc />
    /// <remarks>
    /// Transient IOException is propagated to the caller. Swallowing it and returning
    /// an empty list would let the next SaveAll() write the empty list over the existing
    /// file and silently wipe every persisted schedule (issue #111, same trap as #69 / #97).
    /// </remarks>
    public IReadOnlyList<ScheduledJob> GetAll()
    {
        var path = SchedulesFilePath;
        if (!File.Exists(path)) return Array.Empty<ScheduledJob>();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<ScheduledJob>>(json) ?? new List<ScheduledJob>();
        }
        catch (JsonException ex)
        {
            FileHelpers.QuarantineCorruptedFile(path, ex, "SchedulerService");
            return Array.Empty<ScheduledJob>();
        }
    }

    /// <inheritdoc />
    public void SaveAll(IEnumerable<ScheduledJob> schedules)
    {
        ArgumentNullException.ThrowIfNull(schedules);
        var path = SchedulesFilePath;
        FileHelpers.EnsureDirectoryExists(path);
        FileHelpers.WriteAllTextAtomic(path, JsonSerializer.Serialize(schedules.ToList(), JsonOptions));
    }
}

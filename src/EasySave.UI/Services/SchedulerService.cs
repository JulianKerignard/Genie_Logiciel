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
            Path.GetDirectoryName(AppConfig.Instance.JobsFilePath)!,
            "schedules.json");

    /// <inheritdoc />
    public IReadOnlyList<ScheduledJob> GetAll()
    {
        var path = SchedulesFilePath;
        if (!File.Exists(path)) return Array.Empty<ScheduledJob>();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<ScheduledJob>>(json) ?? new List<ScheduledJob>();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
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

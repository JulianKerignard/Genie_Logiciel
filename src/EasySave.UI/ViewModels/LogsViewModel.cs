using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasySave.Services;
using EasySave.UI.Services;

namespace EasySave.UI.ViewModels;

/// <summary>
/// View model for the logs viewer screen. Lists daily log files written by
/// EasyLog (JSON or XML) and shows a bounded preview of the selected file
/// so a huge daily log does not block the UI thread or balloon memory.
/// </summary>
public sealed partial class LogsViewModel : ViewModelBase
{
    // Hard cap on lines streamed into SelectedContent. A pretty-printed JSON
    // backup-row entry is ~11 lines, so 150 lines is ~13 entries — enough to
    // see the latest activity. Files like the 18 MB XML log we hit in
    // testing would otherwise lock the UI for several seconds.
    private const int MaxPreviewLines = 150;

    public ObservableCollection<LogFileItem> Files { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private LogFileItem? _selectedFile;

    [ObservableProperty]
    private string _selectedContent = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TruncatedBadge))]
    private bool _isTruncated;

    public bool HasSelection => SelectedFile is not null;
    public bool IsEmpty => Files.Count == 0;

    public string TruncatedBadge => string.Format(
        TranslationSource.Instance["logs.truncated_badge"], MaxPreviewLines);

    public LogsViewModel()
    {
        Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        Files.Clear();
        StatusMessage = string.Empty;
        SelectedContent = string.Empty;
        IsTruncated = false;
        SelectedFile = null;

        var dir = AppConfig.Instance.LogDirectory;
        if (!Directory.Exists(dir))
        {
            StatusMessage = $"({dir})";
            OnPropertyChanged(nameof(IsEmpty));
            return;
        }

        // Show JSON and XML daily files, newest first. The .yyyy-MM-dd prefix
        // sorts lexicographically so a string sort gives the desired order.
        var paths = Directory.GetFiles(dir, "*.json")
            .Concat(Directory.GetFiles(dir, "*.xml"))
            .OrderByDescending(p => p, StringComparer.Ordinal);

        foreach (var path in paths)
            Files.Add(new LogFileItem(path));

        if (Files.Count > 0)
            SelectedFile = Files[0];

        OnPropertyChanged(nameof(IsEmpty));
    }

    partial void OnSelectedFileChanged(LogFileItem? value)
    {
        IsTruncated = false;
        if (value is null)
        {
            SelectedContent = string.Empty;
            return;
        }

        try
        {
            // ReadLines streams instead of loading the whole file at once.
            // We take MaxPreviewLines+1 to detect whether the file extends
            // beyond the cap without materialising the rest.
            var lines = File.ReadLines(value.FullPath)
                            .Take(MaxPreviewLines + 1)
                            .ToList();
            if (lines.Count > MaxPreviewLines)
            {
                IsTruncated = true;
                lines.RemoveAt(MaxPreviewLines);
                lines.Add(string.Empty);
                lines.Add(string.Format(
                    TranslationSource.Instance["logs.truncated_notice"],
                    MaxPreviewLines));
            }
            SelectedContent = string.Join('\n', lines);
        }
        catch (IOException ex)
        {
            SelectedContent = string.Empty;
            StatusMessage = ex.Message;
        }
    }
}

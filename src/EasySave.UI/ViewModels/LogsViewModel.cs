using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasySave.Services;

namespace EasySave.UI.ViewModels;

/// <summary>
/// View model for the logs viewer screen. Lists daily log files written by
/// EasyLog (JSON or XML) and shows the raw content of the selected file in
/// a read-only panel.
/// </summary>
public sealed partial class LogsViewModel : ViewModelBase
{
    public ObservableCollection<LogFileItem> Files { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private LogFileItem? _selectedFile;

    [ObservableProperty]
    private string _selectedContent = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public bool HasSelection => SelectedFile is not null;
    public bool IsEmpty => Files.Count == 0;

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
        if (value is null)
        {
            SelectedContent = string.Empty;
            return;
        }

        try
        {
            SelectedContent = File.ReadAllText(value.FullPath);
        }
        catch (IOException ex)
        {
            SelectedContent = string.Empty;
            StatusMessage = ex.Message;
        }
    }
}

public sealed class LogFileItem
{
    public string FullPath { get; }
    public string DisplayName => Path.GetFileName(FullPath);
    public string SizeDisplay
    {
        get
        {
            try
            {
                var bytes = new FileInfo(FullPath).Length;
                return bytes < 1024 ? $"{bytes} B" : $"{bytes / 1024.0:0.#} KB";
            }
            catch (IOException)
            {
                return string.Empty;
            }
        }
    }

    public LogFileItem(string fullPath) => FullPath = fullPath;
}

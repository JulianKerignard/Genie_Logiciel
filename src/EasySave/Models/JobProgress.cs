using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace EasySave.Models;

// Observable view of a single job's live progress.
// One instance per backup job; mutated in place by StateTracker so GUI bindings
// see real-time updates via INotifyPropertyChanged.
public sealed class JobProgress : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; }

    public JobProgress(string name)
    {
        Name = name;
    }

    private string _currentFile = string.Empty;
    public string CurrentFile
    {
        get => _currentFile;
        set => Set(ref _currentFile, value);
    }

    private int _filesRemaining;
    public int FilesRemaining
    {
        get => _filesRemaining;
        set => Set(ref _filesRemaining, value);
    }

    private double _percent;
    public double Percent
    {
        get => _percent;
        set => Set(ref _percent, value);
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

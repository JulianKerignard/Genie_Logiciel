using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using EasySave.Models;
using EasySave.UI.Services;

namespace EasySave.UI.ViewModels;

/// <summary>
/// ComboBox item for <see cref="BackupType"/> with a locale-aware display name.
/// Avalonia's default ComboBox renders enum items via <c>ToString()</c>, which
/// produces the raw identifier ("Full" / "Differential") regardless of the
/// active locale. This wrapper exposes a <see cref="DisplayName"/> bound to the
/// resource dictionary so the dropdown matches the rest of the UI.
/// </summary>
public sealed class BackupTypeOption : ObservableObject, IDisposable
{
    public BackupType Type { get; }

    public string DisplayName => Type == BackupType.Full
        ? TranslationSource.Instance["edit.type_full"]
        : TranslationSource.Instance["edit.type_diff"];

    public BackupTypeOption(BackupType type)
    {
        Type = type;
        TranslationSource.Instance.PropertyChanged += OnLocaleChanged;
    }

    private void OnLocaleChanged(object? sender, PropertyChangedEventArgs e)
        => OnPropertyChanged(nameof(DisplayName));

    public void Dispose()
        => TranslationSource.Instance.PropertyChanged -= OnLocaleChanged;
}

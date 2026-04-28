using Avalonia.Controls;
using Avalonia.Interactivity;
using EasySave.UI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EasySave.UI.Views;

/// <summary>
/// Code-behind for the main application window.
/// Handles code-only interactions (language toggle, About dialog)
/// that do not belong in the view model.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnLanguageButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string locale })
        {
            var langService = App.Services?.GetService<ILanguageService>();
            langService?.SetLanguage(locale);
        }
    }

    private void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        var about = new AboutWindow();
        about.ShowDialog(this);
    }
}

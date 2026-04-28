using Avalonia.Controls;
using Avalonia.Interactivity;

namespace EasySave.UI.Views;

/// <summary>Simple non-modal About dialog showing app name, version, and authors.</summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using EasySave.Models;
using EasySave.Services;
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
        if (sender is not Button { Tag: string locale })
            return;

        // Apply at runtime so every {markup:T} binding refreshes immediately —
        // including any modal currently open (About, JobEdit) that subscribes
        // to the same TranslationSource.Instance.
        var langService = App.Services?.GetService<ILanguageService>();
        langService?.SetLanguage(locale);

        PersistLanguage(locale);
    }

    private static void PersistLanguage(string locale)
    {
        // Best-effort: a transient I/O failure must not block the runtime
        // switch the user just performed. The switch already happened.
        // UnauthorizedAccessException does not inherit from IOException
        // (it inherits from SystemException), so it must be caught explicitly
        // — File.WriteAllText / File.Move both throw it on a restrictive ACL.
        try
        {
            var current = SettingsRepository.Instance.Load();
            SettingsRepository.Instance.Save(new AppSettings
            {
                EncryptedExtensions = current.EncryptedExtensions,
                BusinessSoftware = current.BusinessSoftware,
                Language = locale,
                LogFormat = current.LogFormat,
                CryptoSoft = current.CryptoSoft,
            });
        }
        catch (Exception ex) when (ex is System.IO.IOException
                                       or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"[MainWindow] Failed to persist language '{locale}': {ex.Message}");
        }
    }

    private void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        var about = new AboutWindow();
        about.ShowDialog(this);
    }
}

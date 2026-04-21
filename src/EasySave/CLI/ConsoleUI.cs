using EasySave.Models;
using EasySave.Services;

namespace EasySave.CLI;

/// <summary>Console menu loop. Reads user input and dispatches to the appropriate service.</summary>
public sealed class ConsoleUI
{
    private readonly BackupManager _backupManager;
    private readonly LanguageService _lang;
    private readonly CommandParser _parser = new();

    public ConsoleUI(BackupManager backupManager, LanguageService lang)
    {
        _backupManager = backupManager;
        _lang = lang;
    }

    /// <summary>Starts the interactive menu loop. Returns when the user selects Exit.</summary>
    public void Run()
    {
        bool running = true;
        while (running)
        {
            DisplayMenu();
            string choice = Console.ReadLine() ?? string.Empty;
            switch (choice.Trim())
            {
                case "1": AddJob(); break;
                case "2": RemoveJob(); break;
                case "3": ListJobs(); break;
                case "4": ExecuteJobs(); break;
                case "5": ChangeLanguage(); break;
                case "0": running = false; break;
                default:
                    Console.WriteLine(_lang.T("menu.invalid_choice"));
                    break;
            }
        }
    }

    private void DisplayMenu()
    {
        Console.WriteLine();
        Console.WriteLine(_lang.T("menu.title"));
        Console.WriteLine(_lang.T("menu.option.add"));
        Console.WriteLine(_lang.T("menu.option.remove"));
        Console.WriteLine(_lang.T("menu.option.list"));
        Console.WriteLine(_lang.T("menu.option.execute"));
        Console.WriteLine(_lang.T("menu.option.language"));
        Console.WriteLine(_lang.T("menu.option.exit"));
        Console.Write(_lang.T("menu.prompt"));
    }

    private void AddJob()
    {
        Console.Write(_lang.T("prompt.job_name"));
        var name = Console.ReadLine() ?? string.Empty;

        Console.Write(_lang.T("prompt.source_path"));
        var source = Console.ReadLine() ?? string.Empty;

        Console.Write(_lang.T("prompt.target_path"));
        var target = Console.ReadLine() ?? string.Empty;

        Console.Write(_lang.T("prompt.backup_type"));
        var typeInput = Console.ReadLine() ?? string.Empty;

        if (!Enum.TryParse<BackupType>(typeInput.Trim(), ignoreCase: true, out var backupType))
        {
            Console.WriteLine(_lang.T("error.invalid_backup_type"));
            return;
        }

        var job = new BackupJob { Name = name, SourcePath = source, TargetPath = target, Type = backupType };
        try
        {
            _backupManager.AddJob(job);
            Console.WriteLine(_lang.T("confirm.job_added"));
        }
        catch (ArgumentException)
        {
            Console.WriteLine(_lang.T("error.empty_field"));
        }
        catch (InvalidOperationException ex)
        {
            var colon = ex.Message.IndexOf(':');
            var key = colon >= 0 ? ex.Message[..colon].Trim() : ex.Message;
            Console.WriteLine(_lang.T(key));
        }
    }

    private void RemoveJob()
    {
        var jobs = _backupManager.ListJobs();
        if (jobs.Count == 0)
        {
            Console.WriteLine(_lang.T("job.list_empty"));
            return;
        }

        ListJobs();
        Console.Write(_lang.T("prompt.remove_job"));
        var input = Console.ReadLine() ?? string.Empty;

        if (!int.TryParse(input.Trim(), out var index) || index < 1 || index > jobs.Count)
        {
            Console.WriteLine(_lang.T("error.invalid_selection"));
            return;
        }

        try
        {
            _backupManager.RemoveJob(jobs[index - 1].Name);
            Console.WriteLine(_lang.T("confirm.job_removed"));
        }
        catch (KeyNotFoundException ex)
        {
            Console.WriteLine(string.Format(_lang.T("error.job_not_found"), ex.Message));
        }
    }

    private void ListJobs()
    {
        var jobs = _backupManager.ListJobs();
        if (jobs.Count == 0)
        {
            Console.WriteLine(_lang.T("job.list_empty"));
            return;
        }

        Console.WriteLine(_lang.T("job.list_header"));
        for (int i = 0; i < jobs.Count; i++)
        {
            var j = jobs[i];
            Console.WriteLine(string.Format(_lang.T("job.list_item"), i + 1, j.Name, j.Type, j.SourcePath, j.TargetPath));
        }
    }

    private void ExecuteJobs()
    {
        var jobs = _backupManager.ListJobs();
        if (jobs.Count == 0)
        {
            Console.WriteLine(_lang.T("job.list_empty"));
            return;
        }

        ListJobs();
        Console.Write(_lang.T("prompt.select_jobs"));
        var input = Console.ReadLine() ?? string.Empty;

        var indices = _parser.ParseJobSelection(input);
        if (indices.Count == 0)
        {
            Console.WriteLine(_lang.T("error.invalid_selection"));
            return;
        }

        JobSelectionRunner.Execute(indices, jobs, _backupManager, _lang, Console.WriteLine, Console.WriteLine);
    }

    private void ChangeLanguage()
    {
        Console.Write(_lang.T("prompt.language"));
        var lang = (Console.ReadLine() ?? string.Empty).Trim().ToLowerInvariant();

        if (lang != "en" && lang != "fr")
        {
            Console.WriteLine(_lang.T("error.invalid_language"));
            return;
        }

        _lang.SetLanguage(lang);
        Console.WriteLine(string.Format(_lang.T("confirm.language_changed"), lang));
    }
}

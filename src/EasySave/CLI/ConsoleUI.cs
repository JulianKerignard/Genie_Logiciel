using EasySave.Services;

namespace EasySave.CLI;

/// <summary>Console menu loop. Reads user input and dispatches to the appropriate service.</summary>
public sealed class ConsoleUI
{
    private readonly BackupManager _backupManager;
    private readonly LanguageService _lang;

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
                case "1":
                    Console.WriteLine(_lang.T("error.not_implemented"));
                    break;
                case "2":
                    Console.WriteLine(_lang.T("error.not_implemented"));
                    break;
                case "3":
                    Console.WriteLine(_lang.T("error.not_implemented"));
                    break;
                case "4":
                    Console.WriteLine(_lang.T("error.not_implemented"));
                    break;
                case "0":
                    running = false;
                    break;
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
        Console.WriteLine(_lang.T("menu.option.exit"));
        Console.Write(_lang.T("menu.prompt"));
    }
}

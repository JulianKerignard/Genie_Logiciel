using System.Text.Json;

namespace EasySave.RemoteConsole.Services;

public sealed class LanguageService
{
    private readonly Dictionary<string, string> _strings;

    public LanguageService(string lang = "en")
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", $"{lang}.json");
        _strings = File.Exists(path)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? []
            : [];
    }

    public string T(string key) => _strings.TryGetValue(key, out var v) ? v : key;
}

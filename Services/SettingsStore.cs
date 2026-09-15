using System.IO;
using System.Text.Json;
using TgSpecialWatch.Models;

namespace TgSpecialWatch.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public AppSettings Load()
    {
        AppPaths.EnsureRoot();
        if (!File.Exists(AppPaths.SettingsPath))
            return new AppSettings();

        var json = File.ReadAllText(AppPaths.SettingsPath);
        return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        AppPaths.EnsureRoot();
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(AppPaths.SettingsPath, json);
    }
}

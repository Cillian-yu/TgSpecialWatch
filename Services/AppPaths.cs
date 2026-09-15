using System.IO;

namespace TgSpecialWatch.Services;

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TgSpecialWatch");

    public static string SettingsPath => Path.Combine(Root, "settings.json");

    public static string RulesPath => Path.Combine(Root, "rules.json");

    public static string SessionPath(string fileName) => Path.Combine(Root, fileName);

    public static void EnsureRoot()
    {
        Directory.CreateDirectory(Root);
    }
}

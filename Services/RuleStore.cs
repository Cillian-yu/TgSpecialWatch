using System.IO;
using System.Text.Json;
using TgSpecialWatch.Models;

namespace TgSpecialWatch.Services;

public sealed class RuleStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly List<WatchRule> _rules = new();

    public IReadOnlyList<WatchRule> Rules => _rules;

    public void Load()
    {
        _rules.Clear();
        AppPaths.EnsureRoot();
        if (!File.Exists(AppPaths.RulesPath))
            return;

        var json = File.ReadAllText(AppPaths.RulesPath);
        var list = JsonSerializer.Deserialize<List<WatchRule>>(json, JsonOptions);
        if (list is null)
            return;

        _rules.AddRange(list);
    }

    public void Save()
    {
        AppPaths.EnsureRoot();
        var json = JsonSerializer.Serialize(_rules, JsonOptions);
        File.WriteAllText(AppPaths.RulesPath, json);
    }

    public void Upsert(WatchRule rule)
    {
        var index = _rules.FindIndex(r => r.Id == rule.Id);
        if (index >= 0)
            _rules[index] = rule;
        else
            _rules.Add(rule);

        Save();
    }

    public void Remove(Guid id)
    {
        _rules.RemoveAll(r => r.Id == id);
        Save();
    }
}

using TgSpecialWatch.Models;

namespace TgSpecialWatch.Services;

public sealed class RuleEngine
{
    public WatchRule? Match(IncomingMessage message, IEnumerable<WatchRule> rules)
    {
        if (message.IsOutgoing)
            return null;

        foreach (var rule in rules.Where(r => r.Enabled))
        {
            if (IsMatch(message, rule))
                return rule;
        }

        return null;
    }

    private static bool IsMatch(IncomingMessage message, WatchRule rule)
    {
        return rule.Scope switch
        {
            RuleScope.User =>
                rule.PeerId is long userId
                && message.IsPrivateChat
                && (message.PeerId == userId || message.SenderId == userId),

            RuleScope.Chat =>
                rule.PeerId is long chatId
                && !message.IsPrivateChat
                && message.PeerId == chatId,

            RuleScope.Keyword =>
                !string.IsNullOrWhiteSpace(rule.Keyword)
                && TextContains(message.Text, rule.Keyword!, rule.CaseInsensitive)
                && (rule.PeerId is null
                    || message.PeerId == rule.PeerId
                    || message.SenderId == rule.PeerId),

            _ => false
        };
    }

    private static bool TextContains(string text, string keyword, bool ignoreCase)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        return ignoreCase
            ? text.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            : text.Contains(keyword, StringComparison.Ordinal);
    }
}

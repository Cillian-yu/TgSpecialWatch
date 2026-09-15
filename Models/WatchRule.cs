namespace TgSpecialWatch.Models;

public sealed class WatchRule
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    public bool Enabled { get; set; } = true;

    public RuleScope Scope { get; set; } = RuleScope.User;

    /// <summary>用户或群的 Peer Id；Keyword 全局匹配时可为空。</summary>
    public long? PeerId { get; set; }

    /// <summary>显示用名称（联系人/群名），便于规则列表阅读。</summary>
    public string? PeerDisplayName { get; set; }

    public string? Keyword { get; set; }

    public bool CaseInsensitive { get; set; } = true;

    public AlertLevel Level { get; set; } = AlertLevel.Strong;
}

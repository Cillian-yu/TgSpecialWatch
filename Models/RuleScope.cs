namespace TgSpecialWatch.Models;

public enum RuleScope
{
    /// <summary>整个联系人（私聊任意消息）。</summary>
    User = 0,

    /// <summary>整个群/超级群。</summary>
    Chat = 1,

    /// <summary>关键词（可限定 Peer，或全局）。</summary>
    Keyword = 2
}

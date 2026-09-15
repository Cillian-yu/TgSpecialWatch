namespace TgSpecialWatch.Models;

/// <summary>供规则引擎匹配的入站消息快照。</summary>
public sealed class IncomingMessage
{
    public long PeerId { get; init; }

    public string PeerTitle { get; init; } = "";

    public long? SenderId { get; init; }

    public string SenderName { get; init; } = "";

    public string Text { get; init; } = "";

    public int MessageId { get; init; }

    public bool IsOutgoing { get; init; }

    public bool IsPrivateChat { get; init; }
}

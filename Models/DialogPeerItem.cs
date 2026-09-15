namespace TgSpecialWatch.Models;

public enum DialogPeerKind
{
    User = 0,
    Group = 1,
    Channel = 2
}

public sealed class DialogPeerItem
{
    public long PeerId { get; init; }

    public string Title { get; init; } = "";

    public string? Username { get; init; }

    public DialogPeerKind Kind { get; init; }

    public string KindLabel => Kind switch
    {
        DialogPeerKind.User => "联系人",
        DialogPeerKind.Group => "群组",
        DialogPeerKind.Channel => "频道",
        _ => "未知"
    };

    public string Subtitle
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Username))
                return $"@{Username} · {PeerId}";
            return PeerId.ToString();
        }
    }
}

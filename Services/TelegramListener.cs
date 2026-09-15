using System.IO;
using TgSpecialWatch.Models;
using TL;
using WTelegram;

namespace TgSpecialWatch.Services;

public sealed class TelegramListener : IAsyncDisposable
{
    private readonly SettingsStore _settingsStore;
    private readonly RuleStore _ruleStore;
    private readonly RuleEngine _ruleEngine;
    private readonly AlertService _alertService;

    private Client? _client;
    private bool _isListening;

    public TelegramListener(
        SettingsStore settingsStore,
        RuleStore ruleStore,
        RuleEngine ruleEngine,
        AlertService alertService)
    {
        _settingsStore = settingsStore;
        _ruleStore = ruleStore;
        _ruleEngine = ruleEngine;
        _alertService = alertService;
    }

    public bool IsConnected => _client?.User is not null;

    public string? CurrentUserDisplay { get; private set; }

    public event Action<string>? StatusChanged;

    public event Func<string, Task<string?>>? LoginInputRequired;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_client is not null)
            await DisposeClientAsync();

        var settings = _settingsStore.Load();
        if (settings.ApiId <= 0 || string.IsNullOrWhiteSpace(settings.ApiHash))
            throw new InvalidOperationException("请先在登录页填写 api_id 与 api_hash。");

        AppPaths.EnsureRoot();
        var sessionPath = AppPaths.SessionPath(settings.SessionFileName);

        _client = new Client(what => Config(what, settings, sessionPath));
        _client.OnUpdates += OnUpdates;

        SetStatus("正在登录 Telegram…");
        var user = await _client.LoginUserIfNeeded();
        CurrentUserDisplay = $"{user.first_name} {user.last_name}".Trim();
        if (string.IsNullOrWhiteSpace(CurrentUserDisplay))
            CurrentUserDisplay = user.username ?? user.id.ToString();

        _isListening = true;
        SetStatus($"已登录：{CurrentUserDisplay}，正在监听…");
    }

    public async Task DisconnectAsync()
    {
        _isListening = false;
        await DisposeClientAsync();
        CurrentUserDisplay = null;
        SetStatus("已断开连接");
    }

    /// <summary>
    /// 拉取当前账号会话列表，供规则编辑点选 Peer。
    /// </summary>
    /// <param name="kindFilter">
    /// null=全部；User=仅私聊联系人；Chat=群组/超级群（不含广播频道）。
    /// </param>
    public async Task<IReadOnlyList<DialogPeerItem>> GetDialogsAsync(
        RuleScope? kindFilter = null,
        CancellationToken cancellationToken = default)
    {
        if (_client?.User is null)
            throw new InvalidOperationException("请先登录 Telegram 后再选择会话。");

        SetStatus("正在拉取会话列表…");
        var dialogs = await _client.Messages_GetAllDialogs();
        cancellationToken.ThrowIfCancellationRequested();

        var result = new List<DialogPeerItem>();
        foreach (var dialog in dialogs.Dialogs)
        {
            if (dialog.Peer is null)
                continue;

            var peerId = dialog.Peer.ID;
            if (peerId == 0)
                continue;

            var entity = dialogs.UserOrChat(dialog.Peer);
            DialogPeerItem? item = entity switch
            {
                User u when !u.flags.HasFlag(User.Flags.bot)
                           && !u.flags.HasFlag(User.Flags.deleted)
                           && !u.flags.HasFlag(User.Flags.self) => new DialogPeerItem
                {
                    PeerId = u.id,
                    Title = FormatUser(u),
                    Username = string.IsNullOrWhiteSpace(u.username) ? null : u.username,
                    Kind = DialogPeerKind.User
                },
                Chat chat when chat.IsActive => new DialogPeerItem
                {
                    PeerId = peerId,
                    Title = chat.title ?? $"群:{peerId}",
                    Kind = DialogPeerKind.Group
                },
                Channel channel when channel.IsActive && channel.IsGroup => new DialogPeerItem
                {
                    PeerId = peerId,
                    Title = channel.title ?? $"超级群:{peerId}",
                    Username = string.IsNullOrWhiteSpace(channel.username) ? null : channel.username,
                    Kind = DialogPeerKind.Group
                },
                Channel channel when channel.IsActive => new DialogPeerItem
                {
                    PeerId = peerId,
                    Title = channel.title ?? $"频道:{peerId}",
                    Username = string.IsNullOrWhiteSpace(channel.username) ? null : channel.username,
                    Kind = DialogPeerKind.Channel
                },
                _ => null
            };

            if (item is null)
                continue;

            if (!PassFilter(item, kindFilter))
                continue;

            result.Add(item);
        }

        // 去重并按标题排序，方便查找
        result = result
            .GroupBy(x => x.PeerId)
            .Select(g => g.First())
            .OrderBy(x => x.Kind)
            .ThenBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        SetStatus($"已登录：{CurrentUserDisplay}，正在监听…（会话 {result.Count}）");
        return result;
    }

    private static bool PassFilter(DialogPeerItem item, RuleScope? kindFilter)
    {
        if (kindFilter is null)
            return true;

        return kindFilter switch
        {
            RuleScope.User => item.Kind == DialogPeerKind.User,
            RuleScope.Chat => item.Kind is DialogPeerKind.Group or DialogPeerKind.Channel,
            RuleScope.Keyword => true,
            _ => true
        };
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
    }

    private string? Config(string what, AppSettings settings, string sessionPath)
    {
        switch (what)
        {
            case "api_id":
                return settings.ApiId.ToString();
            case "api_hash":
                return settings.ApiHash;
            case "phone_number":
                return settings.PhoneNumber;
            case "session_pathname":
                return sessionPath;
            case "verification_code":
            case "password":
            case "email":
            case "email_verification_code":
                return RequestLoginInput(what).GetAwaiter().GetResult();
            default:
                return null;
        }
    }

    private async Task<string?> RequestLoginInput(string what)
    {
        if (LoginInputRequired is null)
            throw new InvalidOperationException($"登录需要输入：{what}，但未绑定输入回调。");

        SetStatus($"等待输入：{DescribeLoginField(what)}");
        return await LoginInputRequired(what);
    }

    private static string DescribeLoginField(string what) => what switch
    {
        "verification_code" => "验证码",
        "password" => "两步验证密码",
        "email" => "邮箱",
        "email_verification_code" => "邮箱验证码",
        _ => what
    };

    private async Task OnUpdates(UpdatesBase updates)
    {
        if (!_isListening || _client is null)
            return;

        try
        {
            foreach (var update in updates.UpdateList)
            {
                // UpdateNewChannelMessage 继承自 UpdateNewMessage
                if (update is not UpdateNewMessage { message: Message message })
                    continue;

                var incoming = MapMessage(message, updates);
                if (incoming is null)
                    continue;

                var rule = _ruleEngine.Match(incoming, _ruleStore.Rules);
                if (rule is null)
                    continue;

                var alert = new AlertItem
                {
                    ReceivedAt = DateTimeOffset.Now,
                    SourceTitle = string.IsNullOrWhiteSpace(incoming.PeerTitle)
                        ? incoming.SenderName
                        : incoming.PeerTitle,
                    MessagePreview = string.IsNullOrWhiteSpace(incoming.Text)
                        ? "[非文本消息]"
                        : Truncate(incoming.Text, 200),
                    MatchedRuleName = rule.Name,
                    Level = rule.Level,
                    PeerId = incoming.PeerId,
                    MessageId = incoming.MessageId
                };

                _alertService.Enqueue(alert);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"处理消息时出错：{ex.Message}");
        }

        await Task.CompletedTask;
    }

    private IncomingMessage? MapMessage(Message message, UpdatesBase updates)
    {
        if (_client?.User is null)
            return null;

        var peerId = message.peer_id.ID;
        if (peerId == 0)
            return null;

        var isPrivate = message.peer_id is PeerUser;
        var senderId = message.from_id?.ID;
        if (senderId is null && isPrivate)
            senderId = peerId;

        var peerTitle = ResolvePeerTitle(message.peer_id, updates);
        var senderName = ResolveSenderName(message.from_id, updates) ?? peerTitle;

        return new IncomingMessage
        {
            PeerId = peerId,
            PeerTitle = peerTitle,
            SenderId = senderId,
            SenderName = senderName,
            Text = message.message ?? "",
            MessageId = message.id,
            IsOutgoing = message.flags.HasFlag(Message.Flags.out_),
            IsPrivateChat = isPrivate
        };
    }

    private static string ResolvePeerTitle(Peer peer, UpdatesBase updates) =>
        updates.UserOrChat(peer) switch
        {
            User u => FormatUser(u),
            ChatBase chat => chat.Title ?? $"chat:{peer.ID}",
            _ => $"peer:{peer.ID}"
        };

    private static string? ResolveSenderName(Peer? from, UpdatesBase updates)
    {
        if (from is null)
            return null;

        return updates.UserOrChat(from) switch
        {
            User u => FormatUser(u),
            _ => null
        };
    }

    private static string FormatUser(User u)
    {
        var name = $"{u.first_name} {u.last_name}".Trim();
        if (!string.IsNullOrWhiteSpace(name))
            return name;
        return string.IsNullOrWhiteSpace(u.username) ? u.id.ToString() : "@" + u.username;
    }

    private static string Truncate(string text, int max)
        => text.Length <= max ? text : text[..max] + "…";

    private void SetStatus(string status) => StatusChanged?.Invoke(status);

    private async Task DisposeClientAsync()
    {
        if (_client is null)
            return;

        _client.OnUpdates -= OnUpdates;
        _client.Dispose();
        _client = null;
        await Task.CompletedTask;
    }
}

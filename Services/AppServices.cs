namespace TgSpecialWatch.Services;

/// <summary>单项目起步用的简易服务定位器。</summary>
public sealed class AppServices
{
    public static AppServices Instance { get; } = new();

    public SettingsStore SettingsStore { get; } = new();

    public RuleStore RuleStore { get; } = new();

    public RuleEngine RuleEngine { get; } = new();

    public AlertSoundPlayer SoundPlayer { get; }

    public AlertService AlertService { get; }

    public SocketBroadcastServer SocketServer { get; } = new();

    public TelegramListener TelegramListener { get; }

    private AppServices()
    {
        SoundPlayer = new AlertSoundPlayer(SettingsStore);
        AlertService = new AlertService(SoundPlayer);
        TelegramListener = new TelegramListener(
            SettingsStore,
            RuleStore,
            RuleEngine,
            AlertService);
    }

    public void Initialize()
    {
        AppPaths.EnsureRoot();
        RuleStore.Load();

        AlertService.AlertRaised += item => SocketServer.BroadcastAlert(item);
        AlertService.AlertAcknowledged += id => SocketServer.BroadcastClear(id);
        SocketServer.AlertAckReceived += id =>
        {
            // 必须先关窗，再更新状态；且全程 BeginInvoke，避免读线程被同步 Invoke 卡死
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null)
            {
                AlertService.AcknowledgeFromRemote(id);
                return;
            }

            dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Send, new Action(() =>
            {
                AlertService.AcknowledgeFromRemote(id);
                SocketServer.NotifyStatus("已根据手机确认关闭提醒");
            }));
        };

        _ = ApplySocketFromSettingsAsync();
    }

    public async Task ApplySocketFromSettingsAsync()
    {
        var settings = SettingsStore.Load();
        try
        {
            if (settings.SocketEnabled)
                await SocketServer.StartAsync(settings.SocketPort <= 0 ? 18999 : settings.SocketPort);
            else
                await SocketServer.StopAsync();
        }
        catch (Exception ex)
        {
            SocketServer.NotifyStatus($"Socket 启动失败：{ex.Message}");
        }
    }
}

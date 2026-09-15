using System.Windows;
using TgSpecialWatch.Models;
using TgSpecialWatch.Services;
using TgSpecialWatch.Views;

namespace TgSpecialWatch;

public partial class MainWindow : Window
{
    private readonly AppServices _services = AppServices.Instance;
    private AlertOverlayWindow? _overlay;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _services.TelegramListener.StatusChanged += status =>
            Dispatcher.BeginInvoke(() => StatusText.Text = status);

        // Socket 状态始终显示（含「已处理手机确认」），便于确认 ACK 是否到达
        _services.SocketServer.StatusChanged += status =>
            Dispatcher.BeginInvoke(() => StatusText.Text = status);

        _services.AlertService.AlertPresented += OnAlertPresented;
        _services.AlertService.AlertsCleared += OnAlertsCleared;

        RefreshRules();
        StatusText.Text = _services.TelegramListener.IsConnected
            ? $"已登录：{_services.TelegramListener.CurrentUserDisplay}"
            : "未连接 — 请先登录";
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        _services.AlertService.AlertPresented -= OnAlertPresented;
        _services.AlertService.AlertsCleared -= OnAlertsCleared;
        _overlay?.Close();
        await _services.TelegramListener.DisconnectAsync();
        await _services.SocketServer.StopAsync();
    }

    private void OnAlertPresented(AlertItem item, int pending)
    {
        void Show()
        {
            _overlay ??= new AlertOverlayWindow();
            _overlay.Present(item, pending);
        }

        if (Dispatcher.CheckAccess())
            Show();
        else
            Dispatcher.BeginInvoke(Show);
    }

    private void OnAlertsCleared()
    {
        void HideOverlay()
        {
            if (_overlay is null)
                return;
            _overlay.ClearAndHide();
        }

        if (Dispatcher.CheckAccess())
            HideOverlay();
        else
            Dispatcher.BeginInvoke(HideOverlay);
    }

    private void RefreshRules()
    {
        RulesGrid.ItemsSource = null;
        RulesGrid.ItemsSource = _services.RuleStore.Rules
            .Select(r => new RuleRow(r))
            .ToList();
    }

    private void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        var login = new LoginWindow { Owner = this };
        if (login.ShowDialog() == true)
            StatusText.Text = $"已登录：{_services.TelegramListener.CurrentUserDisplay}，正在监听…";
    }

    private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        await _services.TelegramListener.DisconnectAsync();
    }

    private void AddRule_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new RuleEditWindow { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _services.RuleStore.Upsert(dialog.Rule);
            RefreshRules();
        }
    }

    private void EditRule_Click(object sender, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is not RuleRow row)
        {
            MessageBox.Show(this, "请先选择一条规则。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new RuleEditWindow(row.Source) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _services.RuleStore.Upsert(dialog.Rule);
            RefreshRules();
        }
    }

    private void DeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is not RuleRow row)
        {
            MessageBox.Show(this, "请先选择一条规则。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(this, $"确定删除规则「{row.Name}」？", "确认",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _services.RuleStore.Remove(row.Source.Id);
        RefreshRules();
    }

    private void TestAlertButton_Click(object sender, RoutedEventArgs e)
    {
        _services.AlertService.Enqueue(new AlertItem
        {
            SourceTitle = "测试来源",
            MessagePreview = "这是一条测试强提醒。点击「我已收到」可停止声音与弹窗。",
            MatchedRuleName = "手动测试",
            Level = AlertLevel.Strong
        });
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsWindow { Owner = this };
        dlg.ShowDialog();
    }

    private sealed class RuleRow
    {
        public RuleRow(WatchRule source)
        {
            Source = source;
            Enabled = source.Enabled;
            Name = source.Name;
            Scope = source.Scope switch
            {
                RuleScope.User => "联系人",
                RuleScope.Chat => "群",
                RuleScope.Keyword => "关键词",
                _ => source.Scope.ToString()
            };
            PeerDisplay = string.IsNullOrWhiteSpace(source.PeerDisplayName)
                ? source.PeerId?.ToString() ?? "(全局)"
                : $"{source.PeerDisplayName} ({source.PeerId})";
            Keyword = source.Keyword ?? "";
            Level = source.Level == AlertLevel.FullScreen ? "全屏" : "置顶";
        }

        public WatchRule Source { get; }
        public bool Enabled { get; }
        public string Name { get; }
        public string Scope { get; }
        public string PeerDisplay { get; }
        public string Keyword { get; }
        public string Level { get; }
    }
}

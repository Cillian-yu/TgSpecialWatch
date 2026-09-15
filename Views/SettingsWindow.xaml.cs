using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using Microsoft.Win32;
using TgSpecialWatch.Services;

namespace TgSpecialWatch.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsStore _settingsStore = AppServices.Instance.SettingsStore;
    private readonly SocketBroadcastServer _socket = AppServices.Instance.SocketServer;
    private readonly AlertSoundPlayer _soundPlayer = AppServices.Instance.SoundPlayer;

    public SettingsWindow()
    {
        InitializeComponent();
        LoadUi();
        _socket.StatusChanged += OnSocketStatus;
        Closed += (_, _) => _socket.StatusChanged -= OnSocketStatus;
    }

    private void LoadUi()
    {
        var s = _settingsStore.Load();
        SoundPathBox.Text = s.AlertSoundPath ?? "";
        SocketEnabledBox.IsChecked = s.SocketEnabled;
        SocketPortBox.Text = (s.SocketPort <= 0 ? 18999 : s.SocketPort).ToString();
        RefreshSocketStatus();
        LocalIpText.Text = FormatLocalAddresses();
    }

    private void OnSocketStatus(string status)
    {
        Dispatcher.Invoke(() =>
        {
            SocketStatusText.Text = $"Socket 状态：{status}；已连接 {_socket.ClientCount} 台";
        });
    }

    private void RefreshSocketStatus()
    {
        if (_socket.IsRunning)
            SocketStatusText.Text = $"Socket 状态：运行中 0.0.0.0:{_socket.Port}；已连接 {_socket.ClientCount} 台";
        else
            SocketStatusText.Text = "Socket 状态：未运行";
    }

    private void BrowseSound_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择提示音",
            Filter = "音频文件|*.wav;*.mp3;*.wma;*.m4a;*.aac|所有文件|*.*"
        };
        if (dlg.ShowDialog(this) == true)
            SoundPathBox.Text = dlg.FileName;
    }

    private void TestSound_Click(object sender, RoutedEventArgs e)
    {
        // 临时写入路径再试听
        var s = _settingsStore.Load();
        s.AlertSoundPath = SoundPathBox.Text.Trim();
        _settingsStore.Save(s);
        _soundPlayer.Start();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _soundPlayer.Stop();
        };
        timer.Start();
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(SocketPortBox.Text.Trim(), out var port) || port is < 1 or > 65535)
        {
            MessageBox.Show(this, "请填写有效端口（1–65535）。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var path = SoundPathBox.Text.Trim();
        if (!string.IsNullOrEmpty(path) && !File.Exists(path))
        {
            MessageBox.Show(this, "提示音文件不存在。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var s = _settingsStore.Load();
        s.AlertSoundPath = path;
        s.SocketEnabled = SocketEnabledBox.IsChecked == true;
        s.SocketPort = port;
        _settingsStore.Save(s);

        try
        {
            await AppServices.Instance.ApplySocketFromSettingsAsync();
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Socket 启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string FormatLocalAddresses()
    {
        var items = GetLocalAddresses().ToList();
        if (items.Count == 0)
            return "未检测到可用 IPv4。请确认电脑已连 Wi‑Fi。";

        var lines = new List<string> { "安卓请填「手机同一网络」对应的 IP：" };
        lines.AddRange(items);
        lines.Add("不要填 10.147.x.x（ZeroTier），除非手机也加入了同一个 ZeroTier 网络。");
        lines.Add("不要填 127.0.0.1。手机需关流量、只开 Wi‑Fi，并与电脑同一路由器。");
        return string.Join(Environment.NewLine, lines);
    }

    private static IEnumerable<string> GetLocalAddresses()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                            && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a =>
                    {
                        var ip = a.Address.ToString();
                        var name = n.Name;
                        var prefer = n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211
                                     || name.Contains("WLAN", StringComparison.OrdinalIgnoreCase)
                                     || name.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase)
                                     || name.Contains("无线", StringComparison.OrdinalIgnoreCase);
                        var virtualNet = name.Contains("ZeroTier", StringComparison.OrdinalIgnoreCase)
                                         || name.Contains("VMware", StringComparison.OrdinalIgnoreCase)
                                         || name.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase)
                                         || name.Contains("vEthernet", StringComparison.OrdinalIgnoreCase)
                                         || name.Contains("WSL", StringComparison.OrdinalIgnoreCase)
                                         || ip.StartsWith("10.147.", StringComparison.Ordinal);
                        var tag = prefer ? "推荐" : (virtualNet ? "虚拟网，手机通常连不上" : name);
                        return prefer
                            ? $"【推荐】{ip}  （{name}）"
                            : $"{ip}  （{name}，{tag}）";
                    }))
                .Distinct();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}

using System.Windows;
using TgSpecialWatch.Models;
using TgSpecialWatch.Services;

namespace TgSpecialWatch.Views;

public partial class AlertOverlayWindow : Window
{
    private readonly AlertService _alertService = AppServices.Instance.AlertService;

    public AlertOverlayWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyLayoutForCurrent();
    }

    public void Present(AlertItem item, int pendingCount)
    {
        SourceText.Text = item.SourceTitle;
        RuleText.Text = $"命中规则：{item.MatchedRuleName}";
        MessageText.Text = item.MessagePreview;
        TimeText.Text = item.ReceivedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        PendingText.Text = pendingCount > 0 ? $"队列中还有 {pendingCount} 条" : "";

        ApplyLayout(item.Level);
        Visibility = Visibility.Visible;
        if (!IsVisible)
            Show();
        Activate();
    }

    public void ClearAndHide()
    {
        try
        {
            Visibility = Visibility.Collapsed;
            Hide();
        }
        catch
        {
            // ignore
        }
    }

    private void ApplyLayoutForCurrent()
    {
        var current = _alertService.Current;
        if (current is not null)
            ApplyLayout(current.Level);
    }

    private void ApplyLayout(AlertLevel level)
    {
        if (level == AlertLevel.FullScreen)
        {
            WindowState = WindowState.Maximized;
            Left = 0;
            Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;
        }
        else
        {
            WindowState = WindowState.Normal;
            Width = 520;
            Height = 320;
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
            Top = (SystemParameters.PrimaryScreenHeight - Height) / 2;
        }
    }

    private void AckButton_Click(object sender, RoutedEventArgs e)
    {
        _alertService.AcknowledgeCurrent();
    }
}

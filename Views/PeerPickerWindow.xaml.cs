using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TgSpecialWatch.Models;
using TgSpecialWatch.Services;

namespace TgSpecialWatch.Views;

public partial class PeerPickerWindow : Window
{
    private readonly RuleScope? _filter;
    private List<DialogPeerItem> _all = new();

    public DialogPeerItem? SelectedPeer { get; private set; }

    public PeerPickerWindow(RuleScope? filter = null)
    {
        InitializeComponent();
        _filter = filter;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        HintText.Text = _filter switch
        {
            RuleScope.User => "仅显示私聊联系人。双击或选中后点「选用」。",
            RuleScope.Chat => "仅显示群组/频道。双击或选中后点「选用」。",
            RuleScope.Keyword => "可选择任意会话限定关键词范围；也可取消不选（全局）。双击或选中后点「选用」。",
            _ => "双击或选中后点「选用」。"
        };

        PeerList.IsEnabled = false;
        StatusText.Text = "正在从 Telegram 拉取会话…";

        try
        {
            _all = (await AppServices.Instance.TelegramListener.GetDialogsAsync(_filter)).ToList();
            ApplyFilter();
            StatusText.Text = _all.Count == 0
                ? "没有可用会话。请确认已登录且有聊天记录。"
                : $"共 {_all.Count} 个会话";
        }
        catch (Exception ex)
        {
            StatusText.Text = "拉取失败";
            MessageBox.Show(this, ex.Message, "无法获取会话列表", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            PeerList.IsEnabled = true;
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        var keyword = SearchBox.Text?.Trim() ?? "";
        IEnumerable<DialogPeerItem> query = _all;

        if (!string.IsNullOrEmpty(keyword))
        {
            query = _all.Where(p =>
                p.Title.Contains(keyword, StringComparison.CurrentCultureIgnoreCase)
                || (p.Username?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)
                || p.PeerId.ToString().Contains(keyword, StringComparison.Ordinal));
        }

        var list = query.ToList();
        PeerList.ItemsSource = list;
        StatusText.Text = string.IsNullOrEmpty(keyword)
            ? $"共 {list.Count} 个会话"
            : $"匹配 {list.Count} / {_all.Count}";
    }

    private void PeerList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PeerList.SelectedItem is DialogPeerItem)
            ConfirmSelection();
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e) => ConfirmSelection();

    private void ConfirmSelection()
    {
        if (PeerList.SelectedItem is not DialogPeerItem peer)
        {
            MessageBox.Show(this, "请先选择一个会话。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SelectedPeer = peer;
        DialogResult = true;
        Close();
    }
}

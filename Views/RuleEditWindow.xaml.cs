using System.Windows;
using System.Windows.Controls;
using TgSpecialWatch.Models;
using TgSpecialWatch.Services;

namespace TgSpecialWatch.Views;

public partial class RuleEditWindow : Window
{
    public WatchRule Rule { get; private set; }

    public RuleEditWindow(WatchRule? existing = null)
    {
        InitializeComponent();
        Rule = existing is null
            ? new WatchRule()
            : new WatchRule
            {
                Id = existing.Id,
                Name = existing.Name,
                Enabled = existing.Enabled,
                Scope = existing.Scope,
                PeerId = existing.PeerId,
                PeerDisplayName = existing.PeerDisplayName,
                Keyword = existing.Keyword,
                CaseInsensitive = existing.CaseInsensitive,
                Level = existing.Level
            };

        NameBox.Text = Rule.Name;
        PeerIdBox.Text = Rule.PeerId?.ToString() ?? "";
        PeerNameBox.Text = Rule.PeerDisplayName ?? "";
        KeywordBox.Text = Rule.Keyword ?? "";
        IgnoreCaseBox.IsChecked = Rule.CaseInsensitive;
        EnabledBox.IsChecked = Rule.Enabled;

        ScopeBox.SelectedIndex = Rule.Scope switch
        {
            RuleScope.Chat => 1,
            RuleScope.Keyword => 2,
            _ => 0
        };

        LevelBox.SelectedIndex = Rule.Level == AlertLevel.FullScreen ? 1 : 0;
        UpdateKeywordEnabled();
        UpdatePeerHint();
    }

    private void ScopeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateKeywordEnabled();
        UpdatePeerHint();
    }

    private void UpdateKeywordEnabled()
    {
        var isKeyword = GetSelectedScope() == RuleScope.Keyword;
        KeywordBox.IsEnabled = isKeyword;
        IgnoreCaseBox.IsEnabled = isKeyword;
    }

    private void UpdatePeerHint()
    {
        PeerHintText.Text = GetSelectedScope() switch
        {
            RuleScope.User => "联系人规则：请从会话列表选择私聊对象。",
            RuleScope.Chat => "群规则：请从会话列表选择群组/频道。",
            RuleScope.Keyword => "关键词规则：可选会话限定范围；留空表示全局匹配。",
            _ => ""
        };
    }

    private void PickPeerButton_Click(object sender, RoutedEventArgs e)
    {
        if (!AppServices.Instance.TelegramListener.IsConnected)
        {
            MessageBox.Show(this, "请先在主窗口登录 Telegram，再选择会话。", "未登录",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var scope = GetSelectedScope();
        var picker = new PeerPickerWindow(scope) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedPeer is null)
            return;

        var peer = picker.SelectedPeer;
        PeerIdBox.Text = peer.PeerId.ToString();
        PeerNameBox.Text = peer.Title;

        // 未命名时，用会话名填规则名称，方便识别
        if (string.IsNullOrWhiteSpace(NameBox.Text))
            NameBox.Text = peer.Title;

        // 若当前类型与所选会话明显不符，自动对齐类型
        if (scope == RuleScope.User && peer.Kind != DialogPeerKind.User)
        {
            ScopeBox.SelectedIndex = peer.Kind == DialogPeerKind.User ? 0 : 1;
        }
        else if (scope == RuleScope.Chat && peer.Kind == DialogPeerKind.User)
        {
            ScopeBox.SelectedIndex = 0;
        }
    }

    private void ClearPeerButton_Click(object sender, RoutedEventArgs e)
    {
        PeerIdBox.Text = "";
        PeerNameBox.Text = "";
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            MessageBox.Show(this, "请填写规则名称。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var scope = GetSelectedScope();
        long? peerId = null;
        if (!string.IsNullOrWhiteSpace(PeerIdBox.Text))
        {
            if (!long.TryParse(PeerIdBox.Text.Trim(), out var parsed))
            {
                MessageBox.Show(this, "Peer Id 无效，请重新从会话列表选择。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            peerId = parsed;
        }

        if (scope is RuleScope.User or RuleScope.Chat)
        {
            if (peerId is null)
            {
                MessageBox.Show(this, "联系人/群规则请点击「从会话选择」。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        if (scope == RuleScope.Keyword && string.IsNullOrWhiteSpace(KeywordBox.Text))
        {
            MessageBox.Show(this, "关键词规则必须填写关键词。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Rule.Name = NameBox.Text.Trim();
        Rule.Scope = scope;
        Rule.PeerId = peerId;
        Rule.PeerDisplayName = string.IsNullOrWhiteSpace(PeerNameBox.Text) ? null : PeerNameBox.Text.Trim();
        Rule.Keyword = string.IsNullOrWhiteSpace(KeywordBox.Text) ? null : KeywordBox.Text.Trim();
        Rule.CaseInsensitive = IgnoreCaseBox.IsChecked == true;
        Rule.Enabled = EnabledBox.IsChecked == true;
        Rule.Level = LevelBox.SelectedIndex == 1 ? AlertLevel.FullScreen : AlertLevel.Strong;

        DialogResult = true;
        Close();
    }

    private RuleScope GetSelectedScope()
    {
        if (ScopeBox.SelectedItem is ComboBoxItem { Tag: string tag })
        {
            return tag switch
            {
                "Chat" => RuleScope.Chat,
                "Keyword" => RuleScope.Keyword,
                _ => RuleScope.User
            };
        }

        return RuleScope.User;
    }
}

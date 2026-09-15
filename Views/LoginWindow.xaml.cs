using System.Windows;
using TgSpecialWatch.Models;
using TgSpecialWatch.Services;

namespace TgSpecialWatch.Views;

public partial class LoginWindow : Window
{
    private readonly SettingsStore _settingsStore = AppServices.Instance.SettingsStore;
    private readonly TelegramListener _listener = AppServices.Instance.TelegramListener;

    private TaskCompletionSource<string?>? _inputTcs;

    public LoginWindow()
    {
        InitializeComponent();
        LoadSettings();
        _listener.StatusChanged += status =>
            Dispatcher.Invoke(() => StatusText.Text = status);
    }

    private void LoadSettings()
    {
        var s = _settingsStore.Load();
        ApiIdBox.Text = s.ApiId > 0 ? s.ApiId.ToString() : "";
        ApiHashBox.Text = s.ApiHash;
        PhoneBox.Text = s.PhoneNumber;
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(ApiIdBox.Text.Trim(), out var apiId) || apiId <= 0)
        {
            MessageBox.Show(this, "请填写有效的 api_id。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(ApiHashBox.Text) || string.IsNullOrWhiteSpace(PhoneBox.Text))
        {
            MessageBox.Show(this, "请填写 api_hash 与手机号。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var settings = new AppSettings
        {
            ApiId = apiId,
            ApiHash = ApiHashBox.Text.Trim(),
            PhoneNumber = PhoneBox.Text.Trim()
        };
        _settingsStore.Save(settings);

        LoginButton.IsEnabled = false;
        _listener.LoginInputRequired += OnLoginInputRequired;

        try
        {
            await _listener.ConnectAsync();
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "登录失败", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "登录失败：" + ex.Message;
        }
        finally
        {
            _listener.LoginInputRequired -= OnLoginInputRequired;
            LoginButton.IsEnabled = true;
            CodeBox.IsEnabled = false;
            SubmitCodeButton.IsEnabled = false;
        }
    }

    private Task<string?> OnLoginInputRequired(string what)
    {
        _inputTcs = new TaskCompletionSource<string?>();

        Dispatcher.Invoke(() =>
        {
            CodeBox.IsEnabled = true;
            SubmitCodeButton.IsEnabled = true;
            CodeBox.Focus();
            StatusText.Text = what switch
            {
                "verification_code" => "请输入 Telegram 发来的验证码，然后点「提交验证码/密码」。",
                "password" => "请输入两步验证密码，然后点「提交验证码/密码」。",
                _ => $"请输入：{what}"
            };
        });

        return _inputTcs.Task;
    }

    private void SubmitCodeButton_Click(object sender, RoutedEventArgs e)
    {
        _inputTcs?.TrySetResult(CodeBox.Text.Trim());
        CodeBox.Clear();
        CodeBox.IsEnabled = false;
        SubmitCodeButton.IsEnabled = false;
    }
}

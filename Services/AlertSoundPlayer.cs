using System.IO;
using System.Windows.Media;
using TgSpecialWatch.Models;

namespace TgSpecialWatch.Services;

/// <summary>自定义提示音循环播放；无文件时回退系统音。</summary>
public sealed class AlertSoundPlayer
{
    private readonly SettingsStore _settingsStore;
    private MediaPlayer? _player;
    private bool _looping;

    public AlertSoundPlayer(SettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    public void Start()
    {
        Stop();
        _looping = true;

        var path = _settingsStore.Load().AlertSoundPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            System.Media.SystemSounds.Exclamation.Play();
            return;
        }

        try
        {
            _player = new MediaPlayer();
            _player.MediaEnded += OnMediaEnded;
            _player.MediaFailed += (_, _) =>
            {
                Stop();
                System.Media.SystemSounds.Exclamation.Play();
            };
            _player.Open(new Uri(path));
            _player.Play();
        }
        catch
        {
            Stop();
            System.Media.SystemSounds.Exclamation.Play();
        }
    }

    public void PlayTickFallback()
    {
        if (_player is not null)
            return;
        if (_looping)
            System.Media.SystemSounds.Exclamation.Play();
    }

    public void Stop()
    {
        _looping = false;
        if (_player is null)
            return;

        _player.MediaEnded -= OnMediaEnded;
        try
        {
            _player.Stop();
            _player.Close();
        }
        catch
        {
            // ignore
        }

        _player = null;
    }

    private void OnMediaEnded(object? sender, EventArgs e)
    {
        if (!_looping || _player is null)
            return;

        _player.Position = TimeSpan.Zero;
        _player.Play();
    }
}

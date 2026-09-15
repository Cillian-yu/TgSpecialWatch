using System.Windows.Threading;
using TgSpecialWatch.Models;

namespace TgSpecialWatch.Services;

public sealed class AlertService
{
    private readonly Queue<AlertItem> _queue = new();
    private readonly object _sync = new();
    private readonly DispatcherTimer _soundTimer;
    private readonly AlertSoundPlayer _soundPlayer;
    private AlertItem? _current;
    private bool _isSoundPlaying;

    public AlertService(AlertSoundPlayer soundPlayer)
    {
        _soundPlayer = soundPlayer;
        _soundTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _soundTimer.Tick += (_, _) =>
        {
            if (_isSoundPlaying)
                _soundPlayer.PlayTickFallback();
        };
    }

    public event Action<AlertItem, int>? AlertPresented;

    public event Action? AlertsCleared;

    /// <summary>当前提醒开始展示时触发（供 Socket 广播）。</summary>
    public event Action<AlertItem>? AlertRaised;

    /// <summary>本机确认或远端 ACK 后触发（供 Socket 广播 clear）。</summary>
    public event Action<Guid>? AlertAcknowledged;

    public AlertItem? Current
    {
        get
        {
            lock (_sync)
                return _current;
        }
    }

    public int PendingCount
    {
        get
        {
            lock (_sync)
                return _queue.Count;
        }
    }

    public void Enqueue(AlertItem item)
    {
        AlertItem? raised = null;
        lock (_sync)
        {
            _queue.Enqueue(item);
            if (_current is not null)
            {
                AlertPresented?.Invoke(_current, _queue.Count);
            }
            else
            {
                raised = PresentNext_NoLock();
            }
        }

        // 仅在真正展示时广播，保证手机与电脑看到同一条提醒 ID
        if (raised is not null)
            AlertRaised?.Invoke(raised);
    }

    public void AcknowledgeCurrent()
    {
        Guid? ackedId = null;
        AlertItem? raised = null;
        lock (_sync)
        {
            if (_current is not null)
                ackedId = _current.Id;

            _current = null;
            StopSound_NoLock();

            if (_queue.Count == 0)
            {
                AlertsCleared?.Invoke();
            }
            else
            {
                raised = PresentNext_NoLock();
            }
        }

        if (ackedId is Guid id)
            AlertAcknowledged?.Invoke(id);

        if (raised is not null)
            AlertRaised?.Invoke(raised);
    }

    /// <summary>
    /// 手机确认：清空当前及队列，立刻停声关窗。
    /// ID 仅用于回传 clear；对不上也不影响关窗。
    /// </summary>
    public void AcknowledgeFromRemote(Guid? id = null)
    {
        Guid clearId;
        lock (_sync)
        {
            clearId = id is Guid g && g != Guid.Empty
                ? g
                : _current?.Id ?? Guid.Empty;
            _queue.Clear();
            _current = null;
            StopSound_NoLock();
        }

        AlertsCleared?.Invoke();

        if (clearId != Guid.Empty)
            AlertAcknowledged?.Invoke(clearId);
    }

    private AlertItem? PresentNext_NoLock()
    {
        if (_queue.Count == 0)
        {
            _current = null;
            StopSound_NoLock();
            AlertsCleared?.Invoke();
            return null;
        }

        _current = _queue.Dequeue();
        StartSound_NoLock();
        AlertPresented?.Invoke(_current, _queue.Count);
        return _current;
    }

    private void StartSound_NoLock()
    {
        _isSoundPlaying = true;
        _soundPlayer.Start();
        if (!_soundTimer.IsEnabled)
            _soundTimer.Start();
    }

    private void StopSound_NoLock()
    {
        _isSoundPlaying = false;
        _soundTimer.Stop();
        _soundPlayer.Stop();
    }
}

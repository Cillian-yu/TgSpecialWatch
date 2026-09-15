using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using TgSpecialWatch.Models;

namespace TgSpecialWatch.Services;

public sealed class SocketBroadcastServer : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ConcurrentDictionary<int, ClientSession> _clients = new();
    private CancellationTokenSource? _cts;
    private TcpListener? _listener;
    private Task? _acceptLoop;
    private int _nextClientId;

    public bool IsRunning { get; private set; }

    public int Port { get; private set; }

    public int ClientCount => _clients.Count;

    public event Action<string>? StatusChanged;

    /// <summary>参数为提醒 ID；解析不到时为 Guid.Empty。</summary>
    public event Action<Guid>? AlertAckReceived;

    public async Task StartAsync(int port)
    {
        await StopAsync();

        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "端口必须在 1–65535。");

        Port = port;
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        IsRunning = true;
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
        SetStatus($"Socket 已启动：0.0.0.0:{port}");
    }

    public async Task StopAsync()
    {
        IsRunning = false;
        _cts?.Cancel();

        try
        {
            _listener?.Stop();
        }
        catch
        {
            // ignore
        }

        _listener = null;

        foreach (var client in _clients.Values)
            client.Dispose();
        _clients.Clear();

        if (_acceptLoop is not null)
        {
            try { await _acceptLoop; } catch { /* ignore */ }
            _acceptLoop = null;
        }

        _cts?.Dispose();
        _cts = null;
        SetStatus("Socket 已停止");
    }

    public void BroadcastAlert(AlertItem item) => Broadcast(SocketMessage.Alert(item));

    public void BroadcastClear(Guid id) => Broadcast(SocketMessage.Clear(id));

    public async ValueTask DisposeAsync() => await StopAsync();

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is not null)
        {
            try
            {
                var tcp = await _listener.AcceptTcpClientAsync(ct);
                var id = Interlocked.Increment(ref _nextClientId);
                var session = new ClientSession(id, tcp, HandleClientLine, RemoveClient);
                if (_clients.TryAdd(id, session))
                {
                    session.Start();
                    session.Send(SocketMessage.Welcome());
                    SetStatus($"客户端已连接（当前 {_clients.Count}）");
                }
                else
                {
                    session.Dispose();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                SetStatus($"Accept 异常：{ex.Message}");
            }
        }
    }

    private void HandleClientLine(ClientSession session, string line)
    {
        // 宽松识别 ACK：哪怕 JSON 解析失败，只要像 ack 就关窗
        if (LooksLikeAck(line))
        {
            Guid? id = null;
            try
            {
                var ackMsg = JsonSerializer.Deserialize<SocketMessage>(line, JsonOptions);
                if (!string.IsNullOrWhiteSpace(ackMsg?.Id) && Guid.TryParse(ackMsg.Id.Trim(), out var parsed))
                    id = parsed;
            }
            catch
            {
                // ignore parse errors — still treat as ack
            }

            AlertAckReceived?.Invoke(id ?? Guid.Empty);
            return;
        }

        SocketMessage? msg;
        try
        {
            msg = JsonSerializer.Deserialize<SocketMessage>(line, JsonOptions);
        }
        catch
        {
            return;
        }

        if (msg is null || string.IsNullOrWhiteSpace(msg.Type))
            return;

        switch (msg.Type.ToLowerInvariant())
        {
            case "ping":
                session.Send(SocketMessage.Pong());
                break;
            case "hello":
                session.Send(SocketMessage.Welcome());
                SetStatus($"客户端问候：{msg.Client ?? "unknown"}（当前 {_clients.Count}）");
                break;
        }
    }

    private static bool LooksLikeAck(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var trimmed = line.Trim();
        if (trimmed.Equals("ack", StringComparison.OrdinalIgnoreCase))
            return true;

        // 匹配 "type":"ack" / "type": "ack"，避免误伤 alert
        return trimmed.Contains("\"ack\"", StringComparison.OrdinalIgnoreCase)
               && trimmed.Contains("\"type\"", StringComparison.OrdinalIgnoreCase);
    }

    private void RemoveClient(int id)
    {
        if (_clients.TryRemove(id, out var session))
        {
            session.Dispose();
            SetStatus($"客户端断开（当前 {_clients.Count}）");
        }
    }

    private void Broadcast(SocketMessage message)
    {
        if (!IsRunning)
        {
            SetStatus("广播跳过：Socket 未启用");
            return;
        }

        if (_clients.IsEmpty)
        {
            SetStatus("广播跳过：还没有安卓客户端连上来");
            return;
        }

        foreach (var client in _clients.Values)
            client.Send(message);

        SetStatus($"已向 {_clients.Count} 台设备广播 {message.Type}");
    }

    public void NotifyStatus(string status) => StatusChanged?.Invoke(status);

    private void SetStatus(string status) => StatusChanged?.Invoke(status);

    private sealed class ClientSession : IDisposable
    {
        private readonly int _id;
        private readonly TcpClient _tcp;
        private readonly NetworkStream _stream;
        private readonly Action<ClientSession, string> _onLine;
        private readonly Action<int> _onClosed;
        private readonly object _writeSync = new();
        private CancellationTokenSource? _cts;
        private Task? _readTask;

        public ClientSession(
            int id,
            TcpClient tcp,
            Action<ClientSession, string> onLine,
            Action<int> onClosed)
        {
            _id = id;
            _tcp = tcp;
            _tcp.NoDelay = true;
            _stream = tcp.GetStream();
            _onLine = onLine;
            _onClosed = onClosed;
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _readTask = Task.Run(() => ReadLoopAsync(_cts.Token));
        }

        public void Send(SocketMessage message)
        {
            var json = JsonSerializer.Serialize(message, JsonOptions) + "\n";
            var bytes = Encoding.UTF8.GetBytes(json);
            lock (_writeSync)
            {
                try
                {
                    _stream.Write(bytes, 0, bytes.Length);
                    _stream.Flush();
                }
                catch
                {
                    // ignore; read loop will clean up
                }
            }
        }

        private async Task ReadLoopAsync(CancellationToken ct)
        {
            var buffer = new byte[4096];
            var pending = new StringBuilder();
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var read = await _stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                    if (read <= 0)
                        break;

                    pending.Append(Encoding.UTF8.GetString(buffer, 0, read));
                    while (true)
                    {
                        var text = pending.ToString();
                        var nl = text.IndexOf('\n');
                        if (nl < 0)
                            break;

                        var line = text[..nl].TrimEnd('\r');
                        pending.Clear();
                        if (nl + 1 < text.Length)
                            pending.Append(text[(nl + 1)..]);

                        if (!string.IsNullOrWhiteSpace(line))
                            _onLine(this, line);
                    }
                }
            }
            catch
            {
                // disconnect
            }
            finally
            {
                _onClosed(_id);
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            try { _stream.Dispose(); } catch { }
            try { _tcp.Dispose(); } catch { }
            _cts?.Dispose();
        }
    }
}

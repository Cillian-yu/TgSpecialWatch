namespace TgSpecialWatch.Models;

public sealed class AppSettings
{
    public int ApiId { get; set; }

    public string ApiHash { get; set; } = "";

    public string PhoneNumber { get; set; } = "";

    /// <summary>相对 AppData 的 session 文件名。</summary>
    public string SessionFileName { get; set; } = "session.dat";

    /// <summary>自定义提示音绝对路径；空则用系统提示音。</summary>
    public string AlertSoundPath { get; set; } = "";

    /// <summary>是否启用局域网 Socket 广播服务。</summary>
    public bool SocketEnabled { get; set; }

    /// <summary>Socket 监听端口。</summary>
    public int SocketPort { get; set; } = 18999;
}

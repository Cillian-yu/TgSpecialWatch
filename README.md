# TgSpecialWatch

Telegram 特别关注提醒工具：在 Windows 上监听指定联系人 / 群 / 关键词，命中后本机强提醒，并可同步到同一局域网内的安卓手机。任意一端点击「我已收到」后，两端提醒都会停止。

## 功能

- **Telegram 登录与监听**（基于 [WTelegramClient](https://github.com/wiz0u/WTelegramClient)）
- **规则**：整联系人、整群、关键词；支持从会话列表选择 Peer
- **本机提醒**：置顶弹窗 / 全屏、自定义提示音
- **安卓伴侣**：局域网 TCP 同步提醒；震动、闪光灯可选
- **双向 ACK**：电脑或手机任一确认，两边同时停止

## 技术栈

| 端 | 技术 |
| --- | --- |
| 桌面 | C# / WPF / .NET 8、HandyControl、WTelegramClient |
| 安卓 | Kotlin、minSdk 26、Foreground Service |

## 目录结构

```
TgSpecialWatch/
├── MainWindow.*              # 主窗口：登录、规则、测试提醒、设置
├── Services/                 # Telegram 监听、规则引擎、提醒、Socket 广播
├── Models/                   # 规则、提醒、设置、Socket 协议
├── Views/                    # 登录、设置、规则编辑、会话选择、提醒弹窗
├── Themes/                   # 黑白简约主题
└── android/                  # 安卓客户端
    └── app/src/main/java/... # 连接、前台服务、强提醒界面
```

会话与配置默认保存在：`%AppData%\TgSpecialWatch\`

## 环境要求

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Android Studio（编译安卓端）
- Telegram **api_id / api_hash**（在 [my.telegram.org](https://my.telegram.org) 申请）

## 桌面端运行

```powershell
cd D:\workSpace\Private\TgSpecialWatch
dotnet run
```

1. 登录 Telegram（填写 api_id、api_hash、手机号与验证码）
2. 添加关注规则（可「从会话选择」）
3. **设置**中开启 Socket，端口默认 `18999`
4. 记下界面推荐的 **Wi‑Fi 局域网 IP**（不要用 ZeroTier 的 `10.147.x.x`，除非手机也在同一虚拟网）

可用「测试提醒」验证本机弹窗与手机同步。

## 安卓端运行 / 打包

用 Android Studio 打开 `android/` 目录后运行到手机，或命令行打包：

```powershell
cd android
.\gradlew.bat assembleDebug
```

APK 输出路径：

`android/app/build/outputs/apk/debug/app-debug.apk`

安装后：

1. 填写电脑的局域网 IP 与端口（默认 `18999`）
2. 按需打开震动 / 闪光灯
3. 点击连接；状态显示「已连接」后再测提醒
4. 手机与电脑需同一 Wi‑Fi；若连不上，检查路由器是否开启 AP 隔离、以及 Windows 防火墙是否放行端口

## Socket 协议（简述）

行分隔 JSON，默认端口 `18999`。主要类型：

| type | 方向 | 说明 |
| --- | --- | --- |
| `welcome` | PC → 手机 | 握手 |
| `hello` | 手机 → PC | 客户端问候 |
| `alert` | PC → 手机 | 新提醒 |
| `ack` | 手机 → PC | 已确认 |
| `clear` | PC → 手机 | 停止提醒 |
| `ping` / `pong` | 双向 | 保活 |

## 使用注意

- 本工具仅供个人关注提醒，请遵守 Telegram 服务条款与当地法规
- 请妥善保管 `api_hash` 与本机 session 文件，勿提交到公开仓库
- 局域网 Socket 当前无加密鉴权，请仅在受信网络使用

## License

Private / 个人使用。

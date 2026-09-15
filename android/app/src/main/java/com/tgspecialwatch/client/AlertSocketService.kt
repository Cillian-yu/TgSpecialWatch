package com.tgspecialwatch.client

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.IBinder
import androidx.core.app.NotificationCompat
import androidx.core.content.ContextCompat
import java.io.BufferedReader
import java.io.InputStreamReader
import java.net.InetSocketAddress
import java.net.Socket
import java.nio.charset.StandardCharsets
import java.util.concurrent.atomic.AtomicBoolean
import kotlin.concurrent.thread

class AlertSocketService : Service() {
    private var worker: Thread? = null
    private val running = AtomicBoolean(false)
    private var output: java.io.OutputStream? = null
    private val writeLock = Any()
    private val pendingOutbound = java.util.concurrent.ConcurrentLinkedQueue<String>()

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        instance = this
    }

    override fun onDestroy() {
        if (instance === this) instance = null
        super.onDestroy()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        return try {
            when (intent?.action) {
                ACTION_STOP -> {
                    stopSelfSafe()
                    START_NOT_STICKY
                }
                ACTION_ACK -> {
                    val id = intent.getStringExtra(EXTRA_ACK_ID)
                    val ackId = id?.takeIf { it.isNotBlank() } ?: "unknown"
                    enqueueLine(SocketEnvelope.ack(ackId))
                    enqueueLine("ack")
                    getSystemService(NotificationManager::class.java).cancel(NOTIFY_ALERT)
                    START_STICKY
                }
                else -> {
                    val host = intent?.getStringExtra(EXTRA_HOST).orEmpty()
                    val port = intent?.getIntExtra(EXTRA_PORT, 18999) ?: 18999
                    val vibrate = intent?.getBooleanExtra(EXTRA_VIBRATE, true) ?: true
                    val flash = intent?.getBooleanExtra(EXTRA_FLASH, true) ?: true
                    startForegroundServiceNotification()
                    startWorker(host, port, vibrate, flash)
                    START_STICKY
                }
            }
        } catch (ex: Exception) {
            publishStatus("启动失败：${ex.message}")
            stopSelf()
            START_NOT_STICKY
        }
    }

    private fun startForegroundServiceNotification() {
        ensureChannels()
        val pending = PendingIntent.getActivity(
            this,
            0,
            Intent(this, MainActivity::class.java),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
        val notification = NotificationCompat.Builder(this, CHANNEL_SERVICE)
            .setContentTitle("Tg特别关注")
            .setContentText("正在连接 / 保持监听…")
            .setSmallIcon(R.drawable.ic_stat_notify)
            .setContentIntent(pending)
            .setOngoing(true)
            .build()

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            startForeground(
                NOTIFY_SERVICE,
                notification,
                ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC
            )
        } else {
            startForeground(NOTIFY_SERVICE, notification)
        }
    }

    private fun startWorker(host: String, port: Int, vibrate: Boolean, flash: Boolean) {
        running.set(false)
        worker?.interrupt()
        running.set(true)
        worker = thread(name = "tg-socket", isDaemon = true) {
            var attempt = 0
            while (running.get()) {
                try {
                    publishStatus("正在连接 $host:$port …")
                    Socket().use { socket ->
                        socket.tcpNoDelay = true
                        socket.connect(InetSocketAddress(host, port), 8_000)
                        val reader = BufferedReader(InputStreamReader(socket.getInputStream(), StandardCharsets.UTF_8))
                        val rawOut = socket.getOutputStream()
                        synchronized(writeLock) {
                            output = rawOut
                        }

                        // 先读 welcome，再发 hello
                        socket.soTimeout = 8_000
                        val first = reader.readLine()
                        val parsed = first?.let { SocketEnvelope.parse(it) }
                        val ok = parsed?.type.equals("welcome", ignoreCase = true) == true
                                || first?.contains("welcome", ignoreCase = true) == true
                        if (!ok) {
                            throw IllegalStateException("未收到本软件握手（请确认电脑已启用 Socket，且 IP/端口正确）")
                        }

                        flushPendingOutbound()
                        writeLineNow(SocketEnvelope.hello())

                        publishStatus("已连接到电脑 $host:$port")
                        attempt = 0

                        // 短超时轮询：同一线程读写，ACK 最多延迟约 50ms，避免并发写被内核拖到数秒
                        socket.soTimeout = 50
                        var lastPingAt = System.currentTimeMillis()
                        while (running.get()) {
                            flushPendingOutbound()

                            val now = System.currentTimeMillis()
                            if (now - lastPingAt >= 15_000L) {
                                writeLineNow(SocketEnvelope.ping())
                                lastPingAt = now
                            }

                            try {
                                val line = reader.readLine() ?: break
                                if (line.isBlank()) continue
                                handleLine(line, vibrate, flash)
                            } catch (ex: java.net.SocketTimeoutException) {
                                // 轮询 outbound / ping
                            }
                        }
                    }
                } catch (ex: Exception) {
                    val reason = when {
                        ex is java.net.SocketTimeoutException ->
                            "连接超时：手机打不到该 IP。请填电脑【推荐】的 Wi‑Fi 地址（例如 192.168.x.x），不要填 ZeroTier 的 10.147.x.x；手机请关闭流量、只开同一 Wi‑Fi。"
                        ex is java.net.ConnectException ->
                            "连接被拒绝：电脑 Socket 未开，或端口填错。"
                        else -> ex.message ?: "unknown"
                    }
                    publishStatus("连接异常：$reason 将重试…")
                } finally {
                    synchronized(writeLock) {
                        output = null
                    }
                }

                if (!running.get()) break
                attempt++
                val delay = (2_000L * attempt).coerceAtMost(15_000L)
                try {
                    Thread.sleep(delay)
                } catch (_: InterruptedException) {
                    break
                }
            }
            publishStatus("已断开")
            stopForeground(STOP_FOREGROUND_REMOVE)
            stopSelf()
        }
    }

    private fun handleLine(line: String, vibrate: Boolean, flash: Boolean) {
        val msg = SocketEnvelope.parse(line) ?: return
        when (msg.type.lowercase()) {
            "alert" -> {
                showAlertNotification(msg, vibrate, flash)
                sendBroadcast(
                    Intent(ACTION_SHOW_ALERT)
                        .setPackage(packageName)
                        .putExtra(EXTRA_ALERT_ID, msg.id)
                        .putExtra(EXTRA_ALERT_SOURCE, msg.source)
                        .putExtra(EXTRA_ALERT_PREVIEW, msg.preview)
                        .putExtra(EXTRA_ALERT_RULE, msg.rule)
                        .putExtra(EXTRA_VIBRATE, vibrate)
                        .putExtra(EXTRA_FLASH, flash)
                )
            }
            "clear" -> {
                getSystemService(NotificationManager::class.java).cancel(NOTIFY_ALERT)
                sendBroadcast(Intent(ACTION_REMOTE_CLEAR).setPackage(packageName).putExtra(EXTRA_ACK_ID, msg.id))
            }
            "pong", "welcome" -> Unit
        }
    }

    private fun showAlertNotification(msg: SocketEnvelope, vibrate: Boolean, flash: Boolean) {
        ensureChannels()
        val fullScreen = PendingIntent.getActivity(
            this,
            (msg.id ?: "alert").hashCode(),
            AlertActivity.intent(
                this,
                msg.id,
                msg.source,
                msg.preview,
                msg.rule,
                vibrate,
                flash
            ),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
        val notification = NotificationCompat.Builder(this, CHANNEL_ALERT)
            .setContentTitle(msg.source ?: "特别关注")
            .setContentText(msg.preview ?: "")
            .setSmallIcon(R.drawable.ic_stat_notify)
            .setPriority(NotificationCompat.PRIORITY_MAX)
            .setCategory(NotificationCompat.CATEGORY_ALARM)
            .setVisibility(NotificationCompat.VISIBILITY_PUBLIC)
            .setFullScreenIntent(fullScreen, true)
            .setContentIntent(fullScreen)
            .setAutoCancel(true)
            .build()
        getSystemService(NotificationManager::class.java)
            .notify(NOTIFY_ALERT, notification)
    }

    private fun enqueueLine(line: String) {
        // 只入队，由连接线程在短超时循环里立刻写出（避免与 readLine 并发写导致数秒延迟）
        pendingOutbound.offer(line)
    }

    private fun writeLineNow(line: String) {
        synchronized(writeLock) {
            val out = output ?: run {
                pendingOutbound.offer(line)
                return
            }
            try {
                out.write((line + "\n").toByteArray(StandardCharsets.UTF_8))
                out.flush()
            } catch (_: Exception) {
                pendingOutbound.offer(line)
            }
        }
    }

    private fun flushPendingOutbound() {
        synchronized(writeLock) {
            val out = output ?: return
            val failed = ArrayList<String>()
            try {
                while (true) {
                    val line = pendingOutbound.poll() ?: break
                    try {
                        out.write((line + "\n").toByteArray(StandardCharsets.UTF_8))
                        out.flush()
                    } catch (_: Exception) {
                        failed.add(line)
                        break
                    }
                }
            } finally {
                for (i in failed.indices.reversed()) {
                    pendingOutbound.offer(failed[i])
                }
            }
        }
    }

    private fun sendLine(line: String) {
        enqueueLine(line)
    }

    private fun stopSelfSafe() {
        running.set(false)
        worker?.interrupt()
        synchronized(writeLock) {
            output = null
        }
        stopForeground(STOP_FOREGROUND_REMOVE)
        stopSelf()
    }

    private fun publishStatus(text: String) {
        sendBroadcast(
            Intent(ACTION_STATUS)
                .setPackage(packageName)
                .putExtra(EXTRA_STATUS, text)
        )
        val nm = getSystemService(NotificationManager::class.java)
        val notification = NotificationCompat.Builder(this, CHANNEL_SERVICE)
            .setContentTitle("Tg特别关注")
            .setContentText(text)
            .setSmallIcon(R.drawable.ic_stat_notify)
            .setOngoing(true)
            .build()
        nm.notify(NOTIFY_SERVICE, notification)
    }

    private fun ensureChannels() {
        val nm = getSystemService(NotificationManager::class.java)
        nm.createNotificationChannel(
            NotificationChannel(CHANNEL_SERVICE, getString(R.string.channel_service), NotificationManager.IMPORTANCE_LOW)
        )
        nm.createNotificationChannel(
            NotificationChannel(CHANNEL_ALERT, getString(R.string.channel_alert), NotificationManager.IMPORTANCE_HIGH).apply {
                lockscreenVisibility = Notification.VISIBILITY_PUBLIC
                enableVibration(true)
                setBypassDnd(true)
            }
        )
    }

    companion object {
        @Volatile
        private var instance: AlertSocketService? = null

        const val ACTION_CONNECT = "com.tgspecialwatch.client.CONNECT"
        const val ACTION_STOP = "com.tgspecialwatch.client.STOP"
        const val ACTION_ACK = "com.tgspecialwatch.client.ACK"
        const val ACTION_STATUS = "com.tgspecialwatch.client.STATUS"
        const val ACTION_REMOTE_CLEAR = "com.tgspecialwatch.client.REMOTE_CLEAR"

        const val EXTRA_HOST = "host"
        const val EXTRA_PORT = "port"
        const val EXTRA_VIBRATE = "vibrate"
        const val EXTRA_FLASH = "flash"
        const val EXTRA_ACK_ID = "ack_id"
        const val EXTRA_STATUS = "status"

        const val ACTION_SHOW_ALERT = "com.tgspecialwatch.client.SHOW_ALERT"
        const val EXTRA_ALERT_ID = "alert_id"
        const val EXTRA_ALERT_SOURCE = "alert_source"
        const val EXTRA_ALERT_PREVIEW = "alert_preview"
        const val EXTRA_ALERT_RULE = "alert_rule"

        private const val CHANNEL_SERVICE = "service"
        private const val CHANNEL_ALERT = "alert_v2"
        private const val NOTIFY_SERVICE = 1001
        private const val NOTIFY_ALERT = 1002

        fun start(context: Context, host: String, port: Int, vibrate: Boolean, flash: Boolean) {
            val intent = Intent(context, AlertSocketService::class.java).apply {
                action = ACTION_CONNECT
                putExtra(EXTRA_HOST, host)
                putExtra(EXTRA_PORT, port)
                putExtra(EXTRA_VIBRATE, vibrate)
                putExtra(EXTRA_FLASH, flash)
            }
            ContextCompat.startForegroundService(context, intent)
        }

        fun stop(context: Context) {
            val intent = Intent(context, AlertSocketService::class.java).apply { action = ACTION_STOP }
            context.startService(intent)
        }

        fun sendAck(context: Context, id: String?) {
            val ackId = id?.takeIf { it.isNotBlank() } ?: "unknown"
            val line = SocketEnvelope.ack(ackId)
            val svc = instance
            if (svc != null) {
                svc.enqueueLine(line)
                // 再发一行纯 ack，电脑端宽松识别
                svc.enqueueLine("ack")
                svc.getSystemService(NotificationManager::class.java)?.cancel(NOTIFY_ALERT)
                return
            }

            val intent = Intent(context, AlertSocketService::class.java).apply {
                action = ACTION_ACK
                putExtra(EXTRA_ACK_ID, ackId)
            }
            context.startService(intent)
        }
    }
}

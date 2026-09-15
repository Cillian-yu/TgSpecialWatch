package com.tgspecialwatch.client

import android.Manifest
import android.app.NotificationManager
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.provider.Settings
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import androidx.core.content.edit
import com.tgspecialwatch.client.databinding.ActivityMainBinding

class MainActivity : AppCompatActivity() {
    private lateinit var binding: ActivityMainBinding

    private val prefs by lazy { getSharedPreferences("settings", MODE_PRIVATE) }

    private val appReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context?, intent: Intent?) {
            when (intent?.action) {
                AlertSocketService.ACTION_STATUS -> {
                    binding.textStatus.text = intent.getStringExtra(AlertSocketService.EXTRA_STATUS) ?: return
                }
                AlertSocketService.ACTION_SHOW_ALERT -> {
                    startActivity(
                        AlertActivity.intent(
                            this@MainActivity,
                            intent.getStringExtra(AlertSocketService.EXTRA_ALERT_ID),
                            intent.getStringExtra(AlertSocketService.EXTRA_ALERT_SOURCE),
                            intent.getStringExtra(AlertSocketService.EXTRA_ALERT_PREVIEW),
                            intent.getStringExtra(AlertSocketService.EXTRA_ALERT_RULE),
                            intent.getBooleanExtra(AlertSocketService.EXTRA_VIBRATE, true),
                            intent.getBooleanExtra(AlertSocketService.EXTRA_FLASH, true)
                        )
                    )
                }
            }
        }
    }

    private var pendingConnect: Triple<String, Int, Pair<Boolean, Boolean>>? = null

    private val permissionLauncher = registerForActivityResult(
        ActivityResultContracts.RequestMultiplePermissions()
    ) { result ->
        val notifyOk = Build.VERSION.SDK_INT < 33 ||
            result[Manifest.permission.POST_NOTIFICATIONS] == true ||
            ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS) ==
            PackageManager.PERMISSION_GRANTED
        val pending = pendingConnect
        pendingConnect = null
        if (pending != null) {
            if (!notifyOk && Build.VERSION.SDK_INT >= 33) {
                Toast.makeText(this, "请允许通知权限，否则连接保活会被系统杀掉", Toast.LENGTH_LONG).show()
                return@registerForActivityResult
            }
            actuallyStart(pending.first, pending.second, pending.third.first, pending.third.second)
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityMainBinding.inflate(layoutInflater)
        setContentView(binding.root)

        binding.inputHost.setText(prefs.getString("host", ""))
        binding.inputPort.setText(prefs.getInt("port", 18999).toString())
        binding.switchVibrate.isChecked = prefs.getBoolean("vibrate", true)
        binding.switchFlash.isChecked = prefs.getBoolean("flash", true)

        binding.btnConnect.setOnClickListener { connect() }
        binding.btnDisconnect.setOnClickListener {
            AlertSocketService.stop(this)
            binding.textStatus.text = "已请求断开"
        }

        requestNeededPermissions()
        requestFullScreenIntentIfNeeded()
    }

    override fun onStart() {
        super.onStart()
        val filter = IntentFilter().apply {
            addAction(AlertSocketService.ACTION_STATUS)
            addAction(AlertSocketService.ACTION_SHOW_ALERT)
        }
        ContextCompat.registerReceiver(this, appReceiver, filter, ContextCompat.RECEIVER_NOT_EXPORTED)
    }

    override fun onStop() {
        try {
            unregisterReceiver(appReceiver)
        } catch (_: Exception) {
        }
        super.onStop()
    }

    private fun connect() {
        val host = binding.inputHost.text?.toString()?.trim().orEmpty()
        val port = binding.inputPort.text?.toString()?.trim()?.toIntOrNull()
        if (host.isEmpty() || port == null || port !in 1..65535) {
            Toast.makeText(this, "请填写正确的 IP 和端口", Toast.LENGTH_SHORT).show()
            return
        }

        val vibrate = binding.switchVibrate.isChecked
        val flash = binding.switchFlash.isChecked
        prefs.edit {
            putString("host", host)
            putInt("port", port)
            putBoolean("vibrate", vibrate)
            putBoolean("flash", flash)
        }

        val missing = neededPermissions()
        if (missing.isNotEmpty()) {
            pendingConnect = Triple(host, port, vibrate to flash)
            permissionLauncher.launch(missing.toTypedArray())
            binding.textStatus.text = "请先允许通知权限后再连接"
            return
        }

        actuallyStart(host, port, vibrate, flash)
    }

    private fun actuallyStart(host: String, port: Int, vibrate: Boolean, flash: Boolean) {
        requestFullScreenIntentIfNeeded()
        AlertSocketService.start(this, host, port, vibrate, flash)
        binding.textStatus.text = "正在连接…"
    }

    private fun neededPermissions(): List<String> {
        val need = mutableListOf<String>()
        if (Build.VERSION.SDK_INT >= 33 &&
            ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED
        ) {
            need += Manifest.permission.POST_NOTIFICATIONS
        }
        if (ContextCompat.checkSelfPermission(this, Manifest.permission.CAMERA) != PackageManager.PERMISSION_GRANTED) {
            need += Manifest.permission.CAMERA
        }
        return need
    }

    private fun requestNeededPermissions() {
        val need = neededPermissions()
        if (need.isNotEmpty()) {
            permissionLauncher.launch(need.toTypedArray())
        }
    }

    private fun requestFullScreenIntentIfNeeded() {
        if (Build.VERSION.SDK_INT < 34) return
        val nm = getSystemService(NotificationManager::class.java) ?: return
        if (nm.canUseFullScreenIntent()) return
        Toast.makeText(this, "请允许「全屏通知 / 横幅」权限，否则息屏时无法强提醒", Toast.LENGTH_LONG).show()
        try {
            startActivity(
                Intent(Settings.ACTION_MANAGE_APP_USE_FULL_SCREEN_INTENT).apply {
                    data = Uri.parse("package:$packageName")
                }
            )
        } catch (_: Exception) {
        }
    }
}

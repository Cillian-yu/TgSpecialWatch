package com.tgspecialwatch.client

import android.app.KeyguardManager
import android.content.Context
import android.content.Intent
import android.hardware.camera2.CameraCharacteristics
import android.hardware.camera2.CameraManager
import android.media.AudioAttributes
import android.media.RingtoneManager
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.os.VibrationEffect
import android.os.Vibrator
import android.os.VibratorManager
import android.view.WindowManager
import androidx.appcompat.app.AppCompatActivity
import com.tgspecialwatch.client.databinding.ActivityAlertBinding

class AlertActivity : AppCompatActivity() {
    private lateinit var binding: ActivityAlertBinding
    private val handler = Handler(Looper.getMainLooper())
    private var flashOn = false
    private var cameraId: String? = null
    private var cameraManager: CameraManager? = null
    private var vibrateEnabled = true
    private var flashEnabled = true
    private var alertId: String? = null
    private var ringtone: android.media.Ringtone? = null

    private val clearReceiver = object : android.content.BroadcastReceiver() {
        override fun onReceive(context: android.content.Context?, intent: android.content.Intent?) {
            val clearId = intent?.getStringExtra(AlertSocketService.EXTRA_ACK_ID)
            if (clearId.isNullOrBlank() || clearId == alertId) {
                stopEffects()
                finish()
            }
        }
    }

    private val flashRunnable = object : Runnable {
        override fun run() {
            if (!flashEnabled) return
            try {
                val id = cameraId ?: return
                flashOn = !flashOn
                cameraManager?.setTorchMode(id, flashOn)
            } catch (_: Exception) {
            }
            handler.postDelayed(this, 450)
        }
    }

    private val vibrateRunnable = object : Runnable {
        override fun run() {
            if (!vibrateEnabled) return
            vibrateOnce()
            handler.postDelayed(this, 1200)
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setShowWhenLocked(true)
        setTurnScreenOn(true)
        window.addFlags(
            WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON or
                WindowManager.LayoutParams.FLAG_ALLOW_LOCK_WHILE_SCREEN_ON
        )
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O_MR1) {
            setShowWhenLocked(true)
            setTurnScreenOn(true)
            val km = getSystemService(KeyguardManager::class.java)
            km?.requestDismissKeyguard(this, null)
        }

        binding = ActivityAlertBinding.inflate(layoutInflater)
        setContentView(binding.root)

        androidx.core.content.ContextCompat.registerReceiver(
            this,
            clearReceiver,
            android.content.IntentFilter(AlertSocketService.ACTION_REMOTE_CLEAR),
            androidx.core.content.ContextCompat.RECEIVER_NOT_EXPORTED
        )

        applyIntent(intent)
        binding.btnAck.setOnClickListener { acknowledge() }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        stopEffects()
        applyIntent(intent)
    }

    override fun onDestroy() {
        try {
            unregisterReceiver(clearReceiver)
        } catch (_: Exception) {
        }
        stopEffects()
        super.onDestroy()
    }

    private fun applyIntent(intent: Intent) {
        alertId = intent.getStringExtra(EXTRA_ID)
        vibrateEnabled = intent.getBooleanExtra(EXTRA_VIBRATE, true)
        flashEnabled = intent.getBooleanExtra(EXTRA_FLASH, true)

        binding.textRule.text = "命中规则：${intent.getStringExtra(EXTRA_RULE).orEmpty()}"
        binding.textSource.text = intent.getStringExtra(EXTRA_SOURCE).orEmpty()
        binding.textPreview.text = intent.getStringExtra(EXTRA_PREVIEW).orEmpty()

        startEffects()
    }

    private fun startEffects() {
        try {
            val uri = RingtoneManager.getDefaultUri(RingtoneManager.TYPE_ALARM)
            ringtone = RingtoneManager.getRingtone(this, uri)?.also {
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
                    it.isLooping = true
                }
                it.audioAttributes = AudioAttributes.Builder()
                    .setUsage(AudioAttributes.USAGE_ALARM)
                    .setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION)
                    .build()
                it.play()
            }
        } catch (_: Exception) {
        }

        if (vibrateEnabled) {
            vibrateOnce()
            handler.postDelayed(vibrateRunnable, 1200)
        }

        if (flashEnabled) {
            cameraManager = getSystemService(CameraManager::class.java)
            cameraId = cameraManager?.cameraIdList?.firstOrNull { id ->
                cameraManager?.getCameraCharacteristics(id)
                    ?.get(CameraCharacteristics.FLASH_INFO_AVAILABLE) == true
            }
            if (cameraId != null) {
                handler.post(flashRunnable)
            }
        }
    }

    private fun stopEffects() {
        handler.removeCallbacks(flashRunnable)
        handler.removeCallbacks(vibrateRunnable)
        try {
            ringtone?.stop()
        } catch (_: Exception) {
        }
        ringtone = null
        try {
            cameraId?.let { cameraManager?.setTorchMode(it, false) }
        } catch (_: Exception) {
        }
        flashOn = false
    }

    private fun vibrateOnce() {
        try {
            val vibrator = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                getSystemService(VibratorManager::class.java).defaultVibrator
            } else {
                @Suppress("DEPRECATION")
                getSystemService(Vibrator::class.java)
            }
            val effect = VibrationEffect.createWaveform(longArrayOf(0, 400, 200, 400), -1)
            vibrator.vibrate(effect)
        } catch (_: Exception) {
        }
    }

    private fun acknowledge() {
        AlertSocketService.sendAck(this, alertId)
        stopEffects()
        finish()
    }

    companion object {
        const val EXTRA_ID = "id"
        const val EXTRA_SOURCE = "source"
        const val EXTRA_PREVIEW = "preview"
        const val EXTRA_RULE = "rule"
        const val EXTRA_VIBRATE = "vibrate"
        const val EXTRA_FLASH = "flash"

        fun intent(
            context: Context,
            id: String?,
            source: String?,
            preview: String?,
            rule: String?,
            vibrate: Boolean,
            flash: Boolean
        ): Intent = Intent(context, AlertActivity::class.java).apply {
            addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP or Intent.FLAG_ACTIVITY_SINGLE_TOP)
            putExtra(EXTRA_ID, id)
            putExtra(EXTRA_SOURCE, source)
            putExtra(EXTRA_PREVIEW, preview)
            putExtra(EXTRA_RULE, rule)
            putExtra(EXTRA_VIBRATE, vibrate)
            putExtra(EXTRA_FLASH, flash)
        }
    }
}

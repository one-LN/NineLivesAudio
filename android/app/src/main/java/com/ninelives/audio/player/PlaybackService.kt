package com.ninelives.audio.player

import android.app.PendingIntent
import android.content.Intent
import androidx.media3.common.AudioAttributes
import androidx.media3.common.C
import androidx.media3.exoplayer.DefaultLoadControl
import androidx.media3.exoplayer.ExoPlayer
import androidx.media3.session.MediaSession
import androidx.media3.session.MediaSessionService
import com.ninelives.audio.MainActivity
import com.ninelives.audio.data.PlaybackReporter
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch

class PlaybackService : MediaSessionService() {
    private var mediaSession: MediaSession? = null
    private val serviceScope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private var player: ExoPlayer? = null

    override fun onCreate() {
        super.onCreate()

        // Audiobooks are speech: lets the system pick the right DSP/ducking behaviour.
        val audioAttributes = AudioAttributes.Builder()
            .setContentType(C.AUDIO_CONTENT_TYPE_SPEECH)
            .setUsage(C.USAGE_MEDIA)
            .build()

        // Long-form audio: buffer a little ahead, but start fast after a seek.
        val loadControl = DefaultLoadControl.Builder()
            .setBufferDurationsMs(
                15_000,  // minBufferMs
                60_000,  // maxBufferMs
                2_500,   // bufferForPlaybackMs
                5_000    // bufferForPlaybackAfterRebufferMs
            )
            .build()

        val exoPlayer = ExoPlayer.Builder(this)
            // true = let ExoPlayer handle audio focus (pause on calls, duck/resume)
            .setAudioAttributes(audioAttributes, true)
            // Keep playing when headphones are unplugged / Bluetooth drops.
            .setHandleAudioBecomingNoisy(false)
            .setLoadControl(loadControl)
            // Keep the CPU and Wi-Fi awake while the screen is off, otherwise
            // background listening stalls after a few minutes.
            .setWakeMode(C.WAKE_MODE_NETWORK)
            .build()

        // Tapping the media notification / quick-settings card / dynamic island
        // must bring the player UI back instead of doing nothing.
        val openApp = PendingIntent.getActivity(
            this,
            0,
            Intent(this, MainActivity::class.java).apply {
                flags = Intent.FLAG_ACTIVITY_SINGLE_TOP or Intent.FLAG_ACTIVITY_CLEAR_TOP
            },
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )

        mediaSession = MediaSession.Builder(this, exoPlayer)
            .setSessionActivity(openApp)
            .build()

        // Reporting lives here, not in the ViewModel: the service survives the activity.
        PlaybackReporter.init(this)
        serviceScope.launch {
            while (isActive) {
                delay(1000)
                val p = player ?: continue
                PlaybackReporter.tick(p.currentPosition, p.isPlaying)
            }
        }
    }

    override fun onGetSession(controllerInfo: MediaSession.ControllerInfo): MediaSession? =
        mediaSession

    override fun onDestroy() {
        PlaybackReporter.close()
        mediaSession?.release()
        mediaSession = null
        player?.release()
        player = null
        super.onDestroy()
    }
}

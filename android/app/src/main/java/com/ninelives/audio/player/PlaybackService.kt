package com.ninelives.audio.player

import androidx.media3.common.AudioAttributes
import androidx.media3.common.C
import androidx.media3.exoplayer.DefaultLoadControl
import androidx.media3.exoplayer.ExoPlayer
import androidx.media3.session.MediaSession
import androidx.media3.session.MediaSessionService

class PlaybackService : MediaSessionService() {
    private var mediaSession: MediaSession? = null

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

        val player = ExoPlayer.Builder(this)
            // true = let ExoPlayer handle audio focus (pause on calls, duck/resume)
            .setAudioAttributes(audioAttributes, true)
            // Keep playing when headphones are unplugged / Bluetooth drops.
            .setHandleAudioBecomingNoisy(false)
            .setLoadControl(loadControl)
            .build()

        mediaSession = MediaSession.Builder(this, player).build()
    }

    override fun onGetSession(controllerInfo: MediaSession.ControllerInfo): MediaSession? =
        mediaSession

    override fun onDestroy() {
        mediaSession?.run {
            player.release()
            release()
        }
        mediaSession = null
        super.onDestroy()
    }
}

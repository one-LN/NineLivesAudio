package com.ninelives.audio.data

import android.content.Context
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/**
 * Owns progress reporting for the running playback.
 *
 * It deliberately lives outside the ViewModel: the media service keeps playing when the
 * activity is destroyed, and the reporting must survive that too. One tick per second comes
 * from [com.ninelives.audio.player.PlaybackService].
 */
object PlaybackReporter {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val api = AbsApiClient()

    private var listenLog: ListenLog? = null
    private var appContext: Context? = null
    private var pushCount = 0

    @Volatile private var serverUrl = ""
    @Volatile private var token = ""
    @Volatile private var itemId = ""
    @Volatile private var sessionId = ""
    @Volatile private var duration = 0.0
    @Volatile private var trackOffset = 0.0
    @Volatile private var positionSec = 0.0
    @Volatile private var listened = 0.0
    @Volatile private var lastSyncAt = 0L
    @Volatile private var wasPlaying = false
    @Volatile private var dirty = false

    @Volatile var lastSuccessAt = 0L
        private set
    @Volatile var lastResult = ""
        private set
    @Volatile var lastSentSec = 0.0
        private set

    fun init(context: Context) {
        appContext = context.applicationContext
        if (listenLog == null) listenLog = ListenLog(context.applicationContext)
    }

    private fun log(line: String) {
        try {
            val dir = appContext?.noBackupFilesDir ?: return
            val file = java.io.File(dir, "app_log.txt")
            val stamp = SimpleDateFormat("MM-dd HH:mm:ss", Locale.US).format(Date())
            file.appendText("[$stamp] $line\n")
        } catch (_: Exception) {
        }
    }

    fun configure(url: String, authToken: String, item: String, sid: String, dur: Double, offset: Double) {
        serverUrl = url
        token = authToken
        itemId = item
        sessionId = sid
        duration = dur
        trackOffset = offset
        listened = 0.0
        lastSyncAt = 0L
        wasPlaying = false
        dirty = false
    }

    fun setTrackOffset(offset: Double) {
        trackOffset = offset
    }

    /** Called once per second by the playback service. */
    fun tick(trackPositionMs: Long, playing: Boolean) {
        positionSec = trackOffset + trackPositionMs.coerceAtLeast(0L) / 1000.0
        if (!playing) {
            if (wasPlaying) {
                wasPlaying = false
                push(closing = false)
            }
            return
        }
        wasPlaying = true
        listened += 1.0
        dirty = true
        listenLog?.addSecond()
        val now = System.currentTimeMillis()
        if (now - lastSyncAt >= 10_000) {
            lastSyncAt = now
            push(closing = false)
        }
    }

    fun flush() = push(closing = false)

    fun close() = push(closing = true)

    private fun push(closing: Boolean) {
        val url = serverUrl
        val auth = token
        val item = itemId
        val sid = sessionId
        if (url.isBlank() || auth.isBlank() || item.isBlank()) return
        if (!closing && !dirty) return
        val pos = positionSec
        val dur = duration
        val delta = listened
        listened = 0.0
        dirty = false
        scope.launch {
            val ok = runCatching {
                api.syncProgress(url, auth, sid, item, pos, dur, delta)
            }.getOrDefault(false)
            pushCount++
            if (ok) {
                lastSuccessAt = System.currentTimeMillis()
                lastSentSec = delta
                lastResult = SimpleDateFormat("HH:mm:ss", Locale.US).format(Date(lastSuccessAt))
                if (pushCount % 6 == 0) log("sync ok pos=${pos.toInt()}s +${delta.toInt()}s")
            } else {
                lastResult = "FAIL " + SimpleDateFormat("HH:mm:ss", Locale.US).format(Date())
                log("sync FAILED pos=${pos.toInt()}s +${delta.toInt()}s")
            }
            if (closing && sid.isNotBlank()) {
                runCatching { api.closeSession(url, auth, sid) }
            }
            listenLog?.flush()
        }
    }
}

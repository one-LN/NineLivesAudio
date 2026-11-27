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
    @Volatile private var pushCount = 0

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
    @Volatile var tickCount = 0L
        private set
    val pushes: Int get() = pushCount

    fun init(context: Context) {
        android.util.Log.d("NLREPORT", "init")
        appContext = context.applicationContext
        if (listenLog == null) listenLog = ListenLog(context.applicationContext)
        startWatchdog()
    }

    @Volatile private var lastTickAt = 0L
    @Volatile private var configuredAt = 0L
    private var watchdogStarted = false

    /**
     * If a session is configured but the service stops calling [tick], reporting is
     * dead even though nothing crashed. Say so in the diagnostics log instead of
     * failing silently.
     */
    private fun startWatchdog() {
        if (watchdogStarted) return
        watchdogStarted = true
        scope.launch {
            var lastWarnAt = 0L
            while (true) {
                kotlinx.coroutines.delay(15_000)
                val now = System.currentTimeMillis()
                val since = maxOf(lastTickAt, configuredAt)
                if (itemId.isNotBlank() && since > 0L && now - since > 20_000 && now - lastWarnAt > 60_000) {
                    lastWarnAt = now
                    log("WATCHDOG 回传已停止 ${(now - since) / 1000}s（播放服务未在 tick）")
                }
            }
        }
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

    fun configure(
        url: String,
        authToken: String,
        item: String,
        sid: String,
        dur: Double,
        offset: Double,
        initialPosition: Double
    ) {
        serverUrl = url
        token = authToken
        itemId = item
        sessionId = sid
        duration = dur
        trackOffset = offset
        positionSec = initialPosition
        listened = 0.0
        configuredAt = System.currentTimeMillis()
        lastSyncAt = 0L
        wasPlaying = false
        dirty = false
        android.util.Log.d("NLREPORT", "configure sid=$sid item=$item dur=$dur token=${authToken.isNotBlank()} url=${url.take(24)}")
    }

    fun setTrackOffset(offset: Double) {
        trackOffset = offset
    }

    /**
     * The session that is actually loaded in the media service.
     *
     * The activity (and its ViewModel) can be recreated while playback continues;
     * without this the player screen would come back empty even though audio is
     * still running.
     */
    @Volatile
    var activeSession: PlaybackSessionData? = null
        private set

    fun rememberSession(session: PlaybackSessionData) {
        activeSession = session
    }

    fun forgetSession() {
        activeSession = null
    }

    /** Called once per second by the playback service. */
    fun tick(trackPositionMs: Long, playing: Boolean) {
        lastTickAt = System.currentTimeMillis()
        tickCount++
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

    fun close() {
        // Nothing was played and we have no trustworthy position: publishing here
        // would reset the server-side progress to zero.
        if (!wasPlaying && listened <= 0.0 && positionSec <= 0.5) return
        push(closing = true)
    }

    private fun push(closing: Boolean) {
        android.util.Log.d("NLREPORT", "push closing=$closing dirty=$dirty url=${serverUrl.take(24)} item=$itemId")
        val url = serverUrl
        val auth = token
        val item = itemId
        val sid = sessionId
        if (url.isBlank() || auth.isBlank() || item.isBlank()) return
        if (!closing && !dirty) return
        // A zero position is never meaningful for an existing book; it only happens
        // before the player has seeked to the saved spot.
        if (positionSec <= 0.5 && listened <= 0.0) return
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
            android.util.Log.d("NLREPORT", "result=$ok delta=$delta pos=${pos.toInt()}")
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

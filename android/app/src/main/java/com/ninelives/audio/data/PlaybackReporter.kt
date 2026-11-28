package com.ninelives.audio.data

import android.content.Context
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
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
    private val stateLock = Any()
    private val pushMutex = Mutex()

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

    private data class PushSnapshot(
        val url: String,
        val token: String,
        val itemId: String,
        val sessionId: String,
        val positionSec: Double,
        val durationSec: Double,
        val listenedSec: Double,
        val closing: Boolean
    )

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
        synchronized(stateLock) {
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
        }
        android.util.Log.d("NLREPORT", "configure sid=$sid item=$item dur=$dur token=${authToken.isNotBlank()} url=${url.take(24)}")
    }

    fun setTrackOffset(offset: Double) {
        synchronized(stateLock) { trackOffset = offset }
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
        synchronized(stateLock) { activeSession = session }
    }

    fun forgetSession() {
        synchronized(stateLock) { activeSession = null }
    }

    /** Called once per second by the playback service. */
    fun tick(trackPositionMs: Long, playing: Boolean) {
        var shouldPush = false
        synchronized(stateLock) {
            lastTickAt = System.currentTimeMillis()
            tickCount++
            positionSec = trackOffset + trackPositionMs.coerceAtLeast(0L) / 1000.0
            if (!playing) {
                if (wasPlaying) {
                    wasPlaying = false
                    shouldPush = true
                }
            } else {
                wasPlaying = true
                listened += 1.0
                dirty = true
                val now = System.currentTimeMillis()
                if (now - lastSyncAt >= 10_000) {
                    lastSyncAt = now
                    shouldPush = true
                }
            }
        }
        if (playing) listenLog?.addSecond()
        if (shouldPush) push(closing = false)
    }

    fun flush() = push(closing = false)

    fun close() {
        // Nothing was played and we have no trustworthy position: publishing here
        // would reset the server-side progress to zero.
        val shouldClose = synchronized(stateLock) {
            wasPlaying || listened > 0.0 || positionSec > 0.5
        }
        if (shouldClose) push(closing = true)
    }

    private fun push(closing: Boolean) {
        val snapshot = synchronized(stateLock) {
            if (serverUrl.isBlank() || token.isBlank() || itemId.isBlank()) return
            if (!closing && !dirty) return
            if (positionSec <= 0.5 && listened <= 0.0) return

            PushSnapshot(serverUrl, token, itemId, sessionId, positionSec, duration, listened, closing)
                .also {
                    listened = 0.0
                    dirty = false
                }
        }
        val url = snapshot.url
        val auth = snapshot.token
        val item = snapshot.itemId
        val sid = snapshot.sessionId
        val pos = snapshot.positionSec
        val dur = snapshot.durationSec
        val delta = snapshot.listenedSec
        android.util.Log.d("NLREPORT", "push closing=${snapshot.closing} dirtySnapshot=true url=${url.take(24)} item=$item")

        scope.launch {
            pushMutex.withLock {
                val outcome = runCatching {
                    api.syncProgress(url, auth, sid, item, pos, dur, delta)
                }.getOrElse { error ->
                    ProgressSyncOutcome(
                        sessionRequested = sid.isNotBlank(),
                        sessionError = error.javaClass.simpleName,
                        progressError = error.javaClass.simpleName
                    )
                }
                val ok = outcome.succeeded
                val now = System.currentTimeMillis()
                synchronized(stateLock) {
                    pushCount++
                    if (ok) {
                        lastSuccessAt = now
                        lastSentSec = delta
                        lastResult = if (!outcome.progressPatchSucceeded) {
                            "${SimpleDateFormat("HH:mm:ss", Locale.US).format(Date(now))} session OK, progress failed"
                        } else {
                            SimpleDateFormat("HH:mm:ss", Locale.US).format(Date(now))
                        }
                    } else {
                        // If session sync succeeded but the fallback progress PATCH failed,
                        // ABS has already counted this delta. Retry only when the session POST
                        // itself failed, otherwise the server would count the same seconds twice.
                        val sameSession = sid.isNotBlank() && !outcome.sessionSucceeded &&
                            sessionId == sid && itemId == item
                        if (sameSession) {
                            listened += delta
                            dirty = true
                        }
                        lastResult = buildString {
                            append("FAIL ").append(SimpleDateFormat("HH:mm:ss", Locale.US).format(Date(now)))
                            outcome.sessionStatusCode?.let { append(" session=").append(it) }
                            outcome.progressStatusCode?.let { append(" progress=").append(it) }
                            outcome.sessionError?.let { append(" session=").append(it) }
                            outcome.progressError?.let { append(" progress=").append(it) }
                        }
                    }
                }
                val sessionLabel = if (sid.isBlank()) "n/a" else outcome.sessionStatusCode?.toString() ?: outcome.sessionError ?: "?"
                val progressLabel = outcome.progressStatusCode?.toString() ?: outcome.progressError ?: "?"
                android.util.Log.d(
                    "NLREPORT",
                    "result=$ok session=$sessionLabel progress=$progressLabel counted=${outcome.listeningCounted} delta=$delta pos=${pos.toInt()}"
                )
                if (ok && pushCount % 6 == 0) log("sync ok pos=${pos.toInt()}s +${delta.toInt()}s")
                if (!ok) log("sync FAILED session=$sessionLabel progress=$progressLabel pos=${pos.toInt()}s +${delta.toInt()}s")
                if (snapshot.closing && sid.isNotBlank()) {
                    val closed = runCatching { api.closeSession(url, auth, sid) }.getOrDefault(false)
                    if (closed) {
                        synchronized(stateLock) {
                            if (sessionId == sid && itemId == item) {
                                serverUrl = ""
                                token = ""
                                itemId = ""
                                sessionId = ""
                                duration = 0.0
                                trackOffset = 0.0
                                positionSec = 0.0
                                listened = 0.0
                                dirty = false
                                wasPlaying = false
                            }
                        }
                    } else {
                        log("session close FAILED sid=$sid")
                    }
                }
                listenLog?.flush()
            }
        }
    }
}

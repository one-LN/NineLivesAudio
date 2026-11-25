package com.ninelives.audio.ui

import android.app.Application
import android.content.ComponentName
import android.net.Uri
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableDoubleStateOf
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.core.content.ContextCompat
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import androidx.media3.common.MediaItem
import androidx.media3.common.MediaMetadata
import androidx.media3.common.Player
import androidx.media3.session.MediaController
import androidx.media3.session.SessionToken
import com.ninelives.audio.data.AbsApiClient
import com.ninelives.audio.data.AudioTrackInfo
import com.ninelives.audio.data.DownloadManager
import com.ninelives.audio.data.DownloadStore
import com.ninelives.audio.data.DownloadedBook
import com.ninelives.audio.data.DownloadTask
import com.ninelives.audio.data.BookItem
import com.ninelives.audio.data.LibraryInfo
import com.ninelives.audio.data.PlaybackSessionData
import com.ninelives.audio.data.SavedServer
import com.ninelives.audio.data.SettingsStore
import com.ninelives.audio.player.PlaybackService
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

class MainViewModel(application: Application) : AndroidViewModel(application) {
    private val store = SettingsStore(application)
    private val api = AbsApiClient()
    private val offlineStore = DownloadStore(application)
    private val downloadManager = DownloadManager(application, api, offlineStore)

    var downloadTasks by mutableStateOf(downloadManager.tasks())
        private set
    var downloadedBooks by mutableStateOf(downloadManager.downloadedBooks())
        private set

    private val downloadObserver: () -> Unit = {
        downloadTasks = downloadManager.tasks()
        downloadedBooks = downloadManager.downloadedBooks()
        books = books.map { book ->
            book.copy(isDownloaded = downloadedBooks.any { it.serverUrl == activeUrl && it.itemId == book.id })
        }
    }

    var savedServers by mutableStateOf(store.getSavedServers())
        private set

    var activeUrl by mutableStateOf(store.getActiveUrl())
        private set
    var activeUsername by mutableStateOf(store.getActiveUsername())
        private set
    var activeToken by mutableStateOf(store.getActiveToken())
        private set

    val isConnected: Boolean
        get() = activeUrl.isNotBlank() && activeToken.isNotBlank()

    var statusMessage by mutableStateOf("")
        private set
    var isSyncing by mutableStateOf(false)
        private set
    var isRefreshing by mutableStateOf(false)
        private set
    var totalListeningSec by mutableDoubleStateOf(0.0)
        private set

    var libraries by mutableStateOf<List<LibraryInfo>>(emptyList())
        private set
    var selectedLibrary by mutableStateOf<LibraryInfo?>(null)
        private set
    var books by mutableStateOf<List<BookItem>>(emptyList())
        private set
    var logs by mutableStateOf<List<String>>(emptyList())
        private set

    private fun addLog(msg: String) {
        logs = (listOf(msg) + logs).take(20)
    }

    // Nine Lives: include any book with playback progress or lastUpdate > 0
    val nineLives: List<BookItem>
        get() = books
            .filter { (it.currentTime > 0.0 || it.progress > 0.0 || it.lastUpdate > 0L) && !it.isFinished }
            .sortedByDescending { it.lastUpdate }
            .take(9)

    init {
        downloadManager.setWifiOnly(store.getWifiOnlyDownload())
        downloadManager.observe(downloadObserver)
        viewModelScope.launch {
            downloadManager.retryAll { server, user ->
                store.getSavedServers().firstOrNull {
                    it.url.trimEnd('/').equals(server.trimEnd('/'), true) && it.username.equals(user, true)
                }?.token?.takeIf { it.isNotBlank() }
            }
        }
        if (isConnected) {
            refreshAll()
        }
    }

    fun loginAndConnect(name: String, url: String, username: String, password: String) {
        if (url.isBlank() || username.isBlank() || password.isBlank()) {
            statusMessage = "请填写服务器地址、用户名和密码"
            return
        }
        viewModelScope.launch {
            isSyncing = true
            statusMessage = "正在连接..."
            val res = api.login(url, username, password)
            res.onSuccess { token ->
                val normalized = url.replace(Regex("[\\s\\u200B-\\u200D\\uFEFF\"']+"), "").trimEnd('/')
                addLog("登录成功: $normalized")
                store.setActiveServer(normalized, username.trim(), token)
                activeUrl = normalized
                activeUsername = username.trim()
                activeToken = token

                val updated = savedServers.toMutableList()
                updated.removeAll { it.url.equals(normalized, true) && it.username.equals(username.trim(), true) }
                updated.add(0, SavedServer(name.trim(), normalized, username.trim(), token, System.currentTimeMillis()))
                val trimmed = updated.take(8)
                store.saveServers(trimmed)
                savedServers = trimmed

                statusMessage = "已连接到 $normalized"
                viewModelScope.launch { retryDownloadsFor(normalized, username.trim(), token) }
                refreshAll()
            }.onFailure { err ->
                statusMessage = "连接失败: ${err.message}"
                isSyncing = false
            }
        }
    }

    fun switchServer(server: SavedServer) {
        store.setActiveServer(server.url, server.username, server.token)
        activeUrl = server.url
        activeUsername = server.username
        activeToken = server.token
        statusMessage = "已切换到 ${server.displayName}"
        books = emptyList()
        libraries = emptyList()
        selectedLibrary = null
        refreshAll()
    }

    fun updateServerName(server: SavedServer, newName: String) {
        val updated = savedServers.map {
            if (it.url == server.url && it.username == server.username) it.copy(name = newName.trim()) else it
        }
        store.saveServers(updated)
        savedServers = updated
    }

    fun deleteServer(server: SavedServer) {
        val updated = savedServers.filterNot { it.url == server.url && it.username == server.username }
        store.saveServers(updated)
        savedServers = updated
        if (activeUrl == server.url && activeUsername == server.username) {
            disconnect()
        }
    }

    fun disconnect() {
        store.clearActiveServer()
        activeUrl = ""
        activeUsername = ""
        activeToken = ""
        libraries = emptyList()
        selectedLibrary = null
        books = emptyList()
        statusMessage = "已断开连接"
    }

    fun selectLibrary(lib: LibraryInfo) {
        selectedLibrary = lib
        refreshBooks(lib.id)
    }

    fun isBookDownloaded(itemId: String): Boolean = downloadManager.isDownloaded(activeUrl, itemId)

    fun enqueueDownload(book: BookItem) {
        if (!isConnected) {
            statusMessage = "请先连接服务器再下载"
            return
        }
        downloadManager.enqueue(book, activeUrl, activeUsername, activeToken)
        statusMessage = "已加入下载队列：${book.title}"
    }

    fun playDownloadedBook(book: DownloadedBook) {
        val session = PlaybackSessionData(
            sessionId = "",
            itemId = book.itemId,
            title = book.title,
            author = book.author,
            coverUrl = book.localCoverUri.ifBlank { book.coverUri },
            tracks = book.tracks.map {
                AudioTrackInfo(it.index, it.startOffset, it.duration, it.fileUri)
            },
            currentTime = book.currentTime,
            duration = book.duration,
            chapters = book.chapters
        )
        startPlaybackSession(session)
    }

    fun retryAllDownloads() {
        downloadManager.retryAll { server, user ->
            store.getSavedServers().firstOrNull {
                it.url.trimEnd('/').equals(server.trimEnd('/'), true) && it.username.equals(user, true)
            }?.token?.takeIf { it.isNotBlank() } ?: activeToken.takeIf {
                activeUrl.trimEnd('/').equals(server.trimEnd('/'), true) && activeUsername.equals(user, true)
            }
        }
    }

    fun retryDownload(key: String) {
        val task = downloadManager.tasks().firstOrNull { it.key == key } ?: return
        val token = store.getSavedServers().firstOrNull {
            it.url.trimEnd('/').equals(task.serverUrl.trimEnd('/'), true) && it.username.equals(task.username, true)
        }?.token ?: activeToken.takeIf { activeUrl.trimEnd('/').equals(task.serverUrl.trimEnd('/'), true) }
        if (token.isNullOrBlank()) {
            statusMessage = "请先登录对应服务器后重试下载"
            return
        }
        downloadManager.resume(key) { _, _ -> token }
    }

    fun pauseDownload(key: String) = downloadManager.pause(key)

    fun cancelDownload(key: String) {
        viewModelScope.launch { downloadManager.cancel(key) }
    }

    fun deleteDownloadedBook(book: DownloadedBook) = downloadManager.delete(book.serverUrl, book.itemId)

    private fun retryDownloadsFor(server: String, username: String, token: String) {
        downloadManager.retryAll { savedServer, savedUser ->
            if (savedServer.trimEnd('/').equals(server.trimEnd('/'), true) && savedUser.equals(username, true)) token
            else store.getSavedServers().firstOrNull {
                it.url.trimEnd('/').equals(savedServer.trimEnd('/'), true) && it.username.equals(savedUser, true)
            }?.token
        }
    }

    fun refreshAll() {
        if (!isConnected) return
        viewModelScope.launch {
            isSyncing = true
            val libsRes = api.getLibraries(activeUrl, activeToken)
            libsRes.onSuccess { libs ->
                libraries = libs
                addLog("书库: ${libs.size} 个")
                val target = selectedLibrary?.takeIf { s -> libs.any { it.id == s.id } } ?: libs.firstOrNull()
                selectedLibrary = target
                if (target != null) {
                    loadBooksWithProgress(target.id)
                }
            }.onFailure {
                addLog("拉取书库失败: ${it.message}")
                statusMessage = "拉取书库失败: ${it.message}"
            }
            isSyncing = false
        }
    }

    private fun refreshBooks(libraryId: String) {
        viewModelScope.launch {
            isSyncing = true
            loadBooksWithProgress(libraryId)
            isSyncing = false
        }
    }

    private suspend fun loadBooksWithProgress(libraryId: String) {
        val booksRes = api.getLibraryBooks(activeUrl, activeToken, libraryId)
        val progressMap = api.getProgressMap(activeUrl, activeToken)
        booksRes.onSuccess { list ->
            addLog("书籍: ${list.size} 本, 进度: ${progressMap.size} 条")
            books = list.map { b ->
                val p = progressMap[b.id]
                b.copy(
                    progress = p?.first ?: 0.0,
                    currentTime = p?.second ?: 0.0,
                    isFinished = (p?.first ?: 0.0) >= 0.995,
                    lastUpdate = p?.third ?: 0L,
                    isDownloaded = downloadedBooks.any { it.serverUrl == activeUrl && it.itemId == b.id }
                )
            }
        }.onFailure {
            addLog("拉取书籍失败: ${it.message}")
        }
    }

    // --- Player state ---
    private var mediaController: MediaController? = null
    private var currentTrackIndex = 0

    var currentSession by mutableStateOf<PlaybackSessionData?>(null)
        private set
    var isPlaying by mutableStateOf(false)
        private set
    var currentPositionSec by mutableDoubleStateOf(0.0)
        private set
    var totalDurationSec by mutableDoubleStateOf(0.0)
        private set
    var playbackSpeed by mutableFloatStateOf(1.0f)
        private set
    var sleepTimerMinutes by mutableIntStateOf(0)
        private set
    var sleepRemainingSec by mutableIntStateOf(0)
        private set
    var sleepEndOfChapter by mutableStateOf(false)
        private set
    var themeMode by mutableStateOf("system")
    var autoSyncEnabled by mutableStateOf(true)
    var wifiOnlyDownload by mutableStateOf(store.getWifiOnlyDownload())
        private set

    fun setWifiOnlyDownload(enabled: Boolean) {
        wifiOnlyDownload = enabled
        store.setWifiOnlyDownload(enabled)
        downloadManager.setWifiOnly(enabled)
        retryAllDownloads()
    }
    private var sleepTargetChapterIdx = -1
    var currentIntroSec by mutableIntStateOf(0)
        private set
    var currentOutroSec by mutableIntStateOf(0)
        private set
    private var lastChapterIdx = -1
    var playerError by mutableStateOf<String?>(null)
        private set

    init {
        val sessionToken = SessionToken(
            application,
            ComponentName(application, PlaybackService::class.java)
        )
        val future = MediaController.Builder(application, sessionToken).buildAsync()
        future.addListener({
            try {
                val controller = future.get()
                mediaController = controller
                controller.addListener(object : Player.Listener {
                    override fun onIsPlayingChanged(playing: Boolean) {
                        isPlaying = playing
                    }
                    override fun onPlaybackStateChanged(playbackState: Int) {
                        if (playbackState == Player.STATE_ENDED) {
                            val sess = currentSession ?: return
                            if (currentTrackIndex + 1 < sess.tracks.size) {
                                loadTrackAndPlay(currentTrackIndex + 1, 0L)
                            }
                        }
                    }
                })
            } catch (_: Exception) {
            }
        }, ContextCompat.getMainExecutor(application))

        viewModelScope.launch {
            var tick = 0
            while (true) {
                delay(1000)
                val c = mediaController
                val sess = currentSession
                if (c != null && sess != null) {
                    val trackOffset = sess.tracks.getOrNull(currentTrackIndex)?.startOffset ?: 0.0
                    val posSec = trackOffset + (c.currentPosition.coerceAtLeast(0L) / 1000.0)
                    currentPositionSec = posSec
                    if (isPlaying) {
                        tick++
                        if (tick % 10 == 0 && activeUrl.isNotBlank() && activeToken.isNotBlank()) {
                            syncProgressAndUpdateLocal(sess.itemId, posSec, totalDurationSec)
                        }
                        val chIdx = sess.chapters.indexOfLast { posSec >= it.start }
                        if (sleepEndOfChapter && sleepTargetChapterIdx >= 0 && chIdx > sleepTargetChapterIdx) {
                            c.pause()
                            sleepEndOfChapter = false
                            sleepTargetChapterIdx = -1
                            syncProgressAndUpdateLocal(sess.itemId, posSec, totalDurationSec)
                        } else if (chIdx >= 0 && chIdx != lastChapterIdx) {
                            val ch = sess.chapters[chIdx]
                            if (currentIntroSec > 0 && posSec - ch.start < currentIntroSec && ch.end - ch.start > currentIntroSec + 5) {
                                seekToSec(ch.start + currentIntroSec)
                            }
                            lastChapterIdx = chIdx
                        } else if (chIdx >= 0 && currentOutroSec > 0) {
                            val ch = sess.chapters[chIdx]
                            if (ch.end > ch.start && ch.end - posSec <= currentOutroSec && chIdx + 1 < sess.chapters.size) {
                                val nextCh = sess.chapters[chIdx + 1]
                                seekToSec(nextCh.start + currentIntroSec)
                                lastChapterIdx = chIdx + 1
                            }
                        }
                        if (sleepRemainingSec > 0) {
                            sleepRemainingSec--
                            if (sleepRemainingSec == 0) {
                                c.pause()
                                sleepTimerMinutes = 0
                                syncProgressAndUpdateLocal(sess.itemId, posSec, totalDurationSec)
                            }
                        }
                    }
                }
            }
        }
    }

    fun playBook(book: BookItem) {
        playerError = null
        val local = offlineStore.findBook(activeUrl, book.id)
        if (local != null && local.tracks.isNotEmpty()) {
            playDownloadedBook(local)
            return
        }
        if (!isConnected) {
            playerError = "当前离线，且这本书尚未下载到本机"
            return
        }
        viewModelScope.launch {
            val res = api.startPlaybackSession(activeUrl, activeToken, book.id)
            res.onSuccess { sess ->
                startPlaybackSession(sess)
            }.onFailure { e ->
                playerError = e.message ?: "播放失败"
            }
        }
    }

    private fun startPlaybackSession(sess: PlaybackSessionData) {
        currentSession = sess
        totalDurationSec = sess.duration
        currentPositionSec = sess.currentTime
        currentIntroSec = store.getBookIntroSec(sess.itemId)
        currentOutroSec = store.getBookOutroSec(sess.itemId)
        lastChapterIdx = sess.chapters.indexOfLast { sess.currentTime >= it.start }
        val targetIdx = sess.tracks.indexOfLast { sess.currentTime >= it.startOffset }.coerceAtLeast(0)
        val trackStart = sess.tracks.getOrNull(targetIdx)?.startOffset ?: 0.0
        val offsetMs = ((sess.currentTime - trackStart).coerceAtLeast(0.0) * 1000).toLong()
        loadTrackAndPlay(targetIdx, offsetMs)
    }

    private fun loadTrackAndPlay(trackIndex: Int, offsetMs: Long) {
        val c = mediaController ?: return
        val sess = currentSession ?: return
        val track = sess.tracks.getOrNull(trackIndex) ?: return
        currentTrackIndex = trackIndex
        val meta = MediaMetadata.Builder()
            .setTitle(sess.title)
            .setArtist(sess.author)
            .setArtworkUri(Uri.parse(sess.coverUrl))
            .build()
        val item = MediaItem.Builder()
            .setUri(track.streamUrl)
            .setMediaMetadata(meta)
            .build()
        c.setMediaItem(item, offsetMs.coerceAtLeast(0L))
        c.prepare()
        c.setPlaybackSpeed(playbackSpeed)
        c.play()
    }

    fun togglePlayPause() {
        val c = mediaController ?: return
        if (c.isPlaying) {
            c.pause()
            val sess = currentSession
            if (sess != null && activeUrl.isNotBlank() && activeToken.isNotBlank()) {
                val trackOffset = sess.tracks.getOrNull(currentTrackIndex)?.startOffset ?: 0.0
                val pos = trackOffset + (c.currentPosition.coerceAtLeast(0L) / 1000.0)
                viewModelScope.launch {
                    syncProgressAndUpdateLocal(sess.itemId, pos, totalDurationSec)
                }
            }
        } else {
            c.play()
        }
    }

    fun seekToSec(sec: Double) {
        val c = mediaController ?: return
        val sess = currentSession ?: return
        val clamped = sec.coerceIn(0.0, totalDurationSec.coerceAtLeast(0.0))
        val targetIdx = sess.tracks.indexOfLast { clamped >= it.startOffset }.coerceAtLeast(0)
        val trackStart = sess.tracks.getOrNull(targetIdx)?.startOffset ?: 0.0
        val offsetMs = ((clamped - trackStart).coerceAtLeast(0.0) * 1000).toLong()
        if (targetIdx == currentTrackIndex) {
            c.seekTo(offsetMs)
        } else {
            loadTrackAndPlay(targetIdx, offsetMs)
        }
        currentPositionSec = clamped
    }

    fun skipBy(deltaSec: Double) {
        seekToSec(currentPositionSec + deltaSec)
    }

    fun cycleSpeed() {
        val speeds = listOf(1.0f, 1.25f, 1.5f, 2.0f)
        val next = speeds[(speeds.indexOf(playbackSpeed) + 1) % speeds.size]
        changeSpeed(next)
    }

    fun changeSpeed(speed: Float) {
        playbackSpeed = speed
        mediaController?.setPlaybackSpeed(speed)
    }

    fun setSleepTimer(minutes: Int) {
        sleepEndOfChapter = false
        sleepTargetChapterIdx = -1
        sleepTimerMinutes = minutes
        sleepRemainingSec = minutes * 60
    }

    fun toggleSleepEndOfChapter() {
        val sess = currentSession ?: return
        sleepTimerMinutes = 0
        sleepRemainingSec = 0
        if (sleepEndOfChapter) {
            sleepEndOfChapter = false
            sleepTargetChapterIdx = -1
        } else {
            sleepEndOfChapter = true
            sleepTargetChapterIdx = sess.chapters.indexOfLast { currentPositionSec >= it.start }.coerceAtLeast(0)
        }
    }

    fun updateBookSkip(introSec: Int, outroSec: Int) {
        val sess = currentSession ?: return
        currentIntroSec = introSec.coerceAtLeast(0)
        currentOutroSec = outroSec.coerceAtLeast(0)
        store.setBookIntroSec(sess.itemId, currentIntroSec)
        store.setBookOutroSec(sess.itemId, currentOutroSec)
    }

    fun refreshProgressOnly() {
        if (!isConnected || books.isEmpty()) return
        viewModelScope.launch {
            isRefreshing = true
            val statsTime = api.getListeningStats(activeUrl, activeToken)
            if (statsTime > 0) totalListeningSec = statsTime
            val progressMap = api.getProgressMap(activeUrl, activeToken)
            if (progressMap.isNotEmpty()) {
                books = books.map { b ->
                    val p = progressMap[b.id]
                    if (p != null) {
                        b.copy(
                            progress = p.first,
                            currentTime = p.second,
                            isFinished = p.first >= 0.995,
                            lastUpdate = p.third
                        )
                    } else b
                }
            }
            isRefreshing = false
        }
    }

    private suspend fun syncProgressAndUpdateLocal(itemId: String, posSec: Double, durSec: Double) {
        val sid = currentSession?.sessionId ?: ""
        val now = System.currentTimeMillis()
        offlineStore.updateProgress(activeUrl, itemId, posSec, now)
        downloadedBooks = offlineStore.allBooks()
        if (sid.isNotBlank()) {
            api.syncProgress(activeUrl, activeToken, sid, itemId, posSec, durSec, 10.0)
        }
        val prog = if (durSec > 0) (posSec / durSec).coerceIn(0.0, 1.0) else 0.0
        books = books.map { b ->
            if (b.id == itemId) {
                b.copy(
                    progress = prog,
                    currentTime = posSec,
                    isFinished = prog >= 0.995,
                    lastUpdate = now
                )
            } else b
        }
    }

    override fun onCleared() {
        mediaController?.release()
        mediaController = null
        super.onCleared()
    }
}

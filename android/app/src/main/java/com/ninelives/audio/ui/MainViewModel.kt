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
import androidx.media3.common.PlaybackException
import androidx.media3.common.Player
import androidx.media3.session.MediaController
import androidx.media3.session.SessionToken
import com.ninelives.audio.data.AbsApiClient
import com.ninelives.audio.data.AudioTrackInfo
import com.ninelives.audio.data.DownloadManager
import com.ninelives.audio.data.DownloadStore
import com.ninelives.audio.data.DownloadedBook
import com.ninelives.audio.data.DownloadTask
import com.ninelives.audio.data.Bookmark
import com.ninelives.audio.data.BookmarkStore
import com.ninelives.audio.data.ListenLog
import com.ninelives.audio.data.PendingBookmarkOp
import com.ninelives.audio.data.BookItem
import com.ninelives.audio.data.LibraryCache
import com.ninelives.audio.data.LibraryInfo
import com.ninelives.audio.data.PlaybackSessionData
import com.ninelives.audio.data.SavedServer
import com.ninelives.audio.data.SettingsStore
import com.ninelives.audio.data.AppUpdate
import com.ninelives.audio.data.UpdateChecker
import com.ninelives.audio.player.PlaybackService
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import java.io.File
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.SupervisorJob
import com.ninelives.audio.data.PlaybackReporter

class MainViewModel(application: Application) : AndroidViewModel(application) {
    private val store = SettingsStore(application)
    private val api = AbsApiClient()
    private val offlineStore = DownloadStore(application)
    private val libraryCache = LibraryCache(application)
    private val updater = UpdateChecker(application)
    private val booksByLibrary = mutableMapOf<String, List<BookItem>>()
    private var lastSyncAt = 0L
    private var lastProgressAt = 0L
    private var lastStatsAt = 0L
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

    private val logFile = File(application.noBackupFilesDir, "app_log.txt")
    private var logWrites = 0

    private fun stamp(): String =
        SimpleDateFormat("MM-dd HH:mm:ss", Locale.US).format(Date())

    private fun addLog(msg: String) {
        val line = "[${stamp()}] $msg"
        logs = (listOf(line) + logs).take(50)
        try {
            logFile.appendText(line + "\n")
            logWrites++
            if (logWrites % 40 == 0 && logFile.length() > 128_000) {
                val tail = logFile.readLines().takeLast(300)
                logFile.writeText(tail.joinToString("\n", postfix = "\n"))
            }
        } catch (_: Exception) {
        }
    }

    fun flushProgressNow() {
        PlaybackReporter.flush()
        addLog(t("已触发一次进度回传", "Progress sync triggered"))
    }

    fun clearLogs() {
        logs = emptyList()
        try {
            logFile.writeText("")
        } catch (_: Exception) {
        }
    }

    private fun loadRecentLogs() {
        try {
            if (logFile.isFile) {
                logs = logFile.readLines().takeLast(20).reversed()
            }
        } catch (_: Exception) {
        }
    }

    // Nine Lives: include any book with playback progress or lastUpdate > 0
    val nineLives: List<BookItem>
        get() = books
            .filter { (it.currentTime > 0.0 || it.progress > 0.0 || it.lastUpdate > 0L) && !it.isFinished }
            .sortedByDescending { it.lastUpdate }
            .take(9)

    /** Paints the shelf from the last sync instead of blocking on the network. */
    private fun restoreCachedLibrary() {
        val cached = libraryCache.load() ?: return
        if (!cached.serverUrl.trimEnd('/').equals(activeUrl.trimEnd('/'), true)) return
        val cachedBooks = cached.books.orEmpty()
        if (cachedBooks.isEmpty()) return
        libraries = cached.libraries.orEmpty()
        selectedLibrary = libraries.firstOrNull { it.id == cached.selectedLibraryId } ?: libraries.firstOrNull()
        selectedLibrary?.let { booksByLibrary[it.id] = cachedBooks }
        books = cachedBooks.map { b ->
            b.copy(isDownloaded = downloadedBooks.any { it.serverUrl == activeUrl && it.itemId == b.id })
        }
        if (cached.totalListeningSec > 0.0) totalListeningSec = cached.totalListeningSec
        lastSyncAt = cached.savedAt
        statusMessage = t("已显示本地缓存，正在后台同步", "Showing cached library, syncing in the background")
    }

    private fun isStale(maxAgeMs: Long): Boolean =
        lastSyncAt == 0L || System.currentTimeMillis() - lastSyncAt > maxAgeMs

    /** Called when the home tab becomes visible; cheap and throttled. */
    fun onHomeShown() {
        if (!isConnected) return
        if (System.currentTimeMillis() - lastProgressAt < PROGRESS_WINDOW_MS) return
        viewModelScope.launch { refreshProgress(force = false) }
    }

    private fun persistLibraryCache() {
        if (books.isEmpty()) return
        val id = selectedLibrary?.id ?: ""
        if (id.isNotEmpty()) booksByLibrary[id] = books
        libraryCache.save(activeUrl, libraries, id, books, totalListeningSec)
        lastSyncAt = System.currentTimeMillis()
    }

    private companion object {
        const val FRESH_WINDOW_MS = 10 * 60_000L
        const val PROGRESS_WINDOW_MS = 60_000L
        const val STATS_WINDOW_MS = 10 * 60_000L
    }

    fun loginAndConnect(name: String, url: String, username: String, password: String) {
        if (url.isBlank() || username.isBlank() || password.isBlank()) {
            statusMessage = t("请填写服务器地址、用户名和密码", "Enter server address, username and password")
            return
        }
        viewModelScope.launch {
            isSyncing = true
            statusMessage = t("正在连接...", "Connecting...")
            val res = api.login(url, username, password)
            res.onSuccess { token ->
                val normalized = url.replace(Regex("[\\s\\u200B-\\u200D\\uFEFF\"']+"), "").trimEnd('/')
                addLog(t("登录成功: $normalized", "Signed in: $normalized"))
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

                statusMessage = t("已连接到 $normalized", "Connected to $normalized")
                viewModelScope.launch { retryDownloadsFor(normalized, username.trim(), token) }
                // refreshAll() bails out while isSyncing is true, so release it first.
                isSyncing = false
                refreshAll()
            }.onFailure { err ->
                statusMessage = t("连接失败: ${err.message}", "Connection failed: ${err.message}")
                isSyncing = false
            }
        }
    }

    fun switchServer(server: SavedServer) {
        store.setActiveServer(server.url, server.username, server.token)
        activeUrl = server.url
        activeUsername = server.username
        activeToken = server.token
        statusMessage = t("已切换到 ${server.displayName}", "Switched to ${server.displayName}")
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
        booksByLibrary.clear()
        libraryCache.clear()
        lastSyncAt = 0L
        statusMessage = t("已断开连接", "Disconnected")
    }

    fun selectLibrary(lib: LibraryInfo) {
        selectedLibrary = lib
        val cached = booksByLibrary[lib.id]
        if (cached != null) {
            books = cached
            if (!isStale(FRESH_WINDOW_MS)) return
        }
        refreshBooks(lib.id)
    }

    fun isBookDownloaded(itemId: String): Boolean = downloadManager.isDownloaded(activeUrl, itemId)

    fun enqueueDownload(book: BookItem) {
        if (!isConnected) {
            statusMessage = t("请先连接服务器再下载", "Connect to a server before downloading")
            return
        }
        downloadManager.enqueue(book, activeUrl, activeUsername, activeToken)
        statusMessage = t("已加入下载队列：${book.title}", "Queued: ${book.title}")
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
            statusMessage = t("请先登录对应服务器后重试下载", "Sign in to that server and retry the download")
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

    // --- In-app update ---
    var currentVersion by mutableStateOf(updater.currentVersionName())
        private set
    var pendingUpdate by mutableStateOf<AppUpdate?>(null)
        private set
    var updateStatus by mutableStateOf("")
        private set
    var updateProgress by mutableIntStateOf(0)
        private set
    var isCheckingUpdate by mutableStateOf(false)
        private set
    var isDownloadingUpdate by mutableStateOf(false)
        private set
    private var downloadedUpdate: java.io.File? = null

    fun openReleasesPage() = updater.openReleasesPage()

    fun checkForUpdate() {
        if (isCheckingUpdate || isDownloadingUpdate) return
        viewModelScope.launch {
            isCheckingUpdate = true
            updateStatus = t("正在检查更新…", "Checking for updates...")
            val result = updater.check()
            result.onSuccess { update ->
                pendingUpdate = update
                updateStatus = when {
                    update == null -> t("已是最新版本（$currentVersion）", "Up to date ($currentVersion)")
                    else -> t("发现新版本 ${update.versionName}", "New version ${update.versionName} available")
                }
            }.onFailure {
                updateStatus = t("检查失败：", "Update check failed: ") + (it.message ?: t("网络错误", "network error"))
            }
            isCheckingUpdate = false
        }
    }

    fun downloadUpdate() {
        val update = pendingUpdate ?: return
        if (isDownloadingUpdate) return
        viewModelScope.launch {
            isDownloadingUpdate = true
            updateProgress = 0
            updateStatus = t("正在下载 ${update.versionName}…", "Downloading ${update.versionName}...")
            val result = updater.download(update) { percent -> updateProgress = percent }
            result.onSuccess { file ->
                downloadedUpdate = file
                updateStatus = t("下载完成，正在打开系统安装…", "Downloaded, opening the installer...")
                installUpdate()
            }.onFailure {
                updateStatus = t("下载失败：", "Download failed: ") + (it.message ?: t("网络错误", "network error"))
            }
            isDownloadingUpdate = false
        }
    }

    fun installUpdate() {
        val file = downloadedUpdate
        if (file == null) {
            downloadUpdate()
            return
        }
        if (!updater.canInstall()) {
            updateStatus = t("请允许“安装未知应用”，返回后再点安装", "Allow installing unknown apps, then come back and install")
            updater.openInstallPermission()
            return
        }
        runCatching { updater.install(file) }
            .onSuccess { updateStatus = t("已打开系统安装界面，确认即可覆盖升级", "Installer opened, confirm to upgrade in place") }
            .onFailure { updateStatus = t("无法启动安装：${it.message}", "Cannot start the installer: ${it.message}") }
    }

    fun refreshVersionLabel() {
        currentVersion = updater.currentVersionName()
    }

    fun refreshAll(force: Boolean = false) {
        if (!isConnected) return
        if (isSyncing && !force) return
        flushBookmarkOps()
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
                addLog(t("拉取书库失败: ${it.message}", "Failed to load libraries: ${it.message}"))
                statusMessage = t("拉取书库失败: ${it.message}", "Failed to load libraries: ${it.message}")
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
            booksByLibrary[libraryId] = books
            persistLibraryCache()
        }.onFailure {
            addLog(t("拉取书籍失败: ${it.message}", "Failed to load books: ${it.message}"))
        }
    }

    // --- Player state ---
    private var mediaController: MediaController? = null
    private var currentTrackIndex = 0
    private var pendingSession: PlaybackSessionData? = null
    private var playbackRetries = 0

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
    var volume by mutableFloatStateOf(1.0f)
        private set

    fun updateVolume(value: Float) {
        val clamped = value.coerceIn(0f, 1f)
        volume = clamped
        mediaController?.volume = clamped
    }
    var sleepTimerMinutes by mutableIntStateOf(0)
        private set
    var sleepRemainingSec by mutableIntStateOf(0)
        private set
    var sleepEndOfChapter by mutableStateOf(false)
        private set
    var themeMode by mutableStateOf(store.getThemeMode())
        private set
    var autoSyncEnabled by mutableStateOf(store.getAutoSync())
        private set

    fun updateLanguage(code: String) {
        Lang.code = code
        store.setLanguage(code)
    }

    fun updateThemeMode(mode: String) {
        themeMode = mode
        store.setThemeMode(mode)
    }

    fun updateAutoSync(enabled: Boolean) {
        autoSyncEnabled = enabled
        store.setAutoSync(enabled)
    }
    var wifiOnlyDownload by mutableStateOf(store.getWifiOnlyDownload())
        private set

    // --- Bookmarks ---
    var bookmarks by mutableStateOf<List<Bookmark>>(emptyList())
        private set
    var bookmarkStatus by mutableStateOf("")
        private set
    private var bookmarksItemId: String = ""
    private val bookmarkStore by lazy { BookmarkStore(application) }

    // --- Local listening log for the stats chart ---
    private val listenLog by lazy { ListenLog(application) }
    var listenWeek by mutableStateOf<List<Pair<Long, Long>>>(emptyList())
        private set

    fun updateWifiOnlyDownload(enabled: Boolean) {
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
    var lastSyncInfo by mutableStateOf("")
        private set

    init {
        Lang.code = store.getLanguage()
        loadRecentLogs()
        refreshListenWeek()
        val sessionToken = SessionToken(
            application,
            ComponentName(application, PlaybackService::class.java)
        )
        val future = MediaController.Builder(application, sessionToken).buildAsync()
        future.addListener({
            try {
                val controller = future.get()
                mediaController = controller
                controller.volume = volume
                pendingSession?.let { queued ->
                    pendingSession = null
                    startPlaybackSession(queued)
                }
                controller.addListener(object : Player.Listener {
                    override fun onIsPlayingChanged(playing: Boolean) {
                        val wasPlaying = isPlaying
                        isPlaying = playing
                        if (wasPlaying && !playing) {
                            // Flush immediately: the last partial window would otherwise be lost.
                            listenLog.flush()
                            PlaybackReporter.flush()
                        }
                    }
                    override fun onPlayerError(error: PlaybackException) {
                        playerError = t(
                            "播放出错：${error.errorCodeName}",
                            "Playback error: ${error.errorCodeName}"
                        )
                        addLog("player error ${error.errorCodeName} (${error.errorCode}) at ${currentPositionSec.toInt()}s")
                        if (playbackRetries >= 2) return
                        playbackRetries++
                        val offsetMs = mediaController?.currentPosition?.coerceAtLeast(0L) ?: 0L
                        val index = currentTrackIndex
                        viewModelScope.launch {
                            delay(1_500)
                            loadTrackAndPlay(index, offsetMs)
                        }
                    }
                    override fun onPlaybackStateChanged(playbackState: Int) {
                        if (playbackState == Player.STATE_READY) playbackRetries = 0
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
                        if (tick % 5 == 0) {
                            lastSyncInfo = if (PlaybackReporter.lastSuccessAt > 0L) {
                                "${PlaybackReporter.lastResult} · ${PlaybackReporter.lastSentSec.toInt()}s"
                            } else ""
                        }
                        if (tick % 30 == 0) refreshListenWeek()
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
            playerError = t("当前离线，且这本书尚未下载到本机", "You are offline and this book is not downloaded")
            return
        }
        viewModelScope.launch {
            val res = api.startPlaybackSession(activeUrl, activeToken, book.id)
            res.onSuccess { sess ->
                startPlaybackSession(sess)
            }.onFailure { e ->
                playerError = e.message ?: t("播放失败", "Playback failed")
            }
        }
    }

    private fun startPlaybackSession(sess: PlaybackSessionData) {
        if (mediaController == null) {
            pendingSession = sess
            return
        }
        val previous = currentSession
        if (previous != null && previous.sessionId.isNotBlank() && previous.sessionId != sess.sessionId) {
            PlaybackReporter.close()
        }
        currentSession = sess
        loadBookmarks(sess.itemId)
        totalDurationSec = sess.duration
        currentPositionSec = sess.currentTime
        currentIntroSec = store.getBookIntroSec(sess.itemId)
        currentOutroSec = store.getBookOutroSec(sess.itemId)
        lastChapterIdx = sess.chapters.indexOfLast { sess.currentTime >= it.start }
        val targetIdx = sess.tracks.indexOfLast { sess.currentTime >= it.startOffset }.coerceAtLeast(0)
        val trackStart = sess.tracks.getOrNull(targetIdx)?.startOffset ?: 0.0
        val offsetMs = ((sess.currentTime - trackStart).coerceAtLeast(0.0) * 1000).toLong()
        PlaybackReporter.configure(activeUrl, activeToken, sess.itemId, sess.sessionId, sess.duration, trackStart)
        loadTrackAndPlay(targetIdx, offsetMs)
    }

    private fun loadTrackAndPlay(trackIndex: Int, offsetMs: Long) {
        val c = mediaController ?: return
        val sess = currentSession ?: return
        val track = sess.tracks.getOrNull(trackIndex) ?: return
        currentTrackIndex = trackIndex
        PlaybackReporter.setTrackOffset(track.startOffset)
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

    fun currentAbsolutePosition(): Double {
        val sess = currentSession ?: return currentPositionSec
        val c = mediaController ?: return currentPositionSec
        val trackOffset = sess.tracks.getOrNull(currentTrackIndex)?.startOffset ?: 0.0
        return trackOffset + (c.currentPosition.coerceAtLeast(0L) / 1000.0)
    }

    fun loadBookmarks(itemId: String = currentSession?.itemId ?: "") {
        if (itemId.isBlank()) return
        bookmarksItemId = itemId
        val cached = bookmarkStore.cached(itemId)
        if (cached.isNotEmpty()) bookmarks = cached
        if (activeUrl.isBlank() || activeToken.isBlank()) return
        viewModelScope.launch {
            api.getBookmarks(activeUrl, activeToken, itemId)
                .onSuccess { list ->
                    if (bookmarksItemId == itemId) {
                        val locals = bookmarks.filter { it.id.startsWith("local-") }
                        bookmarks = (list + locals).sortedBy { it.time }
                        bookmarkStore.saveCache(itemId, bookmarks)
                    }
                }
                .onFailure {
                    bookmarkStatus = t(
                        "书签加载失败：${it.message}",
                        "Failed to load bookmarks: ${it.message}"
                    )
                }
        }
    }

    fun addBookmarkAtCurrent(title: String = "") {
        val sess = currentSession
        if (sess == null) {
            bookmarkStatus = t("请先打开一本书", "Open a book first")
            return
        }
        val pos = currentAbsolutePosition()
        val label = title.trim().ifBlank { t("书签 ", "Bookmark ") + formatClock(pos) }
        val local = Bookmark(
            id = "local-" + System.currentTimeMillis(),
            itemId = sess.itemId,
            title = label,
            time = pos,
            createdAt = System.currentTimeMillis()
        )
        bookmarks = (bookmarks + local).sortedBy { it.time }
        bookmarkStore.saveCache(sess.itemId, bookmarks)
        if (activeUrl.isBlank() || activeToken.isBlank()) {
            bookmarkStore.addPending(PendingBookmarkOp("add", sess.itemId, label, pos))
            bookmarkStatus = t("已离线保存，联网后自动同步", "Saved offline, will sync when online")
            return
        }
        viewModelScope.launch {
            api.createBookmark(activeUrl, activeToken, sess.itemId, label, pos)
                .onSuccess {
                    bookmarkStatus = t("已添加：$label", "Added: $label")
                    loadBookmarks(sess.itemId)
                }
                .onFailure {
                    bookmarkStore.addPending(PendingBookmarkOp("add", sess.itemId, label, pos))
                    bookmarkStatus = t("已本地保存，稍后自动重试", "Saved locally, will retry automatically")
                }
        }
    }

    fun deleteBookmark(bookmark: Bookmark) {
        bookmarks = bookmarks.filterNot { it.id == bookmark.id }
        bookmarkStore.saveCache(bookmark.itemId, bookmarks)
        if (bookmark.id.startsWith("local-")) {
            bookmarkStore.dropPendingAdd(bookmark.itemId, bookmark.time)
            return
        }
        if (activeUrl.isBlank() || activeToken.isBlank()) {
            bookmarkStore.addPending(PendingBookmarkOp("delete", bookmark.itemId, "", bookmark.time))
            bookmarkStatus = t("已离线删除，联网后同步", "Deleted offline, will sync when online")
            return
        }
        viewModelScope.launch {
            api.deleteBookmark(activeUrl, activeToken, bookmark.itemId, bookmark.time)
                .onSuccess { bookmarkStatus = t("已删除", "Deleted") }
                .onFailure {
                    bookmarkStore.addPending(PendingBookmarkOp("delete", bookmark.itemId, "", bookmark.time))
                    bookmarkStatus = t("已本地删除，稍后自动重试", "Deleted locally, will retry automatically")
                }
        }
    }

    /** Pushes queued bookmark changes once the server is reachable. */
    fun flushBookmarkOps() {
        if (activeUrl.isBlank() || activeToken.isBlank()) return
        val ops = bookmarkStore.pending()
        if (ops.isEmpty()) return
        viewModelScope.launch {
            var changed = false
            for (op in ops) {
                val ok = if (op.type == "add") {
                    api.createBookmark(activeUrl, activeToken, op.itemId, op.title, op.time).isSuccess
                } else {
                    api.deleteBookmark(activeUrl, activeToken, op.itemId, op.time).isSuccess
                }
                if (ok) {
                    bookmarkStore.removePending(op)
                    changed = true
                } else {
                    break
                }
            }
            if (changed) {
                bookmarkStatus = t("书签已同步", "Bookmarks synced")
                loadBookmarks()
            }
        }
    }

    fun jumpToBookmark(bookmark: Bookmark) {
        seekToSec(bookmark.time)
    }

    fun refreshListenWeek() {
        listenWeek = listenLog.lastDays(7)
    }

    private fun formatClock(sec: Double): String {
        val total = sec.toInt().coerceAtLeast(0)
        return "%d:%02d".format(total / 60, total % 60)
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
        if (!isConnected) return
        viewModelScope.launch { refreshProgress(force = true) }
    }

    private suspend fun refreshProgress(force: Boolean) {
        if (!isConnected) return
        val now = System.currentTimeMillis()
        if (!force && now - lastProgressAt < PROGRESS_WINDOW_MS) return
        lastProgressAt = now
        isRefreshing = true
        try {
            if (now - lastStatsAt > STATS_WINDOW_MS) {
                val statsTime = api.getListeningStats(activeUrl, activeToken)
                if (statsTime > 0) totalListeningSec = statsTime
                lastStatsAt = now
            }
            if (books.isEmpty()) return
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
                persistLibraryCache()
            }
        } finally {
            isRefreshing = false
        }
    }

    private suspend fun syncProgressAndUpdateLocal(itemId: String, posSec: Double, durSec: Double) {
        val now = System.currentTimeMillis()
        offlineStore.updateProgress(activeUrl, itemId, posSec, now)
        downloadedBooks = offlineStore.allBooks()
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
        restoreCachedLibrary()
        if (isConnected && isStale(FRESH_WINDOW_MS)) {
            refreshAll()
        } else if (isConnected) {
            viewModelScope.launch { refreshProgress(force = true) }
        }
    }

    override fun onCleared() {
        listenLog.flush()
        PlaybackReporter.close()
        mediaController?.release()
        mediaController = null
        super.onCleared()
    }
}

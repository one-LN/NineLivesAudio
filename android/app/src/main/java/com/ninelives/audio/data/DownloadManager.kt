package com.ninelives.audio.data

import android.content.Context
import android.net.ConnectivityManager
import android.net.NetworkCapabilities
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Semaphore
import kotlinx.coroutines.sync.withPermit
import java.io.File
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.CopyOnWriteArraySet

class DownloadManager(context: Context, private val api: AbsApiClient, private val store: DownloadStore) {
    private val appContext = context.applicationContext
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val semaphore = Semaphore(2)
    private val jobs = ConcurrentHashMap<String, Job>()
    private val activeTokens = ConcurrentHashMap<String, String>()
    private val observers = CopyOnWriteArraySet<() -> Unit>()
    private val progressLocks = ConcurrentHashMap<String, Any>()
    @Volatile private var wifiOnly = true

    fun observe(observer: () -> Unit) { observers.add(observer) }
    fun removeObserver(observer: () -> Unit) { observers.remove(observer) }
    fun tasks(): List<DownloadTask> = store.queue()
    fun downloadedBooks(): List<DownloadedBook> = store.allBooks()
    fun isDownloaded(serverUrl: String, itemId: String): Boolean = store.findBook(serverUrl, itemId) != null
    fun setWifiOnly(enabled: Boolean) { wifiOnly = enabled }
    private fun notifyObservers() = observers.forEach { runCatching { it() } }

    fun enqueue(book: BookItem, serverUrl: String, username: String, token: String) {
        val server = serverUrl.trim().trimEnd('/')
        val key = taskKey(server, book.id)
        if (store.findBook(server, book.id) != null || store.queue().any { it.key == key }) return
        activeTokens[key] = token
        store.putTask(
            DownloadTask(
                key = key,
                itemId = book.id,
                libraryId = book.libraryId,
                title = book.title,
                author = book.author,
                serverUrl = server,
                username = username
            )
        )
        notifyObservers()
        runTask(key)
    }

    fun resume(key: String, tokenProvider: (String, String) -> String?) {
        val task = store.queue().firstOrNull { it.key == key } ?: return
        val token = tokenProvider(task.serverUrl, task.username) ?: return
        activeTokens[key] = token
        store.putTask(task.copy(state = "queued", error = ""))
        runTask(key)
    }

    fun retryAll(tokenProvider: (String, String) -> String?) {
        store.queue().forEach { saved ->
            val token = tokenProvider(saved.serverUrl, saved.username)
            when (saved.state) {
                "downloading", "queued", "waiting_wifi" -> {
                    if (token != null) {
                        activeTokens[saved.key] = token
                        store.putTask(saved.copy(state = "queued"))
                        runTask(saved.key)
                    } else {
                        store.putTask(saved.copy(state = "failed", error = "请重新登录此服务器后继续下载"))
                    }
                }
                "failed", "paused" -> if (token != null) activeTokens[saved.key] = token
            }
        }
        notifyObservers()
    }

    fun pause(key: String) {
        val task = store.queue().firstOrNull { it.key == key } ?: return
        store.putTask(task.copy(state = "paused"))
        jobs[key]?.cancel()
        notifyObservers()
    }

    suspend fun cancel(key: String) {
        jobs.remove(key)?.cancelAndJoin()
        val task = store.queue().firstOrNull { it.key == key } ?: return
        activeTokens.remove(key)
        store.removeTask(key)
        if (store.findBook(task.serverUrl, task.itemId) == null) {
            store.bookFolder(task.serverUrl, task.itemId).deleteRecursively()
        }
        notifyObservers()
    }

    fun delete(serverUrl: String, itemId: String) {
        val key = taskKey(serverUrl, itemId)
        scope.launch {
            jobs.remove(key)?.cancelAndJoin()
            activeTokens.remove(key)
            store.removeTask(key)
            store.deleteBook(serverUrl, itemId)
            notifyObservers()
        }
    }

    private fun runTask(key: String) {
        if (jobs[key]?.isActive == true) return
        val task = store.queue().firstOrNull { it.key == key } ?: return
        if (activeTokens[key].isNullOrBlank()) {
            store.putTask(task.copy(state = "failed", error = "请重新连接服务器后重新下载"))
            notifyObservers()
            return
        }
        val job = scope.launch {
            try {
                semaphore.withPermit { process(task) }
            } finally {
                jobs.remove(key)
                notifyObservers()
            }
        }
        jobs[key] = job
    }

    private suspend fun process(initial: DownloadTask) {
        val key = initial.key
        var task = store.queue().firstOrNull { it.key == key } ?: return
        val token = activeTokens[key] ?: return
        if (wifiOnly && !isOnWifi()) {
            store.putTask(task.copy(state = "waiting_wifi", error = "等待 Wi-Fi 网络"))
            notifyObservers()
            return
        }
        var currentTask = task.copy(state = "downloading", error = "")
        store.putTask(currentTask)
        notifyObservers()

        var manifest: DownloadManifest
        if (task.manifest != null) {
            manifest = task.manifest
        } else {
            val manifestResult = api.getDownloadManifest(
                serverUrl = task.serverUrl,
                token = token,
                itemId = task.itemId,
                fallbackTitle = task.title,
                fallbackAuthor = task.author,
                fallbackDescription = ""
            )
            val loadedManifest = manifestResult.getOrNull()
            if (loadedManifest == null) {
                val error = manifestResult.exceptionOrNull() ?: Exception("无法获取音频文件信息")
                store.putTask(task.copy(state = "failed", error = error.message ?: "无法获取音频文件信息"))
                notifyObservers()
                return
            }
            manifest = loadedManifest
            task = task.copy(manifest = manifest)
            currentTask = currentTask.copy(manifest = manifest)
            store.putTask(currentTask)
        }
        val totalBytes = manifest.files.sumOf { it.size.coerceAtLeast(0L) }
        currentTask = currentTask.copy(totalBytes = totalBytes)
        store.putTask(currentTask)
        val tracks = mutableListOf<DownloadedTrack>()
        var completedBytes = 0L
        var lastPersistedBytes = 0L
        try {
            for (file in manifest.files) {
                val finalFile = store.trackFile(task.serverUrl, task.itemId, file.index, file.ino)
                val partFile = store.partFile(task.serverUrl, task.itemId, file.index)
                val offsetSeconds = manifest.files.takeWhile { it.index < file.index }.sumOf { it.duration.coerceAtLeast(0.0) }
                if (finalFile.isFile && (file.size <= 0L || finalFile.length() == file.size)) {
                    tracks += DownloadedTrack(file.index, file.ino, file.filename, offsetSeconds, file.duration, finalFile.toURI().toString())
                    completedBytes += finalFile.length()
                    currentTask = currentTask.copy(currentFileIndex = file.index + 1, downloadedBytes = completedBytes)
                    store.putTask(currentTask)
                    continue
                }
                var attempts = 0
                while (true) {
                    currentTask = currentTask.copy(
                        currentFileIndex = file.index,
                        downloadedBytes = completedBytes + if (partFile.isFile) partFile.length() else 0L
                    )
                    store.putTask(currentTask)
                    notifyObservers()
                    val resumeOffset = if (partFile.isFile) partFile.length() else 0L
                    var writtenThisAttempt = 0L
                    val result = api.downloadAudioFile(
                        task.serverUrl, token, task.itemId, file.ino, partFile, resumeOffset
                    ) { count ->
                        synchronized(progressLocks.computeIfAbsent(key) { Any() }) {
                            writtenThisAttempt += count
                            currentTask = currentTask.copy(
                                downloadedBytes = completedBytes + resumeOffset + writtenThisAttempt
                            )
                            if (currentTask.downloadedBytes - lastPersistedBytes >= 512 * 1024) {
                                lastPersistedBytes = currentTask.downloadedBytes
                                store.putTask(currentTask)
                            }
                        }
                        notifyObservers()
                    }
                    val fileSize = result.getOrNull()
                    if (fileSize == null) {
                        val error = result.exceptionOrNull() ?: Exception("下载失败")
                        if (!jobs[key]?.isActive.orFalse()) throw CancellationException("下载已暂停或取消")
                        if (++attempts >= 3) {
                            store.putTask(currentTask.copy(state = "failed", retryCount = attempts, error = error.message ?: "下载失败"))
                            notifyObservers()
                            return
                        }
                        delay(1000L * (1L shl attempts))
                        continue
                    }
                    if (file.size > 0L && fileSize != file.size) {
                        store.putTask(currentTask.copy(state = "failed", error = "${file.filename} 大小校验失败"))
                        notifyObservers()
                        return
                    }
                    if (finalFile.exists()) finalFile.delete()
                    if (!partFile.renameTo(finalFile)) {
                        partFile.copyTo(finalFile, overwrite = true)
                        partFile.delete()
                    }
                    tracks += DownloadedTrack(file.index, file.ino, file.filename, offsetSeconds, file.duration, finalFile.toURI().toString())
                    completedBytes += fileSize
                    currentTask = currentTask.copy(downloadedBytes = completedBytes, currentFileIndex = file.index + 1)
                    lastPersistedBytes = completedBytes
                    store.putTask(currentTask)
                    break
                }
            }

            val cover = store.coverFile(task.serverUrl, task.itemId)
            if (!cover.isFile) api.downloadCover(task.serverUrl, token, task.itemId, cover)
            val book = DownloadedBook(
                serverUrl = task.serverUrl,
                itemId = task.itemId,
                libraryId = task.libraryId,
                title = manifest.title,
                author = manifest.author,
                narrator = manifest.narrator,
                duration = manifest.duration,
                description = manifest.description,
                coverUri = "${task.serverUrl.trimEnd('/')}/api/items/${task.itemId}/cover",
                localCoverUri = if (cover.isFile) cover.toURI().toString() else "",
                tracks = tracks.sortedBy { it.index },
                chapters = manifest.chapters
            )
            store.saveBook(book)
            store.removeTask(key)
            activeTokens.remove(key)
            notifyObservers()
        } catch (cancelled: CancellationException) {
            val latest = store.queue().firstOrNull { it.key == key } ?: currentTask
            if (latest.state != "paused") store.putTask(latest.copy(state = "paused"))
            notifyObservers()
            throw cancelled
        } catch (error: Exception) {
            val latest = store.queue().firstOrNull { it.key == key } ?: currentTask
            store.putTask(latest.copy(state = "failed", error = error.message ?: "下载失败"))
            notifyObservers()
        }
    }

    private fun isOnWifi(): Boolean {
        val manager = appContext.getSystemService(Context.CONNECTIVITY_SERVICE) as? ConnectivityManager ?: return false
        val network = manager.activeNetwork ?: return false
        val caps = manager.getNetworkCapabilities(network) ?: return false
        return caps.hasTransport(NetworkCapabilities.TRANSPORT_WIFI) || caps.hasTransport(NetworkCapabilities.TRANSPORT_ETHERNET)
    }

    private fun Boolean?.orFalse(): Boolean = this ?: false

    private fun taskKey(serverUrl: String, itemId: String): String = "${serverUrl.trimEnd('/')}::$itemId"
}

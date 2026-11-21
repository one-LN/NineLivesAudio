package com.ninelives.audio.ui

import android.app.Application
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.ninelives.audio.data.AbsApiClient
import com.ninelives.audio.data.BookItem
import com.ninelives.audio.data.LibraryInfo
import com.ninelives.audio.data.SavedServer
import com.ninelives.audio.data.SettingsStore
import kotlinx.coroutines.launch

class MainViewModel(application: Application) : AndroidViewModel(application) {
    private val store = SettingsStore(application)
    private val api = AbsApiClient()

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

    var libraries by mutableStateOf<List<LibraryInfo>>(emptyList())
        private set
    var selectedLibrary by mutableStateOf<LibraryInfo?>(null)
        private set
    var books by mutableStateOf<List<BookItem>>(emptyList())
        private set

    val nineLives: List<BookItem>
        get() = books
            .filter { it.hasProgress && !it.isFinished }
            .sortedByDescending { it.lastUpdate }
            .take(9)

    init {
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
                val normalized = url.trim().trimEnd('/')
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

    fun refreshAll() {
        if (!isConnected) return
        viewModelScope.launch {
            isSyncing = true
            val libsRes = api.getLibraries(activeUrl, activeToken)
            libsRes.onSuccess { libs ->
                libraries = libs
                val target = selectedLibrary?.takeIf { s -> libs.any { it.id == s.id } } ?: libs.firstOrNull()
                selectedLibrary = target
                if (target != null) {
                    loadBooksWithProgress(target.id)
                }
            }.onFailure {
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
            books = list.map { b ->
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
    }
}

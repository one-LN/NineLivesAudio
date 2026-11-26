package com.ninelives.audio.data

import android.content.Context
import com.google.gson.Gson
import java.io.File

data class CachedLibraryState(
    val savedAt: Long = 0L,
    val serverUrl: String = "",
    val libraries: List<LibraryInfo>? = null,
    val selectedLibraryId: String = "",
    val books: List<BookItem>? = null,
    val totalListeningSec: Double = 0.0
)

/**
 * Snapshot of the last successful library sync, so a cold start can paint the
 * shelf immediately instead of waiting for the network.
 */
class LibraryCache(context: Context) {
    private val file = File(context.noBackupFilesDir, "library_cache.json")
    private val gson = Gson()

    fun load(): CachedLibraryState? = try {
        if (!file.isFile) null else gson.fromJson(file.readText(), CachedLibraryState::class.java)
    } catch (_: Exception) {
        null
    }

    fun save(
        serverUrl: String,
        libraries: List<LibraryInfo>,
        selectedLibraryId: String,
        books: List<BookItem>,
        totalListeningSec: Double
    ) {
        runCatching {
            val state = CachedLibraryState(
                savedAt = System.currentTimeMillis(),
                serverUrl = serverUrl.trimEnd('/'),
                libraries = libraries,
                selectedLibraryId = selectedLibraryId,
                books = books,
                totalListeningSec = totalListeningSec
            )
            val tmp = File(file.parentFile, "${file.name}.tmp")
            tmp.writeText(gson.toJson(state))
            if (file.exists()) file.delete()
            tmp.renameTo(file)
        }
    }

    fun clear() {
        runCatching { file.delete() }
    }
}

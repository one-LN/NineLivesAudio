package com.ninelives.audio.data

import android.content.Context
import com.google.gson.Gson
import java.io.File

data class CachedLibraryState(
    val version: Int = 0,
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
        if (!file.isFile) null else {
            val raw = gson.fromJson(file.readText(), CachedLibraryState::class.java)
            if (raw == null) null else {
                // Gson bypasses Kotlin default values. A cache written before a
                // BookItem field existed leaves that non-null field as null, and
                // BookItem.copy() then throws at startup. Rebuild every book with
                // explicit defaults instead of trusting the deserialised object.
                CachedLibraryState(
                    version = CACHE_VERSION,
                    savedAt = raw.savedAt,
                    serverUrl = raw.serverUrl,
                    libraries = raw.libraries,
                    selectedLibraryId = raw.selectedLibraryId,
                    books = raw.books?.mapNotNull { it.sanitized() },
                    totalListeningSec = raw.totalListeningSec
                )
            }
        }
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
                version = CACHE_VERSION,
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

    private fun BookItem.sanitized(): BookItem? = try {
        val rawGenres: Any? = genres
        val rawTags: Any? = tags
        val rawSeries: Any? = seriesNames
        BookItem(
            id = id,
            libraryId = libraryId,
            title = title,
            author = author,
            narrator = narrator,
            duration = duration,
            description = description,
            coverUrl = coverUrl,
            progress = progress,
            currentTime = currentTime,
            isFinished = isFinished,
            lastUpdate = lastUpdate,
            isDownloaded = isDownloaded,
            addedAt = addedAt,
            genres = (rawGenres as? List<*>)?.mapNotNull { it as? String } ?: emptyList(),
            tags = (rawTags as? List<*>)?.mapNotNull { it as? String } ?: emptyList(),
            seriesNames = (rawSeries as? List<*>)?.mapNotNull { it as? String } ?: emptyList()
        )
    } catch (_: Exception) {
        null
    }

    private companion object {
        /** Bump when BookItem gains a non-null field. */
        const val CACHE_VERSION = 2
    }
}

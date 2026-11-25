package com.ninelives.audio.data

import android.content.Context
import com.google.gson.Gson
import com.google.gson.reflect.TypeToken
import java.io.File

class DownloadStore(context: Context) {
    private val root = File(context.noBackupFilesDir, "audiobooks").apply { mkdirs() }
    private val indexFile = File(root, "library.json")
    private val queueFile = File(root, "queue.json")
    private val gson = Gson()
    private val lock = Any()

    fun allBooks(): List<DownloadedBook> = synchronized(lock) {
        readList(indexFile, object : TypeToken<List<DownloadedBook>>() {}.type)
    }

    fun findBook(serverUrl: String, itemId: String): DownloadedBook? =
        allBooks().firstOrNull { it.serverUrl == serverUrl && it.itemId == itemId }

    fun saveBook(book: DownloadedBook) = synchronized(lock) {
        val updated = allBooks().filterNot { it.serverUrl == book.serverUrl && it.itemId == book.itemId } + book
        writeAtomic(indexFile, gson.toJson(updated))
    }

    fun deleteBook(serverUrl: String, itemId: String) = synchronized(lock) {
        val updated = allBooks().filterNot { it.serverUrl == serverUrl && it.itemId == itemId }
        writeAtomic(indexFile, gson.toJson(updated))
        bookFolder(serverUrl, itemId).deleteRecursively()
    }

    fun updateProgress(serverUrl: String, itemId: String, currentTime: Double, now: Long) = synchronized(lock) {
        val book = findBook(serverUrl, itemId) ?: return@synchronized
        saveBook(book.copy(currentTime = currentTime.coerceAtLeast(0.0).coerceAtMost(book.duration), lastUpdate = now))
    }

    fun queue(): List<DownloadTask> = synchronized(lock) {
        readList(queueFile, object : TypeToken<List<DownloadTask>>() {}.type)
    }

    fun putTask(task: DownloadTask) = synchronized(lock) {
        val updated = queue().filterNot { it.key == task.key } + task
        writeAtomic(queueFile, gson.toJson(updated))
    }

    fun removeTask(key: String) = synchronized(lock) {
        writeAtomic(queueFile, gson.toJson(queue().filterNot { it.key == key }))
    }

    fun bookFolder(serverUrl: String, itemId: String): File = File(root, "${safe(serverUrl)}_${safe(itemId)}")

    fun partFile(serverUrl: String, itemId: String, index: Int): File =
        File(bookFolder(serverUrl, itemId), "track_${index.toString().padStart(3, '0')}.part")

    fun trackFile(serverUrl: String, itemId: String, index: Int, fileIno: String): File =
        File(bookFolder(serverUrl, itemId), "track_${index.toString().padStart(3, '0')}_${safe(fileIno)}.audio")

    fun coverFile(serverUrl: String, itemId: String): File = File(bookFolder(serverUrl, itemId), "cover.img")

    private fun safe(value: String): String {
        val digest = java.security.MessageDigest.getInstance("SHA-256").digest(value.toByteArray(Charsets.UTF_8))
        return digest.take(16).joinToString("") { "%02x".format(it) }
    }

    private fun <T> readList(file: File, type: java.lang.reflect.Type): List<T> = try {
        if (!file.exists()) emptyList() else gson.fromJson<List<T>>(file.readText(), type) ?: emptyList()
    } catch (_: Exception) { emptyList() }

    private fun writeAtomic(file: File, text: String) {
        file.parentFile?.mkdirs()
        val tmp = File(file.parentFile, file.name + ".tmp")
        tmp.writeText(text)
        if (!tmp.renameTo(file)) {
            file.writeText(text)
            tmp.delete()
        }
    }
}

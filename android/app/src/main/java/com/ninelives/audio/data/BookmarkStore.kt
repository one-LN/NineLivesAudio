package com.ninelives.audio.data

import android.content.Context
import com.google.gson.Gson
import com.google.gson.reflect.TypeToken

/** A bookmark change waiting to be pushed to the server. */
data class PendingBookmarkOp(
    val type: String,   // "add" | "delete"
    val itemId: String,
    val title: String,
    val time: Double
)

/** Offline cache of bookmarks per book plus a queue of pending changes. */
class BookmarkStore(context: Context) {
    private val prefs = context.getSharedPreferences("bookmark_store", Context.MODE_PRIVATE)
    private val gson = Gson()

    fun cached(itemId: String): List<Bookmark> {
        val raw = prefs.getString("cache_$itemId", null) ?: return emptyList()
        return try {
            val type = object : TypeToken<List<Bookmark>>() {}.type
            gson.fromJson<List<Bookmark>>(raw, type) ?: emptyList()
        } catch (_: Exception) {
            emptyList()
        }
    }

    fun saveCache(itemId: String, list: List<Bookmark>) {
        prefs.edit().putString("cache_$itemId", gson.toJson(list)).apply()
    }

    fun pending(): List<PendingBookmarkOp> {
        val raw = prefs.getString("pending", null) ?: return emptyList()
        return try {
            val type = object : TypeToken<List<PendingBookmarkOp>>() {}.type
            gson.fromJson<List<PendingBookmarkOp>>(raw, type) ?: emptyList()
        } catch (_: Exception) {
            emptyList()
        }
    }

    fun addPending(op: PendingBookmarkOp) {
        val list = pending().toMutableList()
        list.add(op)
        prefs.edit().putString("pending", gson.toJson(list)).apply()
    }

    fun removePending(op: PendingBookmarkOp) {
        val list = pending().toMutableList()
        list.removeAll { it.type == op.type && it.itemId == op.itemId && kotlin.math.abs(it.time - op.time) < 0.001 }
        prefs.edit().putString("pending", gson.toJson(list)).apply()
    }

    fun dropPendingAdd(itemId: String, time: Double) {
        val list = pending().toMutableList()
        list.removeAll { it.type == "add" && it.itemId == itemId && kotlin.math.abs(it.time - time) < 0.001 }
        prefs.edit().putString("pending", gson.toJson(list)).apply()
    }

    fun pendingCount(): Int = pending().size
}

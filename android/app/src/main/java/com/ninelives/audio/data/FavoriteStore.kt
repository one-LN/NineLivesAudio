package com.ninelives.audio.data

import android.content.Context

/** Device-side favorites, used when the server account may not modify collections. */
class FavoriteStore(context: Context) {
    private val prefs = context.getSharedPreferences("favorites", Context.MODE_PRIVATE)

    private fun key(server: String, user: String): String =
        server.trimEnd('/').lowercase() + "|" + user.lowercase()

    fun ids(server: String, user: String): Set<String> =
        prefs.getStringSet(key(server, user), emptySet())?.toSet() ?: emptySet()

    fun setFavorite(server: String, user: String, itemId: String, favorite: Boolean): Set<String> {
        val k = key(server, user)
        val current = ids(server, user).toMutableSet()
        if (favorite) current.add(itemId) else current.remove(itemId)
        prefs.edit().putStringSet(k, current).apply()
        return current
    }
}

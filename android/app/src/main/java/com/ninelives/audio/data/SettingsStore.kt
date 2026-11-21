package com.ninelives.audio.data

import android.content.Context
import com.google.gson.Gson
import com.google.gson.reflect.TypeToken

class SettingsStore(context: Context) {
    private val prefs = context.getSharedPreferences("ninelives_settings", Context.MODE_PRIVATE)
    private val gson = Gson()

    fun getSavedServers(): List<SavedServer> {
        val json = prefs.getString("saved_servers", null) ?: return emptyList()
        return try {
            val type = object : TypeToken<List<SavedServer>>() {}.type
            gson.fromJson(json, type) ?: emptyList()
        } catch (_: Exception) {
            emptyList()
        }
    }

    fun saveServers(servers: List<SavedServer>) {
        prefs.edit().putString("saved_servers", gson.toJson(servers)).apply()
    }

    fun getActiveUrl(): String = prefs.getString("active_url", "") ?: ""
    fun getActiveUsername(): String = prefs.getString("active_username", "") ?: ""
    fun getActiveToken(): String = prefs.getString("active_token", "") ?: ""

    fun setActiveServer(url: String, username: String, token: String) {
        prefs.edit()
            .putString("active_url", url.trim().trimEnd('/'))
            .putString("active_username", username.trim())
            .putString("active_token", token)
            .apply()
    }

    fun clearActiveServer() {
        prefs.edit()
            .remove("active_url")
            .remove("active_username")
            .remove("active_token")
            .apply()
    }
}

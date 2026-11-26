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

    fun getLanguage(): String = prefs.getString("app_language", "zh") ?: "zh"

    fun setLanguage(code: String) = prefs.edit().putString("app_language", code).apply()

    fun getThemeMode(): String = prefs.getString("theme_mode", "system") ?: "system"

    fun setThemeMode(mode: String) = prefs.edit().putString("theme_mode", mode).apply()

    fun getAutoSync(): Boolean = prefs.getBoolean("auto_sync", true)

    fun setAutoSync(enabled: Boolean) = prefs.edit().putBoolean("auto_sync", enabled).apply()

    fun getWifiOnlyDownload(): Boolean = prefs.getBoolean("wifi_only_download", true)

    fun setWifiOnlyDownload(enabled: Boolean) = prefs.edit().putBoolean("wifi_only_download", enabled).apply()

    fun getDefaultIntroSec(): Int = prefs.getInt("default_intro_sec", 0)
    fun setDefaultIntroSec(sec: Int) = prefs.edit().putInt("default_intro_sec", sec.coerceAtLeast(0)).apply()

    fun getDefaultOutroSec(): Int = prefs.getInt("default_outro_sec", 0)
    fun setDefaultOutroSec(sec: Int) = prefs.edit().putInt("default_outro_sec", sec.coerceAtLeast(0)).apply()

    fun getBookIntroSec(itemId: String): Int =
        prefs.getInt("book_intro_$itemId", getDefaultIntroSec())

    fun setBookIntroSec(itemId: String, sec: Int) =
        prefs.edit().putInt("book_intro_$itemId", sec.coerceAtLeast(0)).apply()

    fun getBookOutroSec(itemId: String): Int =
        prefs.getInt("book_outro_$itemId", getDefaultOutroSec())

    fun setBookOutroSec(itemId: String, sec: Int) =
        prefs.edit().putInt("book_outro_$itemId", sec.coerceAtLeast(0)).apply()
}

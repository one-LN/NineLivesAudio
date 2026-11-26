package com.ninelives.audio.data

import android.content.Context
import java.time.LocalDate

/** Daily listening seconds kept on the device, so the stats chart works offline too. */
class ListenLog(context: Context) {
    private val prefs = context.getSharedPreferences("listen_log", Context.MODE_PRIVATE)
    private var pending = 0

    fun addSecond() {
        pending += 1
        if (pending >= 30) flush()
    }

    fun flush() {
        if (pending <= 0) return
        val today = LocalDate.now().toEpochDay()
        val stored = prefs.getLong(today.toString(), 0L) + pending
        pending = 0
        val editor = prefs.edit().putLong(today.toString(), stored)
        val cutoff = LocalDate.now().minusDays(120).toEpochDay()
        prefs.all.keys.toList().forEach { key ->
            val day = key.toLongOrNull()
            if (day != null && day < cutoff) editor.remove(key)
        }
        editor.apply()
    }

    /** Returns (epochDay, seconds) for the last [days] days, newest last. */
    fun lastDays(days: Int): List<Pair<Long, Long>> {
        val today = LocalDate.now().toEpochDay()
        return (days - 1 downTo 0).map { offset ->
            val day = today - offset
            val stored = prefs.getLong(day.toString(), 0L)
            day to (stored + if (offset == 0) pending.toLong() else 0L)
        }
    }
}

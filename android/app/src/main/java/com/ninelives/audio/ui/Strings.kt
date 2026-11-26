package com.ninelives.audio.ui

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue

/** In-app language switch: "zh" (default) or "en". */
object Lang {
    var code by mutableStateOf("zh")

    val isZh: Boolean get() = code == "zh"
}

/** Picks the string for the current language. Reading [Lang.code] keeps Compose in sync. */
fun t(zh: String, en: String): String = if (Lang.code == "zh") zh else en

/** Formats "12 小时 3 分钟" style durations in the current language. */
fun durationText(hours: Long, minutes: Long): String =
    if (Lang.isZh) "${hours} 小时 ${minutes} 分钟" else "${hours}h ${minutes}m"

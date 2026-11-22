package com.ninelives.audio.data

data class ChapterInfo(
    val id: Int,
    val title: String,
    val start: Double,
    val end: Double
)

data class AudioTrackInfo(
    val index: Int,
    val startOffset: Double,
    val duration: Double,
    val streamUrl: String
)

data class PlaybackSessionData(
    val sessionId: String,
    val itemId: String,
    val title: String,
    val author: String,
    val coverUrl: String,
    val tracks: List<AudioTrackInfo>,
    val currentTime: Double,
    val duration: Double,
    val chapters: List<ChapterInfo>
)

data class SavedServer(
    val name: String = "",
    val url: String = "",
    val username: String = "",
    val token: String = "",
    val lastUsedAt: Long = System.currentTimeMillis()
) {
    val displayName: String
        get() {
            if (name.isNotBlank()) return name.trim()
            val host = url.removePrefix("https://").removePrefix("http://").substringBefore("/")
            return if (username.isBlank()) host else "$host · $username"
        }
}

data class LibraryInfo(
    val id: String,
    val name: String
)

data class BookItem(
    val id: String,
    val libraryId: String,
    val title: String,
    val author: String = "",
    val narrator: String = "",
    val duration: Double = 0.0,
    val description: String = "",
    val coverUrl: String = "",
    val progress: Double = 0.0,
    val currentTime: Double = 0.0,
    val isFinished: Boolean = false,
    val lastUpdate: Long = 0L
) {
    val progressPercent: Double
        get() = if (progress <= 1.0) progress * 100.0 else progress

    val hasProgress: Boolean
        get() = progressPercent > 0.1 || isFinished
}

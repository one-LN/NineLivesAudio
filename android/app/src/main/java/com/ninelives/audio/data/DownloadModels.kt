package com.ninelives.audio.data

data class AudioFileInfo(
    val ino: String,
    val index: Int,
    val duration: Double,
    val filename: String,
    val mimeType: String,
    val size: Long
)

data class DownloadManifest(
    val title: String,
    val author: String,
    val narrator: String,
    val description: String,
    val duration: Double,
    val coverUrl: String,
    val files: List<AudioFileInfo>,
    val chapters: List<ChapterInfo>
)

data class DownloadTask(
    val key: String,
    val itemId: String,
    val libraryId: String,
    val title: String,
    val author: String,
    val serverUrl: String,
    val username: String,
    val state: String = "queued",
    val downloadedBytes: Long = 0L,
    val totalBytes: Long = 0L,
    val currentFileIndex: Int = 0,
    val retryCount: Int = 0,
    val error: String = "",
    val manifest: DownloadManifest? = null
) {
    val progress: Int get() = if (totalBytes > 0L) ((downloadedBytes * 100L) / totalBytes).toInt().coerceIn(0, 100) else 0
}

data class DownloadedTrack(
    val index: Int,
    val fileIno: String,
    val filename: String,
    val startOffset: Double,
    val duration: Double,
    val fileUri: String
)

data class DownloadedBook(
    val serverUrl: String,
    val itemId: String,
    val libraryId: String,
    val title: String,
    val author: String,
    val narrator: String,
    val duration: Double,
    val description: String,
    val coverUri: String,
    val localCoverUri: String,
    val currentTime: Double = 0.0,
    val lastUpdate: Long = 0L,
    val tracks: List<DownloadedTrack>,
    val chapters: List<ChapterInfo>
) {
    val progress: Double get() = if (duration > 0.0) (currentTime / duration).coerceIn(0.0, 1.0) else 0.0
}

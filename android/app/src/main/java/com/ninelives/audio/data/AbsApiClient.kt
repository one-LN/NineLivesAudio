package com.ninelives.audio.data

import com.google.gson.JsonParser
import java.io.File
import java.io.RandomAccessFile
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.withContext
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONObject
import java.util.concurrent.TimeUnit

class AbsApiClient {
    private val client = OkHttpClient.Builder()
        .connectTimeout(15, TimeUnit.SECONDS)
        .readTimeout(30, TimeUnit.SECONDS)
        .build()

    private val downloadClient = OkHttpClient.Builder()
        .connectTimeout(20, TimeUnit.SECONDS)
        .readTimeout(0, TimeUnit.MILLISECONDS)
        .callTimeout(0, TimeUnit.MILLISECONDS)
        .build()

    private fun cleanUrl(url: String): String =
        url.replace(Regex("[\\s\\u200B-\\u200D\\uFEFF\"']+"), "").trimEnd('/')

    private fun segment(value: String): String =
        java.net.URLEncoder.encode(value, "UTF-8").replace("+", "%20")

    suspend fun login(serverUrl: String, username: String, password: String): Result<String> =
        withContext(Dispatchers.IO) {
            try {
                val base = cleanUrl(serverUrl)
                val bodyJson = JSONObject()
                    .put("username", username.trim())
                    .put("password", password)
                    .toString()

                val req = Request.Builder()
                    .url("$base/login")
                    .post(bodyJson.toRequestBody("application/json".toMediaType()))
                    .build()

                client.newCall(req).execute().use { resp ->
                    val text = resp.body?.string().orEmpty()
                    if (!resp.isSuccessful) {
                        return@withContext Result.failure(Exception("HTTP ${resp.code}: $text"))
                    }
                    val root = JsonParser.parseString(text).asJsonObject
                    val token = root.getAsJsonObject("user")?.get("token")?.asString
                        ?: return@withContext Result.failure(Exception("No token in response"))
                    Result.success(token)
                }
            } catch (e: Exception) {
                Result.failure(e)
            }
        }

    suspend fun getLibraries(serverUrl: String, token: String): Result<List<LibraryInfo>> =
        withContext(Dispatchers.IO) {
            try {
                val base = serverUrl.trim().trimEnd('/')
                val req = Request.Builder()
                    .url("$base/api/libraries")
                    .header("Authorization", "Bearer $token")
                    .build()

                client.newCall(req).execute().use { resp ->
                    val text = resp.body?.string().orEmpty()
                    if (!resp.isSuccessful) return@withContext Result.failure(Exception("HTTP ${resp.code}"))
                    val root = JsonParser.parseString(text).asJsonObject
                    val arr = root.getAsJsonArray("libraries") ?: return@withContext Result.success(emptyList())
                    val list = arr.mapNotNull { el ->
                        val obj = el.asJsonObject
                        val id = obj.get("id")?.asString ?: return@mapNotNull null
                        val name = obj.get("name")?.asString ?: "Library"
                        LibraryInfo(id, name)
                    }
                    Result.success(list)
                }
            } catch (e: Exception) {
                Result.failure(e)
            }
        }

    suspend fun getLibraryBooks(
        serverUrl: String,
        token: String,
        libraryId: String
    ): Result<List<BookItem>> = withContext(Dispatchers.IO) {
        try {
            val base = serverUrl.trim().trimEnd('/')
            val allBooks = mutableListOf<BookItem>()
            var page = 0
            val limit = 100

            var hasMore = true
            while (hasMore) {
                val req = Request.Builder()
                    .url("$base/api/libraries/$libraryId/items?limit=$limit&page=$page")
                    .header("Authorization", "Bearer $token")
                    .build()

                client.newCall(req).execute().use { resp ->
                    val text = resp.body?.string().orEmpty()
                    if (!resp.isSuccessful) return@withContext Result.failure(Exception("HTTP ${resp.code}"))
                    val root = JsonParser.parseString(text).asJsonObject
                    val results = root.getAsJsonArray("results")
                    if (results == null || results.size() == 0) {
                        hasMore = false
                        return@use
                    }

                    for (el in results) {
                        val item = el.asJsonObject
                        val id = item.get("id")?.takeIf { !it.isJsonNull }?.asString ?: continue
                        val media = item.getAsJsonObject("media")
                        val metadata = media?.getAsJsonObject("metadata")
                        val title = metadata?.get("title")?.takeIf { !it.isJsonNull }?.asString ?: "Untitled"
                        val author = metadata?.get("authorName")?.takeIf { !it.isJsonNull }?.asString ?: ""
                        val narrator = metadata?.get("narratorName")?.takeIf { !it.isJsonNull }?.asString ?: ""
                        val desc = metadata?.get("description")?.takeIf { !it.isJsonNull }?.asString ?: ""
                        val duration = media?.get("duration")?.takeIf { !it.isJsonNull }?.asDouble ?: 0.0

                        allBooks.add(
                            BookItem(
                                id = id,
                                libraryId = libraryId,
                                title = title,
                                author = author,
                                narrator = narrator,
                                duration = duration,
                                description = desc,
                                coverUrl = "$base/api/items/$id/cover?token=$token"
                            )
                        )
                    }

                    val total = root.get("total")?.asInt ?: allBooks.size
                    if (allBooks.size >= total || results.size() < limit) {
                        hasMore = false
                    } else {
                        page++
                    }
                }
            }

            Result.success(allBooks)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    suspend fun getDownloadManifest(
        serverUrl: String,
        token: String,
        itemId: String,
        fallbackTitle: String,
        fallbackAuthor: String,
        fallbackDescription: String
    ): Result<DownloadManifest> = withContext(Dispatchers.IO) {
        try {
            val base = cleanUrl(serverUrl)
            val request = Request.Builder()
                .url("$base/api/items/${segment(itemId)}?expanded=1")
                .header("Authorization", "Bearer $token")
                .build()
            client.newCall(request).execute().use { response ->
                if (!response.isSuccessful) return@withContext Result.failure(Exception("HTTP ${response.code}"))
                val root = JsonParser.parseString(response.body?.string().orEmpty()).asJsonObject
                val media = root.getAsJsonObject("media") ?: return@withContext Result.failure(Exception("缺少媒体信息"))
                val metadata = media.getAsJsonObject("metadata")
                val files = media.getAsJsonArray("audioFiles")
                    ?: return@withContext Result.failure(Exception("没有可下载的音频"))
                val audioFiles = files.mapNotNull { element ->
                    val file = element.asJsonObject
                    val ino = file.get("ino")?.takeIf { !it.isJsonNull }?.asString.orEmpty()
                    if (ino.isBlank()) return@mapNotNull null
                    val fm = file.getAsJsonObject("metadata")
                    AudioFileInfo(
                        ino = ino,
                        index = file.get("index")?.takeIf { !it.isJsonNull }?.asInt ?: 0,
                        duration = file.get("duration")?.takeIf { !it.isJsonNull }?.asDouble ?: 0.0,
                        filename = fm?.get("filename")?.takeIf { !it.isJsonNull }?.asString ?: "track.m4b",
                        mimeType = file.get("mimeType")?.takeIf { !it.isJsonNull }?.asString ?: "application/octet-stream",
                        size = fm?.get("size")?.takeIf { !it.isJsonNull }?.asLong ?: 0L
                    )
                }.sortedBy { it.index }
                if (audioFiles.isEmpty()) return@withContext Result.failure(Exception("未找到音频文件"))
                val chapters = media.getAsJsonArray("chapters")?.map { element ->
                    val chapter = element.asJsonObject
                    ChapterInfo(
                        id = chapter.get("id")?.takeIf { !it.isJsonNull }?.asInt ?: 0,
                        title = chapter.get("title")?.takeIf { !it.isJsonNull }?.asString ?: "章节",
                        start = chapter.get("start")?.takeIf { !it.isJsonNull }?.asDouble ?: 0.0,
                        end = chapter.get("end")?.takeIf { !it.isJsonNull }?.asDouble ?: 0.0
                    )
                } ?: emptyList()
                Result.success(
                    DownloadManifest(
                        title = metadata?.get("title")?.takeIf { !it.isJsonNull }?.asString ?: fallbackTitle,
                        author = metadata?.get("authorName")?.takeIf { !it.isJsonNull }?.asString ?: fallbackAuthor,
                        narrator = metadata?.get("narratorName")?.takeIf { !it.isJsonNull }?.asString.orEmpty(),
                        description = metadata?.get("description")?.takeIf { !it.isJsonNull }?.asString ?: fallbackDescription,
                        duration = media.get("duration")?.takeIf { !it.isJsonNull }?.asDouble ?: audioFiles.sumOf { it.duration },
                        coverUrl = "$base/api/items/${segment(itemId)}/cover",
                        files = audioFiles,
                        chapters = chapters
                    )
                )
            }
        } catch (error: Exception) { Result.failure(error) }
    }

    suspend fun downloadAudioFile(
        serverUrl: String,
        token: String,
        itemId: String,
        fileIno: String,
        destination: File,
        resumeOffset: Long,
        onProgress: (Long) -> Unit
    ): Result<Long> = withContext(Dispatchers.IO) {
        try {
            destination.parentFile?.mkdirs()
            val base = cleanUrl(serverUrl)
            val builder = Request.Builder()
                .url("$base/api/items/${segment(itemId)}/file/${segment(fileIno)}")
                .header("Authorization", "Bearer $token")
            if (resumeOffset > 0L) builder.header("Range", "bytes=$resumeOffset-")
            downloadClient.newCall(builder.build()).execute().use { response ->
                if (resumeOffset > 0L && response.code == 200) {
                    RandomAccessFile(destination, "rw").use { it.setLength(0L) }
                    return@use null
                }
                if (resumeOffset > 0L && response.code != 206) return@use Result.failure<Long>(Exception("HTTP ${response.code}"))
                if (resumeOffset == 0L && response.code != 200 && response.code != 206) return@use Result.failure<Long>(Exception("HTTP ${response.code}"))
                val body = response.body ?: return@use Result.failure<Long>(Exception("服务器返回空文件"))
                val rangeStart = response.header("Content-Range")?.substringAfter("bytes ")?.substringBefore('-')?.toLongOrNull()
                if (resumeOffset > 0L && rangeStart != resumeOffset) return@use Result.failure<Long>(Exception("断点响应位置不匹配"))
                val offset = if (response.code == 206) resumeOffset else 0L
                RandomAccessFile(destination, "rw").use { output ->
                    output.seek(offset)
                    body.byteStream().use { input ->
                        val buffer = ByteArray(64 * 1024)
                        while (true) {
                            currentCoroutineContext().ensureActive()
                            val count = input.read(buffer)
                            if (count < 0) break
                            if (count == 0) continue
                            output.write(buffer, 0, count)
                            onProgress(count.toLong())
                        }
                    }
                }
                Result.success(destination.length())
            } ?: downloadAudioFile(serverUrl, token, itemId, fileIno, destination, 0L, onProgress)
        } catch (cancelled: CancellationException) {
            throw cancelled
        } catch (error: Exception) {
            Result.failure(error)
        }
    }

    suspend fun downloadCover(serverUrl: String, token: String, itemId: String, destination: File): Boolean =
        withContext(Dispatchers.IO) {
            try {
                val request = Request.Builder()
                    .url("${cleanUrl(serverUrl)}/api/items/${segment(itemId)}/cover")
                    .header("Authorization", "Bearer $token")
                    .build()
                client.newCall(request).execute().use { response ->
                    val body = response.body ?: return@withContext false
                    if (!response.isSuccessful) return@withContext false
                    destination.parentFile?.mkdirs()
                    destination.outputStream().use { output -> body.byteStream().use { it.copyTo(output) } }
                    true
                }
            } catch (_: Exception) { false }
        }

    suspend fun getProgressMap(serverUrl: String, token: String): Map<String, Triple<Double, Double, Long>> =
        withContext(Dispatchers.IO) {
            try {
                val base = cleanUrl(serverUrl)
                val req = Request.Builder()
                    .url("$base/api/me")
                    .header("Authorization", "Bearer $token")
                    .build()

                client.newCall(req).execute().use { resp ->
                    if (!resp.isSuccessful) return@withContext emptyMap()
                    val text = resp.body?.string().orEmpty()
                    val root = JsonParser.parseString(text).asJsonObject
                    val arr = root.getAsJsonArray("mediaProgress") ?: return@withContext emptyMap()
                    val map = mutableMapOf<String, Triple<Double, Double, Long>>()
                    for (el in arr) {
                        val obj = el.asJsonObject
                        val itemId = obj.get("libraryItemId")?.asString ?: continue
                        val progress = obj.get("progress")?.asDouble ?: 0.0
                        val currentTime = obj.get("currentTime")?.asDouble ?: 0.0
                        val lastUpdate = obj.get("lastUpdate")?.asLong ?: 0L
                        map[itemId] = Triple(progress, currentTime, lastUpdate)
                    }
                    map
                }
            } catch (_: Exception) {
                emptyMap()
            }
        }

    suspend fun getListeningStats(serverUrl: String, token: String): Double =
        withContext(Dispatchers.IO) {
            try {
                val base = cleanUrl(serverUrl)
                val req = Request.Builder()
                    .url("$base/api/me/listening-stats")
                    .header("Authorization", "Bearer $token")
                    .build()
                client.newCall(req).execute().use { resp ->
                    if (!resp.isSuccessful) return@withContext 0.0
                    val text = resp.body?.string().orEmpty()
                    val root = JsonParser.parseString(text).asJsonObject
                    root.get("totalTime")?.takeIf { !it.isJsonNull }?.asDouble ?: 0.0
                }
            } catch (_: Exception) {
                0.0
            }
        }

    suspend fun startPlaybackSession(
        serverUrl: String,
        token: String,
        itemId: String
    ): Result<PlaybackSessionData> = withContext(Dispatchers.IO) {
        try {
            val base = serverUrl.trim().trimEnd('/')
            val bodyJson = JSONObject()
                .put("deviceInfo", JSONObject().put("clientName", "NineLivesAudio-Android"))
                .put("forceDirectPlay", true)
                .put("supportedMimeTypes", org.json.JSONArray(listOf("audio/mpeg", "audio/mp4", "audio/flac", "audio/ogg", "audio/aac")))
                .toString()

            val req = Request.Builder()
                .url("$base/api/items/$itemId/play")
                .header("Authorization", "Bearer $token")
                .post(bodyJson.toRequestBody("application/json".toMediaType()))
                .build()

            client.newCall(req).execute().use { resp ->
                val text = resp.body?.string().orEmpty()
                if (!resp.isSuccessful) {
                    return@withContext Result.failure(Exception("HTTP ${resp.code}: $text"))
                }
                val root = JsonParser.parseString(text).asJsonObject
                val sessionId = root.get("id")?.takeIf { !it.isJsonNull }?.asString ?: ""
                val title = root.get("displayTitle")?.asString ?: "Unknown"
                val author = root.get("displayAuthor")?.asString ?: ""
                val currentTime = root.get("currentTime")?.asDouble ?: 0.0
                val duration = root.get("duration")?.asDouble ?: 0.0

                val tracksArr = root.getAsJsonArray("audioTracks")
                val tracksList = mutableListOf<AudioTrackInfo>()
                if (tracksArr != null) {
                    var offsetAccum = 0.0
                    for (i in 0 until tracksArr.size()) {
                        val tObj = tracksArr[i].asJsonObject
                        val contentUrl = tObj.get("contentUrl")?.takeIf { !it.isJsonNull }?.asString ?: continue
                        val tUrl = if (contentUrl.startsWith("http")) contentUrl else "$base$contentUrl?token=$token"
                        val tDur = tObj.get("duration")?.takeIf { !it.isJsonNull }?.asDouble ?: 0.0
                        val tStart = tObj.get("startOffset")?.takeIf { !it.isJsonNull }?.asDouble ?: offsetAccum
                        tracksList.add(AudioTrackInfo(i, tStart, tDur, tUrl))
                        offsetAccum = tStart + tDur
                    }
                }
                if (tracksList.isEmpty()) {
                    return@withContext Result.failure(Exception("No audioTracks in play response"))
                }

                val chaptersList = mutableListOf<ChapterInfo>()
                val chaptersArr = root.getAsJsonArray("chapters")
                if (chaptersArr != null) {
                    for (el in chaptersArr) {
                        val obj = el.asJsonObject
                        chaptersList.add(
                            ChapterInfo(
                                id = obj.get("id")?.asInt ?: chaptersList.size,
                                title = obj.get("title")?.asString ?: "Chapter ${chaptersList.size + 1}",
                                start = obj.get("start")?.asDouble ?: 0.0,
                                end = obj.get("end")?.asDouble ?: 0.0
                            )
                        )
                    }
                }

                Result.success(
                    PlaybackSessionData(
                        sessionId = sessionId,
                        itemId = itemId,
                        title = title,
                        author = author,
                        coverUrl = "$base/api/items/$itemId/cover?token=$token",
                        tracks = tracksList,
                        currentTime = currentTime,
                        duration = duration,
                        chapters = chaptersList
                    )
                )
            }
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    suspend fun syncProgress(
        serverUrl: String,
        token: String,
        sessionId: String,
        itemId: String,
        currentTime: Double,
        duration: Double,
        timeListenedSec: Double = 10.0
    ) = withContext(Dispatchers.IO) {
        try {
            val base = cleanUrl(serverUrl)
            if (sessionId.isNotBlank()) {
                val sessJson = JSONObject()
                    .put("currentTime", currentTime)
                    .put("duration", duration)
                    .put("timeListened", timeListenedSec.coerceAtLeast(0.0))
                    .toString()
                val sessReq = Request.Builder()
                    .url("$base/api/session/$sessionId/sync")
                    .header("Authorization", "Bearer $token")
                    .post(sessJson.toRequestBody("application/json".toMediaType()))
                    .build()
                client.newCall(sessReq).execute().close()
            }
            val progress = if (duration > 0) (currentTime / duration).coerceIn(0.0, 1.0) else 0.0
            val bodyJson = JSONObject()
                .put("currentTime", currentTime)
                .put("duration", duration)
                .put("progress", progress)
                .put("isFinished", progress >= 0.995)
                .toString()

            val req = Request.Builder()
                .url("$base/api/me/progress/$itemId")
                .header("Authorization", "Bearer $token")
                .patch(bodyJson.toRequestBody("application/json".toMediaType()))
                .build()

            client.newCall(req).execute().close()
        } catch (_: Exception) {
        }
    }
}

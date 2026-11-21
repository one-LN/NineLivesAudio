package com.ninelives.audio.data

import com.google.gson.JsonParser
import kotlinx.coroutines.Dispatchers
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

    private fun cleanUrl(url: String): String =
        url.replace(Regex("[\\s\\u200B-\\u200D\\uFEFF\"']+"), "").trimEnd('/')

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
        itemId: String,
        currentTime: Double,
        duration: Double
    ) = withContext(Dispatchers.IO) {
        try {
            val base = serverUrl.trim().trimEnd('/')
            val progress = if (duration > 0) (currentTime / duration).coerceIn(0.0, 1.0) else 0.0
            val bodyJson = JSONObject()
                .put("currentTime", currentTime)
                .put("duration", duration)
                .put("progress", progress)
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

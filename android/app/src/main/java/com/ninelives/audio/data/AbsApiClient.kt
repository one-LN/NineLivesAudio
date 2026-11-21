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

    suspend fun login(serverUrl: String, username: String, password: String): Result<String> =
        withContext(Dispatchers.IO) {
            try {
                val base = serverUrl.trim().trimEnd('/')
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
                        val id = item.get("id")?.asString ?: continue
                        val media = item.getAsJsonObject("media")
                        val metadata = media?.getAsJsonObject("metadata")
                        val title = metadata?.get("title")?.asString ?: "Untitled"
                        val author = metadata?.get("authorName")?.asString ?: ""
                        val narrator = metadata?.get("narratorName")?.asString ?: ""
                        val desc = metadata?.get("description")?.asString ?: ""
                        val duration = media?.get("duration")?.asDouble ?: 0.0

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
                val base = serverUrl.trim().trimEnd('/')
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
}

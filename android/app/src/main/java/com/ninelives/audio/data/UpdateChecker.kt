package com.ninelives.audio.data

import android.content.Context
import android.content.Intent
import android.net.Uri
import androidx.core.content.FileProvider
import com.google.gson.JsonParser
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import okhttp3.Request
import java.io.File
import java.util.concurrent.TimeUnit
import com.ninelives.audio.ui.t

data class AppUpdate(
    val versionName: String,
    val downloadUrl: String,
    val notes: String = ""
)

/**
 * Reads the public GitHub releases of this repository, so users can update the
 * Android build without visiting a browser.
 */
class UpdateChecker(private val context: Context) {
    private companion object {
        const val CACHE_TTL_MS = 10 * 60 * 1000L
    }

    private val client = OkHttpClient.Builder()
        .connectTimeout(20, TimeUnit.SECONDS)
        .readTimeout(60, TimeUnit.SECONDS)
        .build()

    fun currentVersionName(): String = try {
        context.packageManager.getPackageInfo(context.packageName, 0).versionName ?: "0"
    } catch (_: Exception) {
        "0"
    }

    private val prefs by lazy {
        context.getSharedPreferences("update_cache", Context.MODE_PRIVATE)
    }

    /**
     * GitHub serves at most 60 unauthenticated API calls per hour per IP, and a
     * phone shares that IP with every other device on the network. Cache the
     * release list so normal use never runs into a 403.
     */
    suspend fun check(force: Boolean = false): Result<AppUpdate?> = withContext(Dispatchers.IO) {
        // The REST API allows only 60 anonymous calls per hour per IP, which a phone
        // shares with every other device behind the same router. The releases Atom
        // feed is not rate limited, so use it first and keep the API as a fallback.
        val atom = runCatching { fetchAtom() }.getOrNull()
        if (atom != null) return@withContext atom

        val cachedAt = prefs.getLong("at", 0L)
        val cachedBody = prefs.getString("body", null)
        val fresh = System.currentTimeMillis() - cachedAt < CACHE_TTL_MS
        if (!force && fresh && !cachedBody.isNullOrBlank()) {
            return@withContext runCatching { Result.success(pickLatest(cachedBody)) }
                .getOrElse { Result.success(null) }
        }
        try {
            val request = Request.Builder()
                .url("https://api.github.com/repos/one-LN/NineLivesAudio/releases?per_page=30")
                .header("Accept", "application/vnd.github+json")
                .header("User-Agent", "NineLivesAudio-Android/" + currentVersionName())
                .build()
            client.newCall(request).execute().use { response ->
                if (!response.isSuccessful) {
                    // Rate limited: fall back to the last successful answer instead of
                    // telling the user "HTTP 403" with no explanation.
                    if ((response.code == 403 || response.code == 429) && !cachedBody.isNullOrBlank()) {
                        return@withContext runCatching { Result.success(pickLatest(cachedBody)) }
                            .getOrElse { Result.failure(Exception(t(
                                "GitHub 接口限流，请稍后再试",
                                "GitHub rate limit reached, try again later"
                            ))) }
                    }
                    val hint = if (response.code == 403 || response.code == 429) {
                        t("GitHub 接口限流（每小时 60 次），请稍后再试",
                          "GitHub rate limit (60/hour), try again later")
                    } else {
                        "HTTP ${response.code}"
                    }
                    return@withContext Result.failure(Exception(hint))
                }
                val body = response.body?.string().orEmpty()
                prefs.edit().putString("body", body).putLong("at", System.currentTimeMillis()).apply()
                Result.success(pickLatest(body))
            }
        } catch (error: Exception) {
            Result.failure(error)
        }
    }

    private fun pickLatest(body: String): AppUpdate? {
        val releases = JsonParser.parseString(body).asJsonArray
                val current = currentVersionName()
                var bestVersion = current
                var best: AppUpdate? = null
                for (element in releases) {
                    val obj = element.takeIf { it.isJsonObject }?.asJsonObject ?: continue
                    if (obj.bool("draft") == true) continue
                    val tag = obj.str("tag_name") ?: continue
                    if (!tag.startsWith("android-v")) continue
                    val version = tag.removePrefix("android-v")
                    val assets = obj.get("assets")?.takeIf { it.isJsonArray }?.asJsonArray ?: continue
                    val asset = assets.firstOrNull {
                        it.takeIf { it.isJsonObject }?.asJsonObject?.str("name") == "NineLivesAudio-android.apk"
                    }?.asJsonObject ?: continue
                    val downloadUrl = asset.str("browser_download_url") ?: continue
                    if (compareVersions(version, bestVersion) > 0) {
                        bestVersion = version
                        best = AppUpdate(
                            versionName = version,
                            downloadUrl = downloadUrl,
                            notes = obj.str("body").orEmpty()
                        )
                    }
                }
        return best
    }

    /** Reads the releases Atom feed: no quota, one small request. */
    private fun fetchAtom(): Result<AppUpdate?> {
        val cachedAt = prefs.getLong("atom_at", 0L)
        val cached = prefs.getString("atom_body", null)
        val fresh = System.currentTimeMillis() - cachedAt < CACHE_TTL_MS
        val body = if (fresh && !cached.isNullOrBlank()) cached else {
            val request = Request.Builder()
                .url("https://github.com/one-LN/NineLivesAudio/releases.atom")
                .header("User-Agent", "NineLivesAudio-Android/" + currentVersionName())
                .build()
            val fetched = client.newCall(request).execute().use { response ->
                if (!response.isSuccessful) return Result.failure(Exception("HTTP ${response.code}"))
                response.body?.string().orEmpty()
            }
            prefs.edit().putString("atom_body", fetched).putLong("atom_at", System.currentTimeMillis()).apply()
            fetched
        }

        val current = currentVersionName()
        var bestVersion = current
        var bestTag: String? = null
        for (m in Regex("""/releases/tag/(android-v[0-9][^"<\s]*)""").findAll(body)) {
            val tag = m.groupValues[1]
            val version = tag.removePrefix("android-v")
            if (compareVersions(version, bestVersion) > 0) {
                bestVersion = version
                bestTag = tag
            }
        }
        val tag = bestTag ?: return Result.success(null)
        val version = tag.removePrefix("android-v")
        return Result.success(
            AppUpdate(
                versionName = version,
                downloadUrl = "https://github.com/one-LN/NineLivesAudio/releases/download/$tag/NineLivesAudio-android.apk",
                notes = ""
            )
        )
    }

    suspend fun download(update: AppUpdate, onProgress: (Int) -> Unit): Result<File> =
        withContext(Dispatchers.IO) {
            try {
                val dir = File(context.cacheDir, "updates").apply { mkdirs() }
                dir.listFiles()?.forEach { it.delete() }
                val target = File(dir, "NineLivesAudio-${update.versionName}.apk")
                val request = Request.Builder().url(update.downloadUrl).build()
                client.newCall(request).execute().use { response ->
                    if (!response.isSuccessful) {
                        return@withContext Result.failure(Exception("HTTP ${response.code}"))
                    }
                    val body = response.body ?: return@withContext Result.failure(Exception(t("下载内容为空", "Empty download")))
                    val total = body.contentLength()
                    var read = 0L
                    body.byteStream().use { input ->
                        target.outputStream().use { output ->
                            val buffer = ByteArray(64 * 1024)
                            while (true) {
                                val count = input.read(buffer)
                                if (count < 0) break
                                output.write(buffer, 0, count)
                                read += count
                                if (total > 0L) {
                                    onProgress(((read * 100) / total).toInt().coerceIn(0, 100))
                                }
                            }
                        }
                    }
                }
                onProgress(100)
                Result.success(target)
            } catch (error: Exception) {
                Result.failure(error)
            }
        }

    /** Fallback when the API check cannot run: open the releases page in a browser. */
    fun openReleasesPage() {
        val intent = Intent(
            Intent.ACTION_VIEW,
            Uri.parse("https://github.com/one-LN/NineLivesAudio/releases")
        ).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        runCatching { context.startActivity(intent) }
    }

    private fun com.google.gson.JsonObject.str(key: String): String? =
        get(key)?.takeIf { !it.isJsonNull }?.let { runCatching { it.asString }.getOrNull() }

    private fun com.google.gson.JsonObject.bool(key: String): Boolean? =
        get(key)?.takeIf { !it.isJsonNull }?.let { runCatching { it.asBoolean }.getOrNull() }

    fun canInstall(): Boolean = context.packageManager.canRequestPackageInstalls()

    fun openInstallPermission() {
        val intent = Intent(
            android.provider.Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,
            Uri.parse("package:${context.packageName}")
        ).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        runCatching { context.startActivity(intent) }
    }

    fun install(file: File) {
        val uri = FileProvider.getUriForFile(context, "${context.packageName}.fileprovider", file)
        val intent = Intent(Intent.ACTION_VIEW).apply {
            setDataAndType(uri, "application/vnd.android.package-archive")
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_ACTIVITY_NEW_TASK)
        }
        context.startActivity(intent)
    }

    private fun compareVersions(left: String, right: String): Int {
        val a = left.split('.').map { it.toIntOrNull() ?: 0 }
        val b = right.split('.').map { it.toIntOrNull() ?: 0 }
        for (index in 0 until maxOf(a.size, b.size)) {
            val x = a.getOrElse(index) { 0 }
            val y = b.getOrElse(index) { 0 }
            if (x != y) return x - y
        }
        return 0
    }
}

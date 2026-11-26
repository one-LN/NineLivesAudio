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
    private val client = OkHttpClient.Builder()
        .connectTimeout(20, TimeUnit.SECONDS)
        .readTimeout(60, TimeUnit.SECONDS)
        .build()

    fun currentVersionName(): String = try {
        context.packageManager.getPackageInfo(context.packageName, 0).versionName ?: "0"
    } catch (_: Exception) {
        "0"
    }

    suspend fun check(): Result<AppUpdate?> = withContext(Dispatchers.IO) {
        try {
            val request = Request.Builder()
                .url("https://api.github.com/repos/one-LN/NineLivesAudio/releases?per_page=30")
                .header("Accept", "application/vnd.github+json")
                .build()
            client.newCall(request).execute().use { response ->
                if (!response.isSuccessful) {
                    return@withContext Result.failure(Exception("HTTP ${response.code}"))
                }
                val body = response.body?.string().orEmpty()
                val releases = JsonParser.parseString(body).asJsonArray
                val current = currentVersionName()
                var bestVersion = current
                var best: AppUpdate? = null
                for (element in releases) {
                    val obj = element.asJsonObject
                    if (obj.get("draft")?.asBoolean == true) continue
                    val tag = obj.get("tag_name")?.asString ?: continue
                    if (!tag.startsWith("android-v")) continue
                    val version = tag.removePrefix("android-v")
                    val assets = obj.getAsJsonArray("assets") ?: continue
                    val asset = assets.firstOrNull {
                        it.asJsonObject.get("name")?.asString == "NineLivesAudio-android.apk"
                    }?.asJsonObject ?: continue
                    if (compareVersions(version, bestVersion) > 0) {
                        bestVersion = version
                        best = AppUpdate(
                            versionName = version,
                            downloadUrl = asset.get("browser_download_url").asString,
                            notes = obj.get("body")?.asString.orEmpty()
                        )
                    }
                }
                Result.success(best)
            }
        } catch (error: Exception) {
            Result.failure(error)
        }
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

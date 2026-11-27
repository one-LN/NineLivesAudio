package com.ninelives.audio.data

import android.content.Context
import android.content.Intent
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RectF
import android.net.Uri
import androidx.core.content.pm.ShortcutInfoCompat
import androidx.core.content.pm.ShortcutManagerCompat
import androidx.core.graphics.drawable.IconCompat
import java.io.File
import java.io.FileOutputStream

/**
 * Turns a picture the user picked into the icon that opens the app.
 *
 * Android does not let an app swap its own launcher icon for an arbitrary
 * bitmap at runtime (the icon is compiled into the APK). The supported route
 * is a pinned launcher entry that carries the picture and launches
 * MainActivity. It reuses one shortcut id, so picking another picture updates
 * the same entry instead of adding a second icon.
 */
object CustomIcon {
    const val SHORTCUT_ID = "custom-icon"
    private const val SIZE = 432

    /** All generated icons; only the newest one is kept. */
    private fun candidates(context: Context): List<File> =
        context.filesDir.listFiles { f -> f.name.startsWith("custom_icon_") }?.toList() ?: emptyList()

    /** Newest stored picture, or null when the user never picked one. */
    fun current(context: Context): File? =
        candidates(context).maxByOrNull { it.lastModified() }

    /** Decodes [uri], centre-crops it square and stores a rounded PNG. */
    fun save(context: Context, uri: Uri): File? = runCatching {
        val resolver = context.contentResolver
        val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        resolver.openInputStream(uri)?.use { BitmapFactory.decodeStream(it, null, bounds) }
        if (bounds.outWidth <= 0 || bounds.outHeight <= 0) return null

        val sample = maxOf(1, minOf(bounds.outWidth, bounds.outHeight) / SIZE)
        val opts = BitmapFactory.Options().apply { inSampleSize = sample }
        val decoded = resolver.openInputStream(uri)?.use {
            BitmapFactory.decodeStream(it, null, opts)
        } ?: return null

        val side = minOf(decoded.width, decoded.height)
        val square = Bitmap.createBitmap(
            decoded, (decoded.width - side) / 2, (decoded.height - side) / 2, side, side
        )
        val scaled = Bitmap.createScaledBitmap(square, SIZE, SIZE, true)

        val out = Bitmap.createBitmap(SIZE, SIZE, Bitmap.Config.ARGB_8888)
        val canvas = Canvas(out)
        val path = Path().apply {
            addRoundRect(
                RectF(0f, 0f, SIZE.toFloat(), SIZE.toFloat()),
                SIZE * 0.22f,
                SIZE * 0.22f,
                Path.Direction.CW
            )
        }
        canvas.save()
        canvas.clipPath(path)
        canvas.drawBitmap(scaled, 0f, 0f, Paint(Paint.ANTI_ALIAS_FLAG or Paint.FILTER_BITMAP_FLAG))
        canvas.restore()

        // A fresh name keeps image caches honest; older files are removed.
        candidates(context).forEach { it.delete() }
        val target = File(context.filesDir, "custom_icon_${System.currentTimeMillis()}.png")
        FileOutputStream(target).use { out.compress(Bitmap.CompressFormat.PNG, 100, it) }
        target
    }.getOrNull()

    private fun shortcut(context: Context, icon: Bitmap): ShortcutInfoCompat {
        val launch = Intent(context, Class.forName("com.ninelives.audio.MainActivity"))
            .setAction(Intent.ACTION_MAIN)
            .addCategory(Intent.CATEGORY_LAUNCHER)
        return ShortcutInfoCompat.Builder(context, SHORTCUT_ID)
            .setShortLabel("九命")
            .setLongLabel("九命 · NineLivesAudio")
            .setIcon(IconCompat.createWithBitmap(icon))
            .setIntent(launch)
            .build()
    }

    private fun isPinned(context: Context): Boolean = runCatching {
        ShortcutManagerCompat.getShortcuts(context, ShortcutManagerCompat.FLAG_MATCH_PINNED)
            .any { it.id == SHORTCUT_ID }
    }.getOrDefault(false)

    /**
     * Puts the picture on the home screen. When the entry already exists it is
     * updated in place, so the user never ends up with two icons.
     */
    fun apply(context: Context): Boolean = runCatching {
        val icon = BitmapFactory.decodeFile(current(context)?.absolutePath ?: return false)
            ?: return false
        val info = shortcut(context, icon)
        if (isPinned(context)) {
            ShortcutManagerCompat.updateShortcuts(context, listOf(info))
            true
        } else {
            ShortcutManagerCompat.requestPinShortcut(context, info, null)
        }
    }.getOrDefault(false)

    /** Removes the generated pictures and the launcher entry that points at them. */
    fun clear(context: Context): Boolean = runCatching {
        candidates(context).forEach { it.delete() }
        ShortcutManagerCompat.removeLongLivedShortcuts(context, listOf(SHORTCUT_ID))
        ShortcutManagerCompat.disableShortcuts(context, listOf(SHORTCUT_ID))
        true
    }.getOrDefault(false)
}

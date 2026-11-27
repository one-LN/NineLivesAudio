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
 * Lets the user turn any picture into a launcher icon.
 *
 * Android only allows an app to change its own icon at build time, so the
 * supported route is a pinned launcher shortcut that carries the picture and
 * opens the app. The picture is cropped square, rounded and written to
 * filesDir so the shortcut keeps working after a reboot.
 */
object CustomIcon {
    const val SHORTCUT_ID = "custom-icon"
    private const val SIZE = 432

    fun file(context: Context): File = File(context.filesDir, "custom_icon.png")

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
        val left = (decoded.width - side) / 2
        val top = (decoded.height - side) / 2
        val square = Bitmap.createBitmap(decoded, left, top, side, side)
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

        val target = file(context)
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

    /** True when the launcher supports adding shortcuts without a long-press. */
    fun canPin(context: Context): Boolean =
        ShortcutManagerCompat.isRequestPinShortcutSupported(context)

    /** Asks the launcher to place the custom icon on the home screen. */
    fun requestPin(context: Context): Boolean = runCatching {
        val icon = BitmapFactory.decodeFile(file(context).absolutePath) ?: return false
        ShortcutManagerCompat.requestPinShortcut(context, shortcut(context, icon), null)
    }.getOrDefault(false)

    fun remove(context: Context): Boolean = runCatching {
        file(context).delete()
    }.getOrDefault(false)
}

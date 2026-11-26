package com.ninelives.audio.ui

import androidx.compose.foundation.Canvas
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.unit.dp

/** Player action icons, redrawn to match the desktop Fluent glyph set. */
@Composable
fun PlayerGlyph(kind: String, tint: Color, modifier: Modifier = Modifier) {
    Canvas(modifier = modifier) {
        val stroke = Stroke(width = 1.7.dp.toPx(), cap = StrokeCap.Round, join = StrokeJoin.Round)
        when (kind) {
            "speed" -> drawSpeed(tint, stroke)
            "sleep" -> drawSleep(tint, stroke)
            "volume" -> drawVolume(tint, stroke)
            "cut" -> drawCut(tint, stroke)
            "chapters" -> drawChapters(tint, stroke)
            else -> drawBookmark(tint, stroke)
        }
    }
}

private fun DrawScope.drawSpeed(tint: Color, stroke: Stroke) {
    val w = size.width
    val h = size.height
    val cx = w * 0.5f
    val cy = h * 0.54f
    drawCircle(tint, radius = w * 0.34f, center = Offset(cx, cy), style = stroke)
    drawLine(tint, Offset(cx, cy), Offset(w * 0.70f, h * 0.32f), stroke.width, StrokeCap.Round)
    drawCircle(tint, radius = w * 0.05f, center = Offset(cx, cy))
}

private fun DrawScope.drawSleep(tint: Color, stroke: Stroke) {
    val w = size.width
    val h = size.height
    val cx = w * 0.5f
    val cy = h * 0.60f
    drawCircle(tint, radius = w * 0.30f, center = Offset(cx, cy), style = stroke)
    drawLine(tint, Offset(cx, h * 0.12f), Offset(cx, h * 0.26f), stroke.width, StrokeCap.Round)
    drawLine(tint, Offset(w * 0.40f, h * 0.10f), Offset(w * 0.60f, h * 0.10f), stroke.width, StrokeCap.Round)
    drawLine(tint, Offset(cx, cy), Offset(cx, h * 0.42f), stroke.width, StrokeCap.Round)
    drawLine(tint, Offset(cx, cy), Offset(w * 0.62f, h * 0.68f), stroke.width, StrokeCap.Round)
}

private fun DrawScope.drawVolume(tint: Color, stroke: Stroke) {
    val w = size.width
    val h = size.height
    val speaker = Path().apply {
        moveTo(w * 0.14f, h * 0.40f)
        lineTo(w * 0.28f, h * 0.40f)
        lineTo(w * 0.42f, h * 0.24f)
        lineTo(w * 0.42f, h * 0.76f)
        lineTo(w * 0.28f, h * 0.60f)
        lineTo(w * 0.14f, h * 0.60f)
        close()
    }
    drawPath(speaker, tint, style = stroke)
    drawArc(
        color = tint,
        startAngle = -50f,
        sweepAngle = 100f,
        useCenter = false,
        topLeft = Offset(w * 0.34f, h * 0.30f),
        size = Size(w * 0.28f, h * 0.40f),
        style = stroke
    )
    drawArc(
        color = tint,
        startAngle = -50f,
        sweepAngle = 100f,
        useCenter = false,
        topLeft = Offset(w * 0.34f, h * 0.18f),
        size = Size(w * 0.48f, h * 0.64f),
        style = stroke
    )
}

private fun DrawScope.drawCut(tint: Color, stroke: Stroke) {
    val w = size.width
    val h = size.height
    drawLine(tint, Offset(w * 0.24f, h * 0.14f), Offset(w * 0.72f, h * 0.68f), stroke.width, StrokeCap.Round)
    drawLine(tint, Offset(w * 0.76f, h * 0.14f), Offset(w * 0.28f, h * 0.68f), stroke.width, StrokeCap.Round)
    drawCircle(tint, radius = w * 0.11f, center = Offset(w * 0.26f, h * 0.78f), style = stroke)
    drawCircle(tint, radius = w * 0.11f, center = Offset(w * 0.74f, h * 0.78f), style = stroke)
}

private fun DrawScope.drawChapters(tint: Color, stroke: Stroke) {
    val w = size.width
    val h = size.height
    listOf(0.26f, 0.50f, 0.74f).forEach { y ->
        drawCircle(tint, radius = w * 0.045f, center = Offset(w * 0.16f, h * y))
        drawLine(tint, Offset(w * 0.32f, h * y), Offset(w * 0.88f, h * y), stroke.width, StrokeCap.Round)
    }
}

private fun DrawScope.drawBookmark(tint: Color, stroke: Stroke) {
    val w = size.width
    val h = size.height
    val ribbon = Path().apply {
        moveTo(w * 0.30f, h * 0.14f)
        lineTo(w * 0.70f, h * 0.14f)
        lineTo(w * 0.70f, h * 0.86f)
        lineTo(w * 0.50f, h * 0.70f)
        lineTo(w * 0.30f, h * 0.86f)
        close()
    }
    drawPath(ribbon, tint, style = stroke)
}

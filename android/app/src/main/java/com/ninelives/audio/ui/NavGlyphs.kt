package com.ninelives.audio.ui

import androidx.compose.foundation.Canvas
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.drawscope.rotate
import androidx.compose.ui.unit.dp

/**
 * Bottom-navigation glyphs drawn in the same thin, geometric style the desktop
 * client uses for its sidebar (Fluent outline icons), so both apps look alike.
 */
@Composable
fun NavGlyph(index: Int, tint: Color, modifier: Modifier = Modifier) {
    Canvas(modifier = modifier) {
        val stroke = Stroke(width = 1.6.dp.toPx(), cap = StrokeCap.Round, join = StrokeJoin.Round)
        when (index) {
            0 -> drawCatEye(tint, stroke)
            1 -> drawBooks(tint, stroke)
            2 -> drawPlay(tint, stroke)
            3 -> drawDownload(tint, stroke)
            4 -> drawChart(tint, stroke)
            else -> drawGear(tint, stroke)
        }
    }
}

private fun DrawScope.drawCatEye(tint: Color, stroke: Stroke) {
    val w = size.width
    val h = size.height
    // almond eye
    val eye = Path().apply {
        moveTo(w * 0.10f, h * 0.54f)
        cubicTo(w * 0.30f, h * 0.24f, w * 0.70f, h * 0.24f, w * 0.90f, h * 0.54f)
        cubicTo(w * 0.70f, h * 0.80f, w * 0.30f, h * 0.80f, w * 0.10f, h * 0.54f)
        close()
    }
    drawPath(eye, tint, style = stroke)
    drawCircle(tint, radius = w * 0.13f, center = Offset(w * 0.5f, h * 0.53f), style = stroke)
    drawCircle(tint, radius = w * 0.05f, center = Offset(w * 0.5f, h * 0.53f))
    // ears
    drawLine(tint, Offset(w * 0.28f, h * 0.30f), Offset(w * 0.34f, h * 0.12f), stroke.width, StrokeCap.Round)
    drawLine(tint, Offset(w * 0.34f, h * 0.12f), Offset(w * 0.44f, h * 0.26f), stroke.width, StrokeCap.Round)
    drawLine(tint, Offset(w * 0.72f, h * 0.30f), Offset(w * 0.66f, h * 0.12f), stroke.width, StrokeCap.Round)
    drawLine(tint, Offset(w * 0.66f, h * 0.12f), Offset(w * 0.56f, h * 0.26f), stroke.width, StrokeCap.Round)
}

private fun DrawScope.drawBooks(tint: Color, stroke: Stroke) {
    val w = size.width
    val h = size.height
    val radius = CornerRadius(1.5.dp.toPx())
    drawRoundRect(
        tint,
        topLeft = Offset(w * 0.06f, h * 0.14f),
        size = Size(w * 0.18f, h * 0.72f),
        cornerRadius = radius,
        style = stroke
    )
    drawRoundRect(
        tint,
        topLeft = Offset(w * 0.34f, h * 0.14f),
        size = Size(w * 0.18f, h * 0.72f),
        cornerRadius = radius,
        style = stroke
    )
    rotate(-16f, pivot = Offset(w * 0.82f, h * 0.86f)) {
        drawRoundRect(
            tint,
            topLeft = Offset(w * 0.72f, h * 0.24f),
            size = Size(w * 0.18f, h * 0.62f),
            cornerRadius = radius,
            style = stroke
        )
    }
}

private fun DrawScope.drawPlay(tint: Color, stroke: Stroke) {
    val w = size.width
    val h = size.height
    val path = Path().apply {
        moveTo(w * 0.24f, h * 0.14f)
        lineTo(w * 0.86f, h * 0.50f)
        lineTo(w * 0.24f, h * 0.86f)
        close()
    }
    drawPath(path, tint, style = stroke)
}

private fun DrawScope.drawDownload(tint: Color, stroke: Stroke) {
    val w = size.width
    val h = size.height
    drawLine(tint, Offset(w * 0.50f, h * 0.10f), Offset(w * 0.50f, h * 0.62f), stroke.width, StrokeCap.Round)
    drawLine(tint, Offset(w * 0.30f, h * 0.44f), Offset(w * 0.50f, h * 0.64f), stroke.width, StrokeCap.Round)
    drawLine(tint, Offset(w * 0.70f, h * 0.44f), Offset(w * 0.50f, h * 0.64f), stroke.width, StrokeCap.Round)
    drawLine(tint, Offset(w * 0.18f, h * 0.86f), Offset(w * 0.82f, h * 0.86f), stroke.width, StrokeCap.Round)
}

private fun DrawScope.drawChart(tint: Color, stroke: Stroke) {
    val w = size.width
    val h = size.height
    drawLine(tint, Offset(w * 0.10f, h * 0.88f), Offset(w * 0.90f, h * 0.88f), stroke.width, StrokeCap.Round)
    drawRoundRect(
        tint,
        topLeft = Offset(w * 0.16f, h * 0.46f),
        size = Size(w * 0.16f, h * 0.36f),
        cornerRadius = CornerRadius(1.5.dp.toPx()),
        style = stroke
    )
    drawRoundRect(
        tint,
        topLeft = Offset(w * 0.42f, h * 0.22f),
        size = Size(w * 0.16f, h * 0.60f),
        cornerRadius = CornerRadius(1.5.dp.toPx()),
        style = stroke
    )
    drawRoundRect(
        tint,
        topLeft = Offset(w * 0.68f, h * 0.36f),
        size = Size(w * 0.16f, h * 0.46f),
        cornerRadius = CornerRadius(1.5.dp.toPx()),
        style = stroke
    )
}

private fun DrawScope.drawGear(tint: Color, stroke: Stroke) {
    val w = size.width
    val h = size.height
    val cx = w / 2f
    val cy = h / 2f
    drawCircle(tint, radius = w * 0.30f, center = Offset(cx, cy), style = stroke)
    drawCircle(tint, radius = w * 0.11f, center = Offset(cx, cy), style = stroke)
    repeat(8) { i ->
        rotate(i * 45f, pivot = Offset(cx, cy)) {
            drawLine(
                tint,
                Offset(cx, cy - w * 0.30f),
                Offset(cx, cy - w * 0.44f),
                stroke.width,
                StrokeCap.Round
            )
        }
    }
}

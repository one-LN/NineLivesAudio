package com.ninelives.audio.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectHorizontalDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.ContentCut
import androidx.compose.material.icons.outlined.FormatListBulleted
import androidx.compose.material.icons.outlined.PlayCircle
import androidx.compose.material.icons.outlined.SkipNext
import androidx.compose.material.icons.outlined.SkipPrevious
import androidx.compose.material.icons.outlined.Timer
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.scale
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import coil.compose.AsyncImage

@Composable
fun PlayerScreen(vm: MainViewModel) {
    val sess = vm.currentSession
    if (sess == null) {
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(24.dp),
            verticalArrangement = Arrangement.Center,
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            Text("尚未选择有声书", style = MaterialTheme.typography.titleMedium)
            Spacer(modifier = Modifier.height(8.dp))
            Text(
                "请在「九命」或「书库」中点击任意一本书开始收听。",
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
            vm.playerError?.let { err ->
                Spacer(modifier = Modifier.height(12.dp))
                Text(err, color = MaterialTheme.colorScheme.error)
            }
        }
        return
    }

    val currentChapterIdx = sess.chapters.indexOfLast { vm.currentPositionSec >= it.start }
    val currentChapter = sess.chapters.getOrNull(currentChapterIdx)
    val trackIdx = sess.tracks.indexOfLast { vm.currentPositionSec >= it.startOffset }.coerceAtLeast(0)

    Column(
        modifier = Modifier
            .fillMaxSize()
            .padding(horizontal = 20.dp, vertical = 12.dp),
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        Surface(
            shape = RoundedCornerShape(18.dp),
            tonalElevation = 4.dp,
            modifier = Modifier
                .weight(1f)
                .fillMaxWidth(0.94f)
        ) {
            AsyncImage(
                model = sess.coverUrl,
                contentDescription = sess.title,
                contentScale = ContentScale.Crop,
                modifier = Modifier
                    .fillMaxSize()
                    .padding(4.dp)
                    .clip(RoundedCornerShape(14.dp))
            )
        }
        Spacer(modifier = Modifier.height(14.dp))
        Text(
            text = sess.title,
            style = MaterialTheme.typography.titleMedium,
            fontWeight = FontWeight.Bold,
            textAlign = TextAlign.Center,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis
        )
        Spacer(modifier = Modifier.height(4.dp))
        Text(
            text = "第 ${trackIdx + 1} 轨 / 共 ${sess.tracks.size} 轨",
            style = MaterialTheme.typography.labelSmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant
        )
        if (currentChapter != null) {
            Text(
                text = currentChapter.title,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.primary,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis
            )
            Text(
                text = "开始 ${formatTime(currentChapter.start)} · 结束 ${formatTime(currentChapter.end)}",
                style = MaterialTheme.typography.labelSmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
        }

        Spacer(modifier = Modifier.height(8.dp))
        var showChapterMode by remember { mutableStateOf(true) }
        val hasChapter = currentChapter != null && currentChapter.end > currentChapter.start
        val displayStart = if (showChapterMode && hasChapter) currentChapter!!.start else 0.0
        val displayDuration = if (showChapterMode && hasChapter) {
            (currentChapter!!.end - currentChapter!!.start).coerceAtLeast(1.0)
        } else {
            vm.totalDurationSec.coerceAtLeast(1.0)
        }
        val displayPos = (vm.currentPositionSec - displayStart).coerceIn(0.0, displayDuration)
        val remaining = (displayDuration - displayPos).coerceAtLeast(0.0)
        var draggingRatio by remember { mutableStateOf<Float?>(null) }
        val sliderVal = draggingRatio ?: (displayPos / displayDuration).toFloat().coerceIn(0f, 1f)

        Box(
            contentAlignment = Alignment.Center,
            modifier = Modifier
                .fillMaxWidth()
                .height(26.dp)
                .pointerInput(displayStart, displayDuration) {
                    detectTapGestures { offset ->
                        val r = (offset.x / size.width).coerceIn(0f, 1f)
                        vm.seekToSec(displayStart + r * displayDuration)
                    }
                }
                .pointerInput(displayStart, displayDuration) {
                    detectHorizontalDragGestures(
                        onDragStart = { offset ->
                            draggingRatio = (offset.x / size.width).coerceIn(0f, 1f)
                        },
                        onHorizontalDrag = { change, _ ->
                            change.consume()
                            draggingRatio = (change.position.x / size.width).coerceIn(0f, 1f)
                        },
                        onDragEnd = {
                            draggingRatio?.let { r -> vm.seekToSec(displayStart + r * displayDuration) }
                            draggingRatio = null
                        },
                        onDragCancel = { draggingRatio = null }
                    )
                }
        ) {
            Canvas(modifier = Modifier.fillMaxSize()) {
                val trackH = 4.dp.toPx()
                val cy = size.height / 2f
                val trackTop = cy - trackH / 2f
                val activeW = size.width * sliderVal
                drawRoundRect(
                    color = Color(0xFF3A3F4B),
                    topLeft = Offset(0f, trackTop),
                    size = Size(size.width, trackH),
                    cornerRadius = CornerRadius(trackH / 2f)
                )
                if (activeW > 0f) {
                    drawRoundRect(
                        color = Color(0xFF4FC3F7),
                        topLeft = Offset(0f, trackTop),
                        size = Size(activeW, trackH),
                        cornerRadius = CornerRadius(trackH / 2f)
                    )
                }
                drawCircle(
                    color = Color(0xFF232730),
                    radius = 7.dp.toPx(),
                    center = Offset(activeW.coerceIn(7.dp.toPx(), size.width - 7.dp.toPx()), cy)
                )
                drawCircle(
                    color = Color(0xFF4FC3F7),
                    radius = 4.dp.toPx(),
                    center = Offset(activeW.coerceIn(7.dp.toPx(), size.width - 7.dp.toPx()), cy)
                )
            }
        }
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween
        ) {
            Text(formatTime(displayPos), style = MaterialTheme.typography.labelSmall)
            Text("-${formatTime(remaining)}", style = MaterialTheme.typography.labelSmall)
        }

        if (hasChapter) {
            Row(
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                Text("全书", style = MaterialTheme.typography.labelMedium)
                Switch(
                    checked = showChapterMode,
                    onCheckedChange = { showChapterMode = it },
                    colors = SwitchDefaults.colors(
                        checkedThumbColor = Color.Black,
                        checkedTrackColor = Color(0xFF4FC3F7)
                    ),
                    modifier = Modifier.scale(0.72f)
                )
                Text("章节", style = MaterialTheme.typography.labelMedium)
            }
        }

        Spacer(modifier = Modifier.height(10.dp))
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceEvenly,
            verticalAlignment = Alignment.CenterVertically
        ) {
            Surface(
                shape = CircleShape,
                color = Color(0xFF1A1F2B),
                modifier = Modifier
                    .size(44.dp)
                    .clip(CircleShape)
                    .clickable(enabled = currentChapterIdx > 0) {
                        sess.chapters.getOrNull(currentChapterIdx - 1)?.let { vm.seekToSec(it.start) }
                    }
            ) {
                Box(contentAlignment = Alignment.Center) {
                    Icon(
                        Icons.Outlined.SkipPrevious,
                        contentDescription = "上一章",
                        tint = if (currentChapterIdx > 0) Color.White else Color.Gray,
                        modifier = Modifier.size(22.dp)
                    )
                }
            }
            Surface(
                shape = RoundedCornerShape(22.dp),
                color = Color(0xFF1A1F2B),
                modifier = Modifier
                    .clip(RoundedCornerShape(22.dp))
                    .clickable { vm.skipBy(-10.0) }
            ) {
                Text(
                    "-10s",
                    color = Color.White,
                    style = MaterialTheme.typography.labelMedium,
                    fontWeight = FontWeight.SemiBold,
                    modifier = Modifier.padding(horizontal = 16.dp, vertical = 12.dp)
                )
            }
            Button(
                onClick = { vm.togglePlayPause() },
                shape = CircleShape,
                colors = ButtonDefaults.buttonColors(
                    containerColor = Color(0xFFD4AF37),
                    contentColor = Color.Black
                ),
                modifier = Modifier.size(64.dp),
                contentPadding = PaddingValues(0.dp)
            ) {
                Canvas(modifier = Modifier.size(22.dp)) {
                    val w = size.width
                    val h = size.height
                    if (vm.isPlaying) {
                        val strokeW = 2.5.dp.toPx()
                        drawLine(Color.Black, Offset(w * 0.30f, h * 0.15f), Offset(w * 0.30f, h * 0.85f), strokeWidth = strokeW)
                        drawLine(Color.Black, Offset(w * 0.70f, h * 0.15f), Offset(w * 0.70f, h * 0.85f), strokeWidth = strokeW)
                    } else {
                        val path = Path().apply {
                            moveTo(w * 0.22f, h * 0.12f)
                            lineTo(w * 0.86f, h * 0.50f)
                            lineTo(w * 0.22f, h * 0.88f)
                            close()
                        }
                        drawPath(
                            path = path,
                            color = Color.Black,
                            style = Stroke(width = 2.2.dp.toPx(), join = StrokeJoin.Round)
                        )
                    }
                }
            }
            Surface(
                shape = RoundedCornerShape(22.dp),
                color = Color(0xFF1A1F2B),
                modifier = Modifier
                    .clip(RoundedCornerShape(22.dp))
                    .clickable { vm.skipBy(30.0) }
            ) {
                Text(
                    "+30s",
                    color = Color.White,
                    style = MaterialTheme.typography.labelMedium,
                    fontWeight = FontWeight.SemiBold,
                    modifier = Modifier.padding(horizontal = 16.dp, vertical = 12.dp)
                )
            }
            Surface(
                shape = CircleShape,
                color = Color(0xFF1A1F2B),
                modifier = Modifier
                    .size(44.dp)
                    .clip(CircleShape)
                    .clickable(enabled = currentChapterIdx >= 0 && currentChapterIdx + 1 < sess.chapters.size) {
                        sess.chapters.getOrNull(currentChapterIdx + 1)?.let { vm.seekToSec(it.start) }
                    }
            ) {
                Box(contentAlignment = Alignment.Center) {
                    Icon(
                        Icons.Outlined.SkipNext,
                        contentDescription = "下一章",
                        tint = if (currentChapterIdx >= 0 && currentChapterIdx + 1 < sess.chapters.size) Color.White else Color.Gray,
                        modifier = Modifier.size(22.dp)
                    )
                }
            }
        }

        var showChaptersDialog by remember { mutableStateOf(false) }
        var showSkipDialog by remember { mutableStateOf(false) }

        Spacer(modifier = Modifier.height(14.dp))
        var showSpeedMenu by remember { mutableStateOf(false) }
        var showSleepMenu by remember { mutableStateOf(false) }
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceEvenly
        ) {
            Box {
                IconButton(onClick = { showSpeedMenu = true }) {
                    Icon(
                        Icons.Outlined.PlayCircle,
                        contentDescription = "倍速 ${vm.playbackSpeed}x",
                        tint = if (vm.playbackSpeed != 1.0f) Color(0xFF4FC3F7) else MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }
                DropdownMenu(
                    expanded = showSpeedMenu,
                    onDismissRequest = { showSpeedMenu = false }
                ) {
                    listOf(0.5f, 0.8f, 1.0f, 1.3f, 1.5f, 1.8f, 2.0f, 2.5f, 3.0f).forEach { spd ->
                        DropdownMenuItem(
                            text = {
                                Text(if (vm.playbackSpeed == spd) "✓  ${spd}x" else "    ${spd}x")
                            },
                            onClick = {
                                vm.changeSpeed(spd)
                                showSpeedMenu = false
                            }
                        )
                    }
                }
            }
            Box {
                IconButton(onClick = { showSleepMenu = true }) {
                    val sleepActive = vm.sleepEndOfChapter || vm.sleepRemainingSec > 0
                    Icon(
                        Icons.Outlined.Timer,
                        contentDescription = "睡眠定时",
                        tint = if (sleepActive) Color(0xFF4FC3F7) else MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }
                DropdownMenu(
                    expanded = showSleepMenu,
                    onDismissRequest = { showSleepMenu = false }
                ) {
                    DropdownMenuItem(
                        text = { Text(if (vm.sleepEndOfChapter) "✓  播完本章再停" else "    播完本章再停") },
                        onClick = {
                            if (!vm.sleepEndOfChapter) vm.toggleSleepEndOfChapter()
                            showSleepMenu = false
                        }
                    )
                    DropdownMenuItem(
                        text = { Text(if (!vm.sleepEndOfChapter && vm.sleepTimerMinutes == 0) "✓  关闭" else "    关闭") },
                        onClick = {
                            if (vm.sleepEndOfChapter) vm.toggleSleepEndOfChapter()
                            vm.setSleepTimer(0)
                            showSleepMenu = false
                        }
                    )
                    listOf(5 to "5 分钟", 10 to "10 分钟", 15 to "15 分钟", 30 to "30 分钟", 45 to "45 分钟", 60 to "1 小时", 90 to "1.5 小时", 120 to "2 小时").forEach { (min, label) ->
                        DropdownMenuItem(
                            text = { Text(if (!vm.sleepEndOfChapter && vm.sleepTimerMinutes == min) "✓  $label" else "    $label") },
                            onClick = {
                                if (vm.sleepEndOfChapter) vm.toggleSleepEndOfChapter()
                                vm.setSleepTimer(min)
                                showSleepMenu = false
                            }
                        )
                    }
                }
            }
            IconButton(onClick = { showSkipDialog = true }) {
                val skipActive = vm.currentIntroSec > 0 || vm.currentOutroSec > 0
                Icon(
                    Icons.Outlined.ContentCut,
                    contentDescription = "跳过片头片尾",
                    tint = if (skipActive) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
            if (sess.chapters.isNotEmpty()) {
                IconButton(onClick = { showChaptersDialog = true }) {
                    Icon(
                        Icons.Outlined.FormatListBulleted,
                        contentDescription = "章节列表",
                        tint = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }
            }
        }

        if (showChaptersDialog) {
            AlertDialog(
                onDismissRequest = { showChaptersDialog = false },
                title = { Text("章节列表") },
                text = {
                    LazyColumn(modifier = Modifier.height(340.dp)) {
                        itemsIndexed(sess.chapters) { _, ch ->
                            val isCurrent = currentChapter?.id == ch.id
                            Row(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .clickable {
                                        vm.seekToSec(ch.start)
                                        showChaptersDialog = false
                                    }
                                    .padding(vertical = 10.dp, horizontal = 6.dp),
                                horizontalArrangement = Arrangement.SpaceBetween
                            ) {
                                Text(
                                    text = ch.title,
                                    color = if (isCurrent) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurface,
                                    fontWeight = if (isCurrent) FontWeight.Bold else FontWeight.Normal,
                                    maxLines = 1,
                                    overflow = TextOverflow.Ellipsis,
                                    modifier = Modifier.weight(1f)
                                )
                                Text(
                                    text = formatTime(ch.start),
                                    style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant
                                )
                            }
                        }
                    }
                },
                confirmButton = {
                    TextButton(onClick = { showChaptersDialog = false }) { Text("关闭") }
                }
            )
        }

        if (showSkipDialog) {
            var introText by remember { mutableStateOf(vm.currentIntroSec.toString()) }
            var outroText by remember { mutableStateOf(vm.currentOutroSec.toString()) }
            AlertDialog(
                onDismissRequest = { showSkipDialog = false },
                title = { Text("本书跳过片头 / 片尾") },
                text = {
                    Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                        OutlinedTextField(
                            value = introText,
                            onValueChange = { introText = it.filter { c -> c.isDigit() } },
                            label = { Text("跳过片头（秒）") },
                            singleLine = true
                        )
                        OutlinedTextField(
                            value = outroText,
                            onValueChange = { outroText = it.filter { c -> c.isDigit() } },
                            label = { Text("跳过片尾（秒）") },
                            singleLine = true
                        )
                    }
                },
                confirmButton = {
                    TextButton(onClick = {
                        vm.updateBookSkip(introText.toIntOrNull() ?: 0, outroText.toIntOrNull() ?: 0)
                        showSkipDialog = false
                    }) { Text("保存") }
                },
                dismissButton = {
                    TextButton(onClick = { showSkipDialog = false }) { Text("取消") }
                }
            )
        }

        vm.playerError?.let { err ->
            Spacer(modifier = Modifier.height(12.dp))
            Text(err, color = MaterialTheme.colorScheme.error)
        }
    }
}

private fun formatTime(sec: Double): String {
    val total = sec.toInt().coerceAtLeast(0)
    val h = total / 3600
    val m = (total % 3600) / 60
    val s = total % 60
    return if (h > 0) {
        "%d:%02d:%02d".format(h, m, s)
    } else {
        "%02d:%02d".format(m, s)
    }
}

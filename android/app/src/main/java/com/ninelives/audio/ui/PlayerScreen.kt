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
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.BookmarkBorder
import androidx.compose.material.icons.outlined.ContentCut
import androidx.compose.material.icons.outlined.Delete
import androidx.compose.material.icons.outlined.FormatListBulleted
import androidx.compose.material.icons.outlined.Speed
import androidx.compose.material.icons.outlined.SkipNext
import androidx.compose.material.icons.outlined.SkipPrevious
import androidx.compose.material.icons.outlined.Timer
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.HorizontalDivider
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
import androidx.compose.material3.Slider
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
    val cs = MaterialTheme.colorScheme
    val sess = vm.currentSession
    if (sess == null) {
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(24.dp),
            verticalArrangement = Arrangement.Center,
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            Text(t("尚未选择有声书", "No audiobook selected"), style = MaterialTheme.typography.titleMedium)
            Spacer(modifier = Modifier.height(8.dp))
            Text(
                t("请在「九命」或「书库」中点击任意一本书开始收听。", "Tap any book in NineLives or Library to start listening."),
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
            text = t("第 ${trackIdx + 1} 轨 / 共 ${sess.tracks.size} 轨", "Track ${trackIdx + 1} / ${sess.tracks.size}"),
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
                text = t("开始 ${formatTime(currentChapter.start)} · 结束 ${formatTime(currentChapter.end)}", "Start ${formatTime(currentChapter.start)} · End ${formatTime(currentChapter.end)}"),
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
                    color = cs.outline,
                    topLeft = Offset(0f, trackTop),
                    size = Size(size.width, trackH),
                    cornerRadius = CornerRadius(trackH / 2f)
                )
                if (activeW > 0f) {
                    drawRoundRect(
                        color = cs.primary,
                        topLeft = Offset(0f, trackTop),
                        size = Size(activeW, trackH),
                        cornerRadius = CornerRadius(trackH / 2f)
                    )
                }
                drawCircle(
                    color = cs.surfaceContainerHighest,
                    radius = 7.dp.toPx(),
                    center = Offset(activeW.coerceIn(7.dp.toPx(), size.width - 7.dp.toPx()), cy)
                )
                drawCircle(
                    color = cs.primary,
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
                Text(t("全书", "Whole book"), style = MaterialTheme.typography.labelMedium)
                Switch(
                    checked = showChapterMode,
                    onCheckedChange = { showChapterMode = it },
                    colors = SwitchDefaults.colors(
                        checkedThumbColor = cs.onPrimary,
                        checkedTrackColor = cs.primary
                    ),
                    modifier = Modifier.scale(0.72f)
                )
                Text(t("章节", "Chapter"), style = MaterialTheme.typography.labelMedium)
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
                color = cs.surfaceContainerHigh,
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
                        contentDescription = t("上一章", "Previous chapter"),
                        tint = if (currentChapterIdx > 0) cs.onSurface else cs.onSurfaceVariant.copy(alpha = 0.4f),
                        modifier = Modifier.size(22.dp)
                    )
                }
            }
            Surface(
                shape = RoundedCornerShape(22.dp),
                color = cs.surfaceContainerHigh,
                modifier = Modifier
                    .clip(RoundedCornerShape(22.dp))
                    .clickable { vm.skipBy(-10.0) }
            ) {
                Text(
                    "-10s",
                    color = cs.onSurface,
                    style = MaterialTheme.typography.labelMedium,
                    fontWeight = FontWeight.SemiBold,
                    modifier = Modifier.padding(horizontal = 16.dp, vertical = 12.dp)
                )
            }
            Button(
                onClick = { vm.togglePlayPause() },
                shape = CircleShape,
                colors = ButtonDefaults.buttonColors(
                    containerColor = cs.primary,
                    contentColor = cs.onPrimary
                ),
                modifier = Modifier.size(64.dp),
                contentPadding = PaddingValues(0.dp)
            ) {
                Canvas(modifier = Modifier.size(22.dp)) {
                    val w = size.width
                    val h = size.height
                    if (vm.isPlaying) {
                        val strokeW = 2.5.dp.toPx()
                        drawLine(cs.onPrimary, Offset(w * 0.30f, h * 0.15f), Offset(w * 0.30f, h * 0.85f), strokeWidth = strokeW)
                        drawLine(cs.onPrimary, Offset(w * 0.70f, h * 0.15f), Offset(w * 0.70f, h * 0.85f), strokeWidth = strokeW)
                    } else {
                        val path = Path().apply {
                            moveTo(w * 0.22f, h * 0.12f)
                            lineTo(w * 0.86f, h * 0.50f)
                            lineTo(w * 0.22f, h * 0.88f)
                            close()
                        }
                        drawPath(
                            path = path,
                            color = cs.onPrimary,
                            style = Stroke(width = 2.2.dp.toPx(), join = StrokeJoin.Round)
                        )
                    }
                }
            }
            Surface(
                shape = RoundedCornerShape(22.dp),
                color = cs.surfaceContainerHigh,
                modifier = Modifier
                    .clip(RoundedCornerShape(22.dp))
                    .clickable { vm.skipBy(30.0) }
            ) {
                Text(
                    "+30s",
                    color = cs.onSurface,
                    style = MaterialTheme.typography.labelMedium,
                    fontWeight = FontWeight.SemiBold,
                    modifier = Modifier.padding(horizontal = 16.dp, vertical = 12.dp)
                )
            }
            Surface(
                shape = CircleShape,
                color = cs.surfaceContainerHigh,
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
                        contentDescription = t("下一章", "Next chapter"),
                        tint = if (currentChapterIdx >= 0 && currentChapterIdx + 1 < sess.chapters.size) cs.onSurface else cs.onSurfaceVariant.copy(alpha = 0.4f),
                        modifier = Modifier.size(22.dp)
                    )
                }
            }
        }

        var showChaptersDialog by remember { mutableStateOf(false) }
        var showSkipDialog by remember { mutableStateOf(false) }
        var showBookmarksDialog by remember { mutableStateOf(false) }

        Spacer(modifier = Modifier.height(14.dp))
        var showSpeedMenu by remember { mutableStateOf(false) }
        var showSleepMenu by remember { mutableStateOf(false) }
        var showVolumeMenu by remember { mutableStateOf(false) }
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceEvenly,
            verticalAlignment = Alignment.Top
        ) {
            Box {
                ActionButton(
                    kind = "speed",
                    label = t("倍速", "Speed"),
                    active = vm.playbackSpeed != 1.0f
                ) { showSpeedMenu = true }
                DropdownMenu(expanded = showSpeedMenu, onDismissRequest = { showSpeedMenu = false }) {
                    listOf(0.5f, 0.8f, 1.0f, 1.3f, 1.5f, 1.8f, 2.0f, 2.5f, 3.0f).forEach { spd ->
                        DropdownMenuItem(
                            text = { Text(if (vm.playbackSpeed == spd) "✓  ${spd}x" else "    ${spd}x") },
                            onClick = {
                                vm.changeSpeed(spd)
                                showSpeedMenu = false
                            }
                        )
                    }
                }
            }
            Box {
                val sleepActive = vm.sleepEndOfChapter || vm.sleepRemainingSec > 0
                ActionButton("sleep", t("睡眠", "Sleep"), sleepActive) { showSleepMenu = true }
                DropdownMenu(expanded = showSleepMenu, onDismissRequest = { showSleepMenu = false }) {
                    DropdownMenuItem(
                        text = { Text(if (vm.sleepEndOfChapter) t("✓  播完本章再停", "✓  Stop after this chapter") else t("    播完本章再停", "    Stop after this chapter")) },
                        onClick = {
                            if (!vm.sleepEndOfChapter) vm.toggleSleepEndOfChapter()
                            showSleepMenu = false
                        }
                    )
                    DropdownMenuItem(
                        text = { Text(if (!vm.sleepEndOfChapter && vm.sleepTimerMinutes == 0) t("✓  关闭", "✓  Off") else t("    关闭", "    Off")) },
                        onClick = {
                            if (vm.sleepEndOfChapter) vm.toggleSleepEndOfChapter()
                            vm.setSleepTimer(0)
                            showSleepMenu = false
                        }
                    )
                    listOf(5 to t("5 分钟", "5 min"), 10 to t("10 分钟", "10 min"), 15 to t("15 分钟", "15 min"), 30 to t("30 分钟", "30 min"), 45 to t("45 分钟", "45 min"), 60 to t("1 小时", "1 hour"), 90 to t("1.5 小时", "1.5 hours"), 120 to t("2 小时", "2 hours")).forEach { (min, label) ->
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
            Box {
                ActionButton("volume", t("音量", "Volume"), vm.volume < 0.99f) { showVolumeMenu = true }
                DropdownMenu(expanded = showVolumeMenu, onDismissRequest = { showVolumeMenu = false }) {
                    Column(modifier = Modifier.padding(horizontal = 12.dp).width(200.dp)) {
                        Text(
                            t("音量 ${(vm.volume * 100).toInt()}%", "Volume ${(vm.volume * 100).toInt()}%"),
                            style = MaterialTheme.typography.labelMedium
                        )
                        Slider(value = vm.volume, onValueChange = { vm.setVolume(it) })
                    }
                }
            }
            ActionButton(
                kind = "cut",
                label = t("跳过", "Skip"),
                active = vm.currentIntroSec > 0 || vm.currentOutroSec > 0
            ) { showSkipDialog = true }
            if (sess.chapters.isNotEmpty()) {
                ActionButton("chapters", t("章节", "Chapters"), false) { showChaptersDialog = true }
            }
            ActionButton("bookmark", t("书签", "Bookmarks"), vm.bookmarks.isNotEmpty()) {
                vm.loadBookmarks()
                showBookmarksDialog = true
            }
        }

        if (showBookmarksDialog) {
            AlertDialog(
                onDismissRequest = { showBookmarksDialog = false },
                title = { Text(t("书签", "Bookmark")) },
                text = {
                    Column(modifier = Modifier.fillMaxWidth()) {
                        if (vm.bookmarkStatus.isNotBlank()) {
                            Text(
                                vm.bookmarkStatus,
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant
                            )
                            Spacer(modifier = Modifier.height(6.dp))
                        }
                        Text(
                            t("当前位置 ${formatTime(vm.currentAbsolutePosition())}", "At ${formatTime(vm.currentAbsolutePosition())}"),
                            style = MaterialTheme.typography.bodyMedium
                        )
                        Spacer(modifier = Modifier.height(6.dp))
                        if (vm.bookmarks.isEmpty()) {
                            Text(
                                t("还没有书签。点下方按钮记录当前进度，之后可从列表一键跳回。", "No bookmarks yet. Tap below to save your place and jump back anytime."),
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant
                            )
                        } else {
                            HorizontalDivider()
                            LazyColumn(modifier = Modifier.height(300.dp)) {
                                itemsIndexed(vm.bookmarks) { _, bm ->
                                    Row(
                                        verticalAlignment = Alignment.CenterVertically,
                                        modifier = Modifier
                                            .fillMaxWidth()
                                            .clickable {
                                                vm.jumpToBookmark(bm)
                                                showBookmarksDialog = false
                                            }
                                            .padding(vertical = 8.dp)
                                    ) {
                                        Column(modifier = Modifier.weight(1f)) {
                                            Text(
                                                bm.title,
                                                maxLines = 1,
                                                overflow = TextOverflow.Ellipsis,
                                                style = MaterialTheme.typography.bodyMedium
                                            )
                                            Text(
                                                formatTime(bm.time),
                                                style = MaterialTheme.typography.bodySmall,
                                                color = cs.primary
                                            )
                                        }
                                        IconButton(onClick = { vm.deleteBookmark(bm) }) {
                                            Icon(
                                                Icons.Outlined.Delete,
                                                contentDescription = t("删除书签", "Delete bookmark"),
                                                tint = MaterialTheme.colorScheme.onSurfaceVariant,
                                                modifier = Modifier.size(20.dp)
                                            )
                                        }
                                    }
                                }
                            }
                        }
                    }
                },
                confirmButton = {
                    Button(onClick = { vm.addBookmarkAtCurrent() }) {
                        Text(t("添加当前进度", "Bookmark this position"))
                    }
                },
                dismissButton = {
                    TextButton(onClick = { showBookmarksDialog = false }) { Text(t("关闭", "Close")) }
                }
            )
        }

        if (showChaptersDialog) {
            AlertDialog(
                onDismissRequest = { showChaptersDialog = false },
                title = { Text(t("章节列表", "Chapters")) },
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
                    TextButton(onClick = { showChaptersDialog = false }) { Text(t("关闭", "Close")) }
                }
            )
        }

        if (showSkipDialog) {
            var introText by remember { mutableStateOf(vm.currentIntroSec.toString()) }
            var outroText by remember { mutableStateOf(vm.currentOutroSec.toString()) }
            AlertDialog(
                onDismissRequest = { showSkipDialog = false },
                title = { Text(t("本书跳过片头 / 片尾", "Skip intro / outro for this book")) },
                text = {
                    Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                        OutlinedTextField(
                            value = introText,
                            onValueChange = { introText = it.filter { c -> c.isDigit() } },
                            label = { Text(t("跳过片头（秒）", "Skip intro (sec)")) },
                            singleLine = true
                        )
                        OutlinedTextField(
                            value = outroText,
                            onValueChange = { outroText = it.filter { c -> c.isDigit() } },
                            label = { Text(t("跳过片尾（秒）", "Skip outro (sec)")) },
                            singleLine = true
                        )
                    }
                },
                confirmButton = {
                    TextButton(onClick = {
                        vm.updateBookSkip(introText.toIntOrNull() ?: 0, outroText.toIntOrNull() ?: 0)
                        showSkipDialog = false
                    }) { Text(t("保存", "Save")) }
                },
                dismissButton = {
                    TextButton(onClick = { showSkipDialog = false }) { Text(t("取消", "Cancel")) }
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

@Composable
private fun ActionButton(kind: String, label: String, active: Boolean, onClick: () -> Unit) {
    Column(horizontalAlignment = Alignment.CenterHorizontally) {
        IconButton(onClick = onClick, modifier = Modifier.size(40.dp)) {
            PlayerGlyph(
                kind = kind,
                tint = if (active) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.size(22.dp)
            )
        }
        Text(
            text = label,
            style = MaterialTheme.typography.labelSmall,
            color = if (active) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurfaceVariant
        )
    }
}

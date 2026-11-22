package com.ninelives.audio.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
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
import androidx.compose.material.icons.rounded.Bedtime
import androidx.compose.material.icons.rounded.FastForward
import androidx.compose.material.icons.rounded.FormatListBulleted
import androidx.compose.material.icons.rounded.Forward30
import androidx.compose.material.icons.rounded.Pause
import androidx.compose.material.icons.rounded.PlayArrow
import androidx.compose.material.icons.rounded.Replay10
import androidx.compose.material.icons.rounded.SkipNext
import androidx.compose.material.icons.rounded.SkipPrevious
import androidx.compose.material.icons.rounded.Speed
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Surface
import androidx.compose.material3.Switch
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
import androidx.compose.ui.graphics.Color
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

        Slider(
            value = sliderVal,
            onValueChange = { ratio -> draggingRatio = ratio },
            onValueChangeFinished = {
                draggingRatio?.let { r -> vm.seekToSec(displayStart + r * displayDuration) }
                draggingRatio = null
            },
            colors = SliderDefaults.colors(
                thumbColor = Color(0xFFD4AF37),
                activeTrackColor = Color(0xFFD4AF37),
                inactiveTrackColor = MaterialTheme.colorScheme.surfaceVariant
            ),
            modifier = Modifier.fillMaxWidth()
        )
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
                    onCheckedChange = { showChapterMode = it }
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
            IconButton(
                enabled = currentChapterIdx > 0,
                onClick = {
                    sess.chapters.getOrNull(currentChapterIdx - 1)?.let { vm.seekToSec(it.start) }
                }
            ) {
                Icon(
                    Icons.Rounded.SkipPrevious,
                    contentDescription = "上一章",
                    modifier = Modifier.size(28.dp)
                )
            }
            IconButton(onClick = { vm.skipBy(-10.0) }) {
                Icon(
                    Icons.Rounded.Replay10,
                    contentDescription = "快退10秒",
                    modifier = Modifier.size(28.dp)
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
                Icon(
                    if (vm.isPlaying) Icons.Rounded.Pause else Icons.Rounded.PlayArrow,
                    contentDescription = if (vm.isPlaying) "暂停" else "播放",
                    modifier = Modifier.size(34.dp)
                )
            }
            IconButton(onClick = { vm.skipBy(30.0) }) {
                Icon(
                    Icons.Rounded.Forward30,
                    contentDescription = "快进30秒",
                    modifier = Modifier.size(28.dp)
                )
            }
            IconButton(
                enabled = currentChapterIdx >= 0 && currentChapterIdx + 1 < sess.chapters.size,
                onClick = {
                    sess.chapters.getOrNull(currentChapterIdx + 1)?.let { vm.seekToSec(it.start) }
                }
            ) {
                Icon(
                    Icons.Rounded.SkipNext,
                    contentDescription = "下一章",
                    modifier = Modifier.size(28.dp)
                )
            }
        }

        var showChaptersDialog by remember { mutableStateOf(false) }
        var showSkipDialog by remember { mutableStateOf(false) }

        Spacer(modifier = Modifier.height(14.dp))
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceEvenly
        ) {
            IconButton(onClick = { vm.cycleSpeed() }) {
                Icon(
                    Icons.Rounded.Speed,
                    contentDescription = "倍速 ${vm.playbackSpeed}x",
                    tint = if (vm.playbackSpeed != 1.0f) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
            IconButton(onClick = {
                if (vm.sleepEndOfChapter) {
                    vm.toggleSleepEndOfChapter()
                } else {
                    when (vm.sleepTimerMinutes) {
                        0 -> vm.setSleepTimer(15)
                        15 -> vm.setSleepTimer(30)
                        30 -> vm.setSleepTimer(60)
                        60 -> vm.toggleSleepEndOfChapter()
                        else -> vm.setSleepTimer(0)
                    }
                }
            }) {
                val sleepActive = vm.sleepEndOfChapter || vm.sleepRemainingSec > 0
                Icon(
                    Icons.Rounded.Bedtime,
                    contentDescription = "睡眠定时",
                    tint = if (sleepActive) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
            IconButton(onClick = { showSkipDialog = true }) {
                val skipActive = vm.currentIntroSec > 0 || vm.currentOutroSec > 0
                Icon(
                    Icons.Rounded.FastForward,
                    contentDescription = "跳过片头片尾",
                    tint = if (skipActive) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
            if (sess.chapters.isNotEmpty()) {
                IconButton(onClick = { showChaptersDialog = true }) {
                    Icon(
                        Icons.Rounded.FormatListBulleted,
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

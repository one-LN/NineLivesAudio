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
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Slider
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
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 20.dp, vertical = 12.dp),
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        AsyncImage(
            model = sess.coverUrl,
            contentDescription = sess.title,
            contentScale = ContentScale.Crop,
            modifier = Modifier
                .size(180.dp)
                .clip(RoundedCornerShape(14.dp))
        )
        Spacer(modifier = Modifier.height(10.dp))
        Surface(
            shape = RoundedCornerShape(16.dp),
            tonalElevation = 2.dp
        ) {
            Text(
                text = "正在从服务器串流",
                style = MaterialTheme.typography.labelSmall,
                modifier = Modifier.padding(horizontal = 12.dp, vertical = 4.dp)
            )
        }
        Spacer(modifier = Modifier.height(10.dp))
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

        Spacer(modifier = Modifier.height(10.dp))
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

        Slider(
            value = (displayPos / displayDuration).toFloat().coerceIn(0f, 1f),
            onValueChange = { ratio -> vm.seekToSec(displayStart + ratio * displayDuration) },
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
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically
        ) {
            OutlinedButton(
                enabled = currentChapterIdx > 0,
                onClick = {
                    sess.chapters.getOrNull(currentChapterIdx - 1)?.let { vm.seekToSec(it.start) }
                },
                shape = CircleShape,
                modifier = Modifier.size(44.dp),
                contentPadding = PaddingValues(0.dp)
            ) {
                Text("|<", style = MaterialTheme.typography.labelSmall)
            }
            OutlinedButton(
                onClick = { vm.skipBy(-10.0) },
                contentPadding = PaddingValues(horizontal = 12.dp)
            ) {
                Text("-10s")
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
                Text(if (vm.isPlaying) "||" else "▶", fontWeight = FontWeight.Bold)
            }
            OutlinedButton(
                onClick = { vm.skipBy(30.0) },
                contentPadding = PaddingValues(horizontal = 12.dp)
            ) {
                Text("+30s")
            }
            OutlinedButton(
                enabled = currentChapterIdx >= 0 && currentChapterIdx + 1 < sess.chapters.size,
                onClick = {
                    sess.chapters.getOrNull(currentChapterIdx + 1)?.let { vm.seekToSec(it.start) }
                },
                shape = CircleShape,
                modifier = Modifier.size(44.dp),
                contentPadding = PaddingValues(0.dp)
            ) {
                Text(">|", style = MaterialTheme.typography.labelSmall)
            }
        }

        var showChaptersDialog by remember { mutableStateOf(false) }
        var showSkipDialog by remember { mutableStateOf(false) }

        Spacer(modifier = Modifier.height(14.dp))
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceEvenly
        ) {
            TextButton(onClick = { vm.cycleSpeed() }) {
                Text("倍速\n${vm.playbackSpeed}x", textAlign = TextAlign.Center, style = MaterialTheme.typography.labelSmall)
            }
            TextButton(onClick = {
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
                val label = when {
                    vm.sleepEndOfChapter -> "睡眠\n本章"
                    vm.sleepRemainingSec > 0 -> "睡眠\n${vm.sleepRemainingSec / 60}m"
                    else -> "睡眠\n关闭"
                }
                Text(label, textAlign = TextAlign.Center, style = MaterialTheme.typography.labelSmall)
            }
            TextButton(onClick = { showSkipDialog = true }) {
                Text("跳过\n${vm.currentIntroSec}/${vm.currentOutroSec}s", textAlign = TextAlign.Center, style = MaterialTheme.typography.labelSmall)
            }
            if (sess.chapters.isNotEmpty()) {
                TextButton(onClick = { showChaptersDialog = true }) {
                    Text("章节\n${sess.chapters.size}", textAlign = TextAlign.Center, style = MaterialTheme.typography.labelSmall)
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

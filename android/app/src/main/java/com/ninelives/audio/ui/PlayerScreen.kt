package com.ninelives.audio.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
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
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontWeight
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

    val currentChapter = sess.chapters.lastOrNull { vm.currentPositionSec >= it.start }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .padding(20.dp),
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        Spacer(modifier = Modifier.height(12.dp))
        AsyncImage(
            model = sess.coverUrl,
            contentDescription = sess.title,
            contentScale = ContentScale.Crop,
            modifier = Modifier
                .size(220.dp)
                .clip(RoundedCornerShape(16.dp))
        )
        Spacer(modifier = Modifier.height(20.dp))
        Text(
            text = sess.title,
            style = MaterialTheme.typography.titleLarge,
            fontWeight = FontWeight.Bold,
            maxLines = 2,
            overflow = TextOverflow.Ellipsis
        )
        if (sess.author.isNotBlank()) {
            Spacer(modifier = Modifier.height(4.dp))
            Text(
                text = sess.author,
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
        }
        if (currentChapter != null) {
            Spacer(modifier = Modifier.height(6.dp))
            Text(
                text = currentChapter.title,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.primary,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis
            )
        }

        Spacer(modifier = Modifier.height(16.dp))
        var showFullBook by remember { mutableStateOf(false) }
        val hasChapter = currentChapter != null && currentChapter.end > currentChapter.start
        val displayStart = if (!showFullBook && hasChapter) currentChapter!!.start else 0.0
        val displayDuration = if (!showFullBook && hasChapter) {
            (currentChapter!!.end - currentChapter!!.start).coerceAtLeast(1.0)
        } else {
            vm.totalDurationSec.coerceAtLeast(1.0)
        }
        val displayPos = (vm.currentPositionSec - displayStart).coerceIn(0.0, displayDuration)

        Slider(
            value = (displayPos / displayDuration).toFloat().coerceIn(0f, 1f),
            onValueChange = { ratio -> vm.seekToSec(displayStart + ratio * displayDuration) },
            modifier = Modifier.fillMaxWidth()
        )
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically
        ) {
            Text(formatTime(displayPos), style = MaterialTheme.typography.bodySmall)
            if (hasChapter) {
                OutlinedButton(onClick = { showFullBook = !showFullBook }) {
                    Text(if (showFullBook) "全书进度" else "章节进度", style = MaterialTheme.typography.labelSmall)
                }
            }
            Text(formatTime(displayDuration), style = MaterialTheme.typography.bodySmall)
        }

        Spacer(modifier = Modifier.height(20.dp))
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceEvenly,
            verticalAlignment = Alignment.CenterVertically
        ) {
            OutlinedButton(onClick = { vm.skipBy(-15.0) }) {
                Text("-15s")
            }
            Button(onClick = { vm.togglePlayPause() }) {
                Text(if (vm.isPlaying) "暂停" else "播放")
            }
            OutlinedButton(onClick = { vm.skipBy(30.0) }) {
                Text("+30s")
            }
        }

        var showChaptersDialog by remember { mutableStateOf(false) }
        var showSkipDialog by remember { mutableStateOf(false) }

        Spacer(modifier = Modifier.height(16.dp))
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceEvenly
        ) {
            OutlinedButton(onClick = { vm.cycleSpeed() }) {
                Text("${vm.playbackSpeed}x")
            }
            OutlinedButton(onClick = {
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
                    vm.sleepEndOfChapter -> "播完本章"
                    vm.sleepRemainingSec > 0 -> "定时 ${vm.sleepRemainingSec / 60}:${(vm.sleepRemainingSec % 60).toString().padStart(2, '0')}"
                    else -> "睡眠定时"
                }
                Text(label)
            }
        }

        Spacer(modifier = Modifier.height(10.dp))
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceEvenly
        ) {
            if (sess.chapters.isNotEmpty()) {
                OutlinedButton(onClick = { showChaptersDialog = true }) {
                    Text("章节 (${sess.chapters.size})")
                }
            }
            OutlinedButton(onClick = { showSkipDialog = true }) {
                Text("片头 ${vm.currentIntroSec}s / 片尾 ${vm.currentOutroSec}s")
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

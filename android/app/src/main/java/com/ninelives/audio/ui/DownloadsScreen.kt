package com.ninelives.audio.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.ninelives.audio.data.DownloadTask
import com.ninelives.audio.data.DownloadedBook

@Composable
fun DownloadsScreen(vm: MainViewModel, onBookClick: (DownloadedBook) -> Unit) {
    val active = vm.downloadTasks
    val completed = vm.downloadedBooks
    LazyColumn(
        modifier = Modifier.fillMaxSize().padding(horizontal = 16.dp, vertical = 14.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp)
    ) {
        item {
            Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                Text("下载管理", style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold)
                if (active.any { it.state == "failed" || it.state == "waiting_wifi" }) {
                    TextButton(onClick = { vm.retryAllDownloads() }) { Text("重试全部") }
                }
            }
        }
        item {
            Card(modifier = Modifier.fillMaxWidth()) {
                Column(modifier = Modifier.padding(16.dp)) {
                    Text("下载设置", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
                    Spacer(modifier = Modifier.height(6.dp))
                    Text(
                        if (vm.wifiOnlyDownload) "仅在 Wi-Fi / 以太网下下载" else "允许使用移动网络下载",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                    Text("当前下载队列 ${active.size} 项 · 已下载 ${completed.size} 本", style = MaterialTheme.typography.bodySmall)
                }
            }
        }
        if (active.isNotEmpty()) {
            item { Text("下载队列", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold) }
            items(active, key = { it.key }) { task -> DownloadTaskCard(task, vm) }
        }
        if (completed.isNotEmpty()) {
            item { Text("离线书籍", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold) }
            items(completed, key = { "${it.serverUrl}:${it.itemId}" }) { book -> DownloadedBookCard(book, vm, onBookClick) }
        }
        if (active.isEmpty() && completed.isEmpty()) {
            item {
                Card(modifier = Modifier.fillMaxWidth()) {
                    Column(modifier = Modifier.padding(16.dp)) {
                        Text("还没有离线书籍", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
                        Spacer(modifier = Modifier.height(6.dp))
                        Text("在书库中打开一本书，点「下载离线」即可在本机保存音频。")
                    }
                }
            }
        }
    }
}

@Composable
private fun DownloadTaskCard(task: DownloadTask, vm: MainViewModel) {
    Card(modifier = Modifier.fillMaxWidth()) {
        Column(modifier = Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(7.dp)) {
            Text(task.title, style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.SemiBold)
            Text(task.author.ifBlank { "未知作者" }, style = MaterialTheme.typography.bodySmall)
            LinearProgressIndicator(
                progress = { task.progress / 100f },
                modifier = Modifier.fillMaxWidth()
            )
            Text("${task.progress}% · ${formatBytes(task.downloadedBytes)} / ${formatBytes(task.totalBytes)}", style = MaterialTheme.typography.bodySmall)
            val stateText = when (task.state) {
                "queued" -> "排队中"
                "downloading" -> "正在下载"
                "paused" -> "已暂停"
                "waiting_wifi" -> "等待 Wi-Fi"
                "failed" -> "失败：${task.error}"
                else -> task.state
            }
            Text(stateText, color = if (task.state == "failed") MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurfaceVariant, style = MaterialTheme.typography.bodySmall)
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                if (task.state == "downloading" || task.state == "queued") {
                    OutlinedButton(onClick = { vm.pauseDownload(task.key) }) { Text("暂停") }
                } else {
                    Button(onClick = { vm.retryDownload(task.key) }, enabled = task.state != "downloading") { Text("继续") }
                }
                TextButton(onClick = { vm.cancelDownload(task.key) }) { Text("取消") }
            }
        }
    }
}

@Composable
private fun DownloadedBookCard(book: DownloadedBook, vm: MainViewModel, onBookClick: (DownloadedBook) -> Unit) {
    Card(modifier = Modifier.fillMaxWidth()) {
        Row(modifier = Modifier.fillMaxWidth().padding(14.dp), horizontalArrangement = Arrangement.SpaceBetween) {
            Column(modifier = Modifier.weight(1f)) {
                Text(book.title, style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.SemiBold)
                Text(book.author.ifBlank { "未知作者" }, style = MaterialTheme.typography.bodySmall)
                Text("离线可播放 · ${formatDuration(book.duration)}", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.primary)
            }
            Column {
                Button(onClick = { onBookClick(book) }) { Text("播放") }
                TextButton(onClick = { vm.deleteDownloadedBook(book) }) { Text("删除") }
            }
        }
    }
}

private fun formatBytes(bytes: Long): String = when {
    bytes >= 1024L * 1024 * 1024 -> "%.1f GB".format(bytes / (1024.0 * 1024 * 1024))
    bytes >= 1024L * 1024 -> "%.1f MB".format(bytes / (1024.0 * 1024))
    bytes >= 1024L -> "%.1f KB".format(bytes / 1024.0)
    else -> "$bytes B"
}

private fun formatDuration(seconds: Double): String {
    val hours = (seconds / 3600).toInt()
    val minutes = ((seconds % 3600) / 60).toInt()
    return if (hours > 0) "${hours}小时${minutes}分钟" else "${minutes}分钟"
}

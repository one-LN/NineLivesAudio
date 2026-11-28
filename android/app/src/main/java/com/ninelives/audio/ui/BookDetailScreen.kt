package com.ninelives.audio.ui

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.ArrowBack
import androidx.compose.material.icons.outlined.Download
import androidx.compose.material.icons.outlined.PlayArrow
import androidx.compose.material3.Button
import androidx.compose.material3.FilterChip
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
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
import com.ninelives.audio.data.BookItem
import androidx.compose.material.icons.filled.Favorite
import androidx.compose.material.icons.outlined.FavoriteBorder

/** Full-screen book page, mirroring the desktop BookDetailPage instead of a dialog. */
@Composable
fun BookDetailScreen(
    vm: MainViewModel,
    book: BookItem,
    onBack: () -> Unit,
    onPlay: (BookItem) -> Unit,
    onFilterClick: ((String) -> Unit)? = null
) {
    BackHandler { onBack() }
    LaunchedEffect(book.id) { vm.loadBookDetail(book.id) }
    val extra = vm.detailCache[book.id]
    val genres = (extra?.genres?.takeIf { it.isNotEmpty() } ?: book.genres)
    val tags = (extra?.tags?.takeIf { it.isNotEmpty() } ?: book.tags)
    val seriesNames = (extra?.seriesNames?.takeIf { it.isNotEmpty() } ?: book.seriesNames)
    val chapters = extra?.chapters.orEmpty()
    val tracks = extra?.tracks.orEmpty()
    var detailTab by remember { mutableIntStateOf(0) } // 0=章节, 1=音轨
    val percent = book.progressPercent.coerceIn(0.0, 100.0).toFloat()
    val description = remember(book.description) {
        android.text.Html.fromHtml(book.description, android.text.Html.FROM_HTML_MODE_LEGACY)
            .toString().trim()
    }
    val listened = book.currentTime.coerceAtLeast(0.0)

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 16.dp, vertical = 8.dp)
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            IconButton(onClick = onBack) {
                Icon(
                    Icons.AutoMirrored.Outlined.ArrowBack,
                    contentDescription = t("返回", "Back"),
                    tint = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
            Text(
                t("详情", "Details"),
                style = MaterialTheme.typography.titleMedium,
                fontWeight = FontWeight.SemiBold
            )
            Spacer(modifier = Modifier.weight(1f))
            IconButton(onClick = { vm.toggleFavorite(book) }) {
                Icon(
                    if (vm.isFavorite(book.id)) Icons.Filled.Favorite else Icons.Outlined.FavoriteBorder,
                    contentDescription = t("收藏", "Favorite"),
                    tint = if (vm.isFavorite(book.id)) MaterialTheme.colorScheme.primary
                    else MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
        }
        if (vm.favoriteStatus.isNotBlank()) {
            Text(
                vm.favoriteStatus,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
        }

        Spacer(modifier = Modifier.height(8.dp))
        AsyncImage(
            model = book.coverUrl,
            contentDescription = book.title,
            contentScale = ContentScale.Crop,
            modifier = Modifier
                .align(Alignment.CenterHorizontally)
                .fillMaxWidth(0.62f)
                .aspectRatio(1f)
                .clip(RoundedCornerShape(10.dp))
        )

        Spacer(modifier = Modifier.height(14.dp))
        Text(
            book.title,
            style = MaterialTheme.typography.headlineSmall,
            fontWeight = FontWeight.Bold
        )
        if (book.author.isNotBlank()) {
            Text(
                book.author,
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
        }
        if (book.narrator.isNotBlank()) {
            Text(
                t("演播：${book.narrator}", "Narrator: ${book.narrator}"),
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
        }

        Spacer(modifier = Modifier.height(10.dp))
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(
                t("时长 ${formatDuration(book.duration)}", "${formatDuration(book.duration)} long"),
                style = MaterialTheme.typography.labelMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
            if (book.isDownloaded) {
                Spacer(modifier = Modifier.width(8.dp))
                Text(
                    t("已离线", "Downloaded"),
                    style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.primary
                )
            }
        }

        if (book.hasProgress) {
            Spacer(modifier = Modifier.height(10.dp))
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween
            ) {
                Text(
                    t("已听 ${formatDuration(listened)}", "Listened ${formatDuration(listened)}"),
                    style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
                Text(
                    "${percent.toInt()}%",
                    style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.primary
                )
            }
            Spacer(modifier = Modifier.height(4.dp))
            LinearProgressIndicator(
                progress = { percent / 100f },
                modifier = Modifier
                    .fillMaxWidth()
                    .height(6.dp)
                    .clip(RoundedCornerShape(3.dp))
            )
        }

        Spacer(modifier = Modifier.height(14.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Button(
                onClick = { onPlay(book) },
                modifier = Modifier.weight(1f)
            ) {
                Icon(Icons.Outlined.PlayArrow, contentDescription = null, modifier = Modifier.size(18.dp))
                Spacer(modifier = Modifier.width(6.dp))
                Text(if (book.hasProgress) t("继续收听", "Continue") else t("开始播放", "Play"))
            }
            OutlinedButton(
                onClick = { vm.enqueueDownload(book) },
                enabled = !book.isDownloaded
            ) {
                Icon(Icons.Outlined.Download, contentDescription = null, modifier = Modifier.size(18.dp))
                Spacer(modifier = Modifier.width(6.dp))
                Text(if (book.isDownloaded) t("已离线", "Downloaded") else t("下载", "Download"))
            }
        }

        Spacer(modifier = Modifier.height(18.dp))
        HorizontalDivider()
        if (seriesNames.isNotEmpty() || genres.isNotEmpty() || tags.isNotEmpty()) {
            Spacer(modifier = Modifier.height(12.dp))
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .horizontalScroll(rememberScrollState()),
                horizontalArrangement = Arrangement.spacedBy(6.dp)
            ) {
                seriesNames.forEach { name ->
                    MetaPill(text = t("系列：$name", "Series: $name")) { onFilterClick?.invoke(name) }
                }
                genres.forEach { g ->
                    MetaPill(text = g) { onFilterClick?.invoke(g) }
                }
                tags.forEach { tg ->
                    MetaPill(text = "#$tg") { onFilterClick?.invoke(tg) }
                }
            }
        }
        Spacer(modifier = Modifier.height(12.dp))
        Text(
            t("简介", "Description"),
            style = MaterialTheme.typography.titleSmall,
            fontWeight = FontWeight.SemiBold
        )
        Spacer(modifier = Modifier.height(6.dp))
        Text(
            description.ifBlank { t("暂无简介", "No description") },
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant
        )
        if (chapters.isNotEmpty() || tracks.isNotEmpty()) {
            Spacer(modifier = Modifier.height(16.dp))
            HorizontalDivider()
            Spacer(modifier = Modifier.height(10.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                if (chapters.isNotEmpty()) {
                    FilterChip(
                        selected = detailTab == 0 || tracks.isEmpty(),
                        onClick = { detailTab = 0 },
                        label = { Text(t("章节 (${chapters.size})", "Chapters (${chapters.size})")) }
                    )
                }
                if (tracks.isNotEmpty()) {
                    FilterChip(
                        selected = detailTab == 1 || chapters.isEmpty(),
                        onClick = { detailTab = 1 },
                        label = { Text(t("音轨 (${tracks.size})", "Tracks (${tracks.size})")) }
                    )
                }
            }
            Spacer(modifier = Modifier.height(8.dp))
            val showChapters = (detailTab == 0 && chapters.isNotEmpty()) || tracks.isEmpty()
            if (showChapters) {
                val activeChapterIdx = chapters.indexOfLast { book.currentTime >= it.start }
                chapters.forEachIndexed { idx, ch ->
                    val isCurrent = idx == activeChapterIdx && book.hasProgress
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .clip(RoundedCornerShape(8.dp))
                            .clickable {
                                vm.playBookAtTrack(book, ch.start)
                                onPlay(book)
                            }
                            .padding(horizontal = 8.dp, vertical = 10.dp),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Text(
                            text = ch.title,
                            style = MaterialTheme.typography.bodyMedium,
                            fontWeight = if (isCurrent) FontWeight.SemiBold else FontWeight.Normal,
                            color = if (isCurrent) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurface,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                            modifier = Modifier.weight(1f)
                        )
                        Spacer(modifier = Modifier.width(8.dp))
                        Text(
                            text = formatClock(ch.start),
                            style = MaterialTheme.typography.labelSmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    }
                }
            } else {
                val activeTrackIdx = tracks.indexOfLast { book.currentTime >= it.startOffset }
                tracks.forEachIndexed { idx, tr ->
                    val isCurrent = idx == activeTrackIdx && book.hasProgress
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .clip(RoundedCornerShape(8.dp))
                            .clickable {
                                vm.playBookAtTrack(book, tr.startOffset)
                                onPlay(book)
                            }
                            .padding(horizontal = 8.dp, vertical = 10.dp),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Text(
                            text = t("第 ${idx + 1} 轨", "Track ${idx + 1}"),
                            style = MaterialTheme.typography.bodyMedium,
                            fontWeight = if (isCurrent) FontWeight.SemiBold else FontWeight.Normal,
                            color = if (isCurrent) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurface,
                            modifier = Modifier.weight(1f)
                        )
                        Text(
                            text = "${formatClock(tr.startOffset)} · ${formatClock(tr.duration)}",
                            style = MaterialTheme.typography.labelSmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    }
                }
            }
        }
        Spacer(modifier = Modifier.height(20.dp))
    }
}

@Composable
private fun MetaPill(text: String, onClick: () -> Unit) {
    Surface(
        shape = RoundedCornerShape(16.dp),
        color = MaterialTheme.colorScheme.surfaceVariant,
        modifier = Modifier.clickable { onClick() }
    ) {
        Text(
            text = text,
            style = MaterialTheme.typography.labelSmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.padding(horizontal = 10.dp, vertical = 5.dp)
        )
    }
}

private fun formatClock(sec: Double): String {
    val total = sec.toInt().coerceAtLeast(0)
    val h = total / 3600
    val m = (total % 3600) / 60
    val s = total % 60
    return if (h > 0) "%d:%02d:%02d".format(h, m, s) else "%d:%02d".format(m, s)
}

private fun formatDuration(sec: Double): String {
    val total = sec.toInt().coerceAtLeast(0)
    val h = total / 3600
    val m = (total % 3600) / 60
    return when {
        Lang.isZh && h > 0 -> "${h} 小时 ${m} 分钟"
        Lang.isZh -> "${m} 分钟"
        h > 0 -> "${h}h ${m}m"
        else -> "${m}m"
    }
}

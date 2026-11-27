package com.ninelives.audio.ui

import androidx.compose.foundation.Image
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
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
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Card
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import coil.compose.AsyncImage
import com.ninelives.audio.R
import com.ninelives.audio.data.BookItem
import androidx.compose.material3.FilterChip

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun HomeScreen(vm: MainViewModel, onBookClick: (BookItem) -> Unit) {
    val lives = vm.nineLives
    val totalListeningSec = if (vm.totalListeningSec > 0) vm.totalListeningSec.toLong() else vm.books.sumOf { it.currentTime }.toLong()
    val totalHours = totalListeningSec / 3600
    val totalMins = (totalListeningSec % 3600) / 60

    PullToRefreshBox(
        isRefreshing = vm.isRefreshing,
        onRefresh = { vm.refreshProgressOnly() },
        modifier = Modifier.fillMaxSize()
    ) {
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(16.dp),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            Image(
                painter = painterResource(id = R.drawable.ic_logo),
                contentDescription = "NineLivesAudio",
                modifier = Modifier
                    .size(56.dp)
                    .clip(RoundedCornerShape(12.dp))
            )
            Spacer(modifier = Modifier.height(6.dp))
            Text(
                text = "九命",
                style = MaterialTheme.typography.titleMedium,
                fontWeight = FontWeight.Bold,
                color = MaterialTheme.colorScheme.primary
            )
            if (vm.isConnected) {
                val livesSec = lives.sumOf { it.currentTime }
                Text(
                    text = formatShortDuration(livesSec),
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
            Spacer(modifier = Modifier.height(12.dp))

            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.Center
            ) {
                FilterChip(
                    selected = vm.homeTab == 0,
                    onClick = { vm.updateHomeTab(0) },
                    label = { Text(t("在听", "In progress")) }
                )
                Spacer(modifier = Modifier.width(8.dp))
                FilterChip(
                    selected = vm.homeTab == 1,
                    onClick = {
                        vm.updateHomeTab(1)
                        if (vm.favorites.isEmpty()) vm.loadFavorites()
                    },
                    label = { Text(t("收藏", "Favorites")) }
                )
            }
            Spacer(modifier = Modifier.height(10.dp))

            if (!vm.isConnected) {
                Text(t("请先在「设置」中连接 Audiobookshelf 服务器", "Connect your Audiobookshelf server in Settings first"), color = MaterialTheme.colorScheme.onSurfaceVariant)
            } else if (vm.homeTab == 1 && vm.favorites.isEmpty()) {
                Text(
                    t("还没有收藏，点开一本书后点心形图标", "No favorites yet — open a book and tap the heart"),
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            } else if (vm.homeTab == 0 && lives.isEmpty()) {
                Text(t("暂无在听书籍", "Nothing in progress"), color = MaterialTheme.colorScheme.onSurfaceVariant)
            } else {
                val shown = if (vm.homeTab == 1) vm.favorites else lives
                LazyColumn(
                    modifier = Modifier.fillMaxSize(),
                    verticalArrangement = Arrangement.spacedBy(10.dp)
                ) {
                itemsIndexed(shown, key = { _, b -> b.id }) { index, book ->
                    Card(
                        modifier = Modifier
                            .fillMaxWidth()
                            .clickable { onBookClick(book) }
                    ) {
                        Row(
                            modifier = Modifier.padding(12.dp),
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            AsyncImage(
                                model = book.coverUrl,
                                contentDescription = book.title,
                                contentScale = ContentScale.Crop,
                                modifier = Modifier
                                    .size(64.dp)
                                    .clip(RoundedCornerShape(8.dp))
                                    .border(1.dp, MaterialTheme.colorScheme.primary.copy(alpha = 0.45f), RoundedCornerShape(8.dp))
                            )
                            Spacer(modifier = Modifier.width(12.dp))
                            Column(modifier = Modifier.weight(1f)) {
                                Text(
                                    text = book.title,
                                    style = MaterialTheme.typography.titleMedium,
                                    fontWeight = FontWeight.SemiBold,
                                    maxLines = 1,
                                    overflow = TextOverflow.Ellipsis
                                )
                                Spacer(modifier = Modifier.height(4.dp))
                                Text(
                                    text = buildAnnotatedString {
                                        withStyle(SpanStyle(color = MaterialTheme.colorScheme.primary)) {
                                            append(t("生命 ${toRoman(index + 1)}  ·  ${bookWeight(book.duration)}", "Life ${toRoman(index + 1)}  ·  ${bookWeight(book.duration)}"))
                                        }
                                        withStyle(SpanStyle(color = MaterialTheme.colorScheme.onSurfaceVariant)) {
                                            append("  ·  ${formatRelativeTime(book.lastUpdate)}")
                                        }
                                    },
                                    style = MaterialTheme.typography.labelSmall,
                                    maxLines = 1,
                                    overflow = TextOverflow.Ellipsis
                                )
                            }
                            Spacer(modifier = Modifier.width(10.dp))
                            Column(
                                horizontalAlignment = Alignment.CenterHorizontally
                            ) {
                                Box(
                                    contentAlignment = Alignment.Center,
                                    modifier = Modifier.size(36.dp)
                                ) {
                                    CircularProgressIndicator(
                                        progress = { (book.progressPercent / 100.0).toFloat().coerceIn(0f, 1f) },
                                        strokeWidth = 3.dp,
                                        modifier = Modifier.fillMaxSize()
                                    )
                                }
                                Spacer(modifier = Modifier.height(3.dp))
                                Text(
                                    text = formatShortDuration(book.currentTime),
                                    style = MaterialTheme.typography.labelSmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant
                                )
                            }
                        }
                    }
                }
            }
        }
    }
}
}

private fun toRoman(n: Int): String = when (n) {
    1 -> "I"; 2 -> "II"; 3 -> "III"; 4 -> "IV"; 5 -> "V"
    6 -> "VI"; 7 -> "VII"; 8 -> "VIII"; 9 -> "IX"
    else -> n.toString()
}

private fun bookWeight(durationSec: Double): String {
    val hours = durationSec / 3600.0
    return when {
        hours < 4.0 -> "LIGHT"
        hours < 15.0 -> "MEDIUM"
        else -> "HEAVY"
    }
}

private fun formatShortDuration(sec: Double): String {
    val totalMin = (sec / 60.0).toInt()
    if (totalMin < 1) return "< 1m"
    val h = totalMin / 60
    val m = totalMin % 60
    return if (h > 0) "${h}h ${m}m" else "${m}m"
}

private fun formatRelativeTime(lastUpdateMs: Long): String {
    if (lastUpdateMs <= 0L) return t("刚刚", "just now")
    val diffSec = ((System.currentTimeMillis() - lastUpdateMs) / 1000L).coerceAtLeast(0L)
    return when {
        diffSec < 60 -> t("刚刚", "just now")
        diffSec < 3600 -> t("${diffSec / 60} 分钟前", "${diffSec / 60} min ago")
        diffSec < 86400 -> t("${diffSec / 3600} 小时前", "${diffSec / 3600} h ago")
        diffSec < 172800 -> t("昨天", "yesterday")
        diffSec < 604800 -> t("${diffSec / 86400} 天前", "${diffSec / 86400} d ago")
        else -> t("${diffSec / 604800} 周前", "${diffSec / 604800} w ago")
    }
}

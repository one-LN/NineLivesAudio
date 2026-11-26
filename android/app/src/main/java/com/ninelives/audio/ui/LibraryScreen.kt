package com.ninelives.audio.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowDropDown
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material.icons.outlined.Search
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.drawWithContent
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.RoundRect
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.PathMeasure
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import coil.compose.AsyncImage
import com.ninelives.audio.data.BookItem

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun LibraryScreen(vm: MainViewModel, onBookClick: (BookItem) -> Unit) {
    var query by remember { mutableStateOf("") }
    var filterMode by remember { mutableStateOf(0) } // 0=全部, 1=在听, 2=未开始, 3=已完成
    var sortMode by remember { mutableStateOf(0) }   // 0=书名, 1=最近收听, 2=时长, 3=作者
    var sortMenuExpanded by remember { mutableStateOf(false) }
    var libMenuExpanded by remember { mutableStateOf(false) }

    val filtered = remember(vm.books, query, filterMode, sortMode) {
        val searched = if (query.isBlank()) vm.books else vm.books.filter {
            it.title.contains(query, ignoreCase = true) || it.author.contains(query, ignoreCase = true)
        }
        val byStatus = when (filterMode) {
            1 -> searched.filter { it.hasProgress && !it.isFinished }
            2 -> searched.filter { !it.hasProgress && !it.isFinished }
            3 -> searched.filter { it.isFinished }
            else -> searched
        }
        when (sortMode) {
            1 -> byStatus.sortedByDescending { it.lastUpdate }
            2 -> byStatus.sortedByDescending { it.duration }
            3 -> byStatus.sortedBy { it.author }
            else -> byStatus.sortedBy { it.title }
        }
    }

    var selectedBook by remember { mutableStateOf<BookItem?>(null) }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .padding(horizontal = 12.dp, vertical = 8.dp)
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.SpaceBetween
        ) {
            Box {
                Row(
                    verticalAlignment = Alignment.CenterVertically,
                    modifier = Modifier.clickable { libMenuExpanded = true }
                ) {
                    Text(
                        text = vm.selectedLibrary?.name ?: t("书库", "Library"),
                        style = MaterialTheme.typography.headlineSmall,
                        fontWeight = FontWeight.Bold
                    )
                    Icon(
                        Icons.Default.ArrowDropDown,
                        contentDescription = t("切换书库", "Switch library"),
                        tint = MaterialTheme.colorScheme.onSurfaceVariant,
                        modifier = Modifier.size(22.dp)
                    )
                }
                DropdownMenu(
                    expanded = libMenuExpanded,
                    onDismissRequest = { libMenuExpanded = false }
                ) {
                    vm.libraries.forEach { lib ->
                        DropdownMenuItem(
                            text = {
                                Text(
                                    (if (lib.id == vm.selectedLibrary?.id) "✓  " else "    ") + lib.name
                                )
                            },
                            onClick = {
                                vm.selectLibrary(lib)
                                libMenuExpanded = false
                            }
                        )
                    }
                }
            }
            IconButton(onClick = { vm.refreshAll() }) {
                Icon(Icons.Default.Refresh, contentDescription = t("刷新", "Refresh"))
            }
        }

        OutlinedTextField(
            value = query,
            onValueChange = { query = it },
            placeholder = { Text(t("搜索书籍...", "Search books..."), style = MaterialTheme.typography.bodySmall) },
            singleLine = true,
            trailingIcon = {
                Icon(
                    Icons.Outlined.Search,
                    contentDescription = t("搜索", "Search"),
                    tint = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.size(18.dp)
                )
            },
            shape = RoundedCornerShape(8.dp),
            colors = OutlinedTextFieldDefaults.colors(
                focusedBorderColor = Color(0xFF4FC3F7),
                unfocusedBorderColor = MaterialTheme.colorScheme.outline.copy(alpha = 0.35f),
                focusedContainerColor = MaterialTheme.colorScheme.surface,
                unfocusedContainerColor = MaterialTheme.colorScheme.surface
            ),
            modifier = Modifier
                .fillMaxWidth()
                .height(44.dp)
        )
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .horizontalScroll(rememberScrollState()),
            horizontalArrangement = Arrangement.spacedBy(6.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            listOf(
                0 to t("全部", "All"),
                1 to t("在听", "In progress"),
                2 to t("未开始", "Unstarted"),
                3 to t("已完成", "Finished")
            ).forEach { (mode, label) ->
                FilterChip(
                    selected = filterMode == mode,
                    onClick = { filterMode = mode },
                    label = { Text(label, style = MaterialTheme.typography.labelMedium) }
                )
            }
            Box {
                TextButton(onClick = { sortMenuExpanded = true }) {
                    val sortLabel = listOf(
                        t("按书名", "Title"),
                        t("最近收听", "Recently played"),
                        t("按时长", "Duration"),
                        t("按作者", "Author")
                    )[sortMode]
                    Text(sortLabel + "  ▾", style = MaterialTheme.typography.labelMedium)
                }
                DropdownMenu(
                    expanded = sortMenuExpanded,
                    onDismissRequest = { sortMenuExpanded = false }
                ) {
                    listOf(
                        0 to t("按书名", "Title"),
                        1 to t("最近收听", "Recently played"),
                        2 to t("按时长", "Duration"),
                        3 to t("按作者", "Author")
                    ).forEach { (mode, label) ->
                        DropdownMenuItem(
                            text = { Text((if (sortMode == mode) "✓  " else "    ") + label) },
                            onClick = {
                                sortMode = mode
                                sortMenuExpanded = false
                            }
                        )
                    }
                }
            }
        }
        Spacer(modifier = Modifier.height(4.dp))

        if (vm.books.isEmpty()) {
            Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                Column(
                    horizontalAlignment = Alignment.CenterHorizontally,
                    verticalArrangement = Arrangement.spacedBy(12.dp)
                ) {
                    if (vm.isSyncing) {
                        CircularProgressIndicator()
                        Text(
                            t("正在同步书库…", "Syncing library..."),
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    } else {
                        Text(
                            vm.statusMessage.ifBlank { t("书库是空的", "This library is empty") },
                            style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                        Button(onClick = { vm.refreshAll(force = true) }) {
                            Text(t("重新同步", "Sync again"))
                        }
                    }
                }
            }
        } else {
            PullToRefreshBox(
                isRefreshing = vm.isSyncing,
                onRefresh = { vm.refreshAll() },
                modifier = Modifier.fillMaxSize()
            ) {
                LazyVerticalGrid(
                    columns = GridCells.Fixed(3),
                    contentPadding = PaddingValues(bottom = 16.dp),
                    horizontalArrangement = Arrangement.spacedBy(6.dp),
                    verticalArrangement = Arrangement.spacedBy(8.dp)
                ) {
                items(filtered, key = { it.id }) { book ->
                    val prog = (book.progressPercent / 100.0).toFloat().coerceIn(0f, 1f)
                    val ringColor = MaterialTheme.colorScheme.primary
                    Card(
                        modifier = Modifier
                            .fillMaxWidth()
                            .drawWithContent {
                                drawContent()
                                if (book.hasProgress && prog > 0f) {
                                    val stroke = 1.5.dp.toPx()
                                    val half = stroke / 2f
                                    val radius = 12.dp.toPx()
                                    val fullPath = Path().apply {
                                        addRoundRect(
                                            RoundRect(
                                                left = half,
                                                top = half,
                                                right = size.width - half,
                                                bottom = size.height - half,
                                                cornerRadius = CornerRadius(radius, radius)
                                            )
                                        )
                                    }
                                    val measure = PathMeasure().apply { setPath(fullPath, false) }
                                    val segPath = Path()
                                    measure.getSegment(0f, measure.length * prog, segPath, true)
                                    drawPath(
                                        path = segPath,
                                        color = ringColor,
                                        style = Stroke(width = stroke, cap = StrokeCap.Round)
                                    )
                                }
                            }
                            .clickable { selectedBook = book }
                    ) {
                        Column(modifier = Modifier.padding(6.dp)) {
                            AsyncImage(
                                model = book.coverUrl,
                                contentDescription = book.title,
                                contentScale = ContentScale.Crop,
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .aspectRatio(1f)
                                    .clip(RoundedCornerShape(6.dp))
                            )
                            Spacer(modifier = Modifier.height(4.dp))
                            Text(
                                text = book.title,
                                style = MaterialTheme.typography.bodySmall,
                                fontWeight = FontWeight.SemiBold,
                                minLines = 1,
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis
                            )
                            if (book.isDownloaded) {
                                Text(
                                    text = t("离线", "Offline"),
                                    style = MaterialTheme.typography.labelSmall,
                                    color = MaterialTheme.colorScheme.primary
                                )
                            }
                        }
                    }
                }
            }
            }
        }
    }

    selectedBook?.let { book ->
        AlertDialog(
            onDismissRequest = { selectedBook = null },
            title = {
                Text(
                    text = book.title,
                    maxLines = 2,
                    overflow = TextOverflow.Ellipsis
                )
            },
            text = {
                Column(
                    modifier = Modifier
                        .height(300.dp)
                        .verticalScroll(rememberScrollState()),
                    verticalArrangement = Arrangement.spacedBy(8.dp)
                ) {
                    if (book.author.isNotBlank()) {
                        Text(t("作者：${book.author}", "Author: ${book.author}"), style = MaterialTheme.typography.bodySmall)
                    }
                    if (book.narrator.isNotBlank()) {
                        Text(t("演播：${book.narrator}", "Narrator: ${book.narrator}"), style = MaterialTheme.typography.bodySmall)
                    }
                    if (book.hasProgress) {
                        Text(
                            t("当前进度：${book.progressPercent}%", "Progress: ${book.progressPercent}%"),
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.primary
                        )
                    }
                    Text(
                        text = book.description.ifBlank { t("暂无简介", "No description") },
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }
            },
            confirmButton = {
                Button(onClick = {
                    val b = book
                    selectedBook = null
                    onBookClick(b)
                }) {
                    Text(if (book.hasProgress) t("继续收听", "Continue") else t("开始播放", "Play"))
                }
            },
            dismissButton = {
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    TextButton(onClick = { selectedBook = null }) {
                        Text(t("关闭", "Close"))
                    }
                    TextButton(
                        onClick = {
                            vm.enqueueDownload(book)
                            selectedBook = null
                        },
                        enabled = !book.isDownloaded
                    ) {
                        Text(if (book.isDownloaded) t("已离线", "Downloaded") else t("下载离线", "Download"))
                    }
                }
            }
        )
    }
}

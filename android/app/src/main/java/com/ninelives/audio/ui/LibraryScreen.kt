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
fun LibraryScreen(
    vm: MainViewModel,
    onBookClick: (BookItem) -> Unit,
    onOpenPlayer: () -> Unit = {}
) {
    var query by remember { mutableStateOf("") }
    var filterMode by remember { mutableStateOf(0) } // 0=全部, 1=在听, 2=未开始, 3=已完成
    var selectedGroup by remember { mutableStateOf("") } // 动态抓取的系列/分类/标签
    var groupMenuExpanded by remember { mutableStateOf(false) }
    var sortMenuExpanded by remember { mutableStateOf(false) }
    var libMenuExpanded by remember { mutableStateOf(false) }
    val sortMode = vm.librarySortMode

    // 从服务器 filterdata + 当前书库书籍合并出真实存在的分类/系列/标签，避免空项
    val serverFacets = remember(vm.libraryFilterData, vm.books) {
        val series = (vm.libraryFilterData.series + vm.books.flatMap { it.seriesNames }).distinct().sorted()
        val genres = (vm.libraryFilterData.genres + vm.books.flatMap { it.genres }).distinct().sorted()
        val tags = (vm.libraryFilterData.tags + vm.books.flatMap { it.tags }).distinct().sorted()
        series.map { "S:$it" to t("系列 · $it", "Series · $it") } +
            genres.map { "G:$it" to t("分类 · $it", "Genre · $it") } +
            tags.map { "T:$it" to "#$it" }
    }

    val filtered = remember(vm.books, query, filterMode, sortMode, selectedGroup) {
        val searched = if (query.isBlank()) vm.books else vm.books.filter {
            it.title.contains(query, ignoreCase = true) ||
                it.author.contains(query, ignoreCase = true) ||
                it.seriesNames.any { s -> s.contains(query, ignoreCase = true) } ||
                it.genres.any { g -> g.contains(query, ignoreCase = true) } ||
                it.tags.any { tg -> tg.contains(query, ignoreCase = true) }
        }
        val byFacet = when {
            selectedGroup.startsWith("S:") -> {
                val target = selectedGroup.removePrefix("S:")
                searched.filter { target in it.seriesNames }
            }
            selectedGroup.startsWith("G:") -> {
                val target = selectedGroup.removePrefix("G:")
                searched.filter { target in it.genres }
            }
            selectedGroup.startsWith("T:") -> {
                val target = selectedGroup.removePrefix("T:")
                searched.filter { target in it.tags }
            }
            else -> searched
        }
        val byStatus = when (filterMode) {
            1 -> byFacet.filter { it.hasProgress && !it.isFinished }
            2 -> byFacet.filter { !it.hasProgress && !it.isFinished }
            3 -> byFacet.filter { it.isFinished }
            else -> byFacet
        }
        when (sortMode) {
            1 -> byStatus.sortedByDescending { it.lastUpdate }
            2 -> byStatus.sortedByDescending { it.duration }
            3 -> byStatus.sortedBy { it.author }
            4 -> byStatus.sortedByDescending { it.addedAt }
            else -> byStatus.sortedBy { it.title }
        }
    }

    var selectedBook by remember { mutableStateOf<BookItem?>(null) }

    val detailBook = selectedBook
    if (detailBook != null) {
        BookDetailScreen(
            vm = vm,
            book = detailBook,
            onBack = { selectedBook = null },
            onPlay = { picked ->
                selectedBook = null
                onBookClick(picked)
            },
            onPlayAt = { picked, timeSec ->
                selectedBook = null
                vm.playBookAtTrack(picked, timeSec)
                onOpenPlayer()
            },
            onFilterClick = { keyword ->
                selectedBook = null
                query = keyword
            }
        )
        return
    }

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
                focusedBorderColor = MaterialTheme.colorScheme.primary,
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
            if (serverFacets.isNotEmpty()) {
                Box {
                    val activeLabel = serverFacets.firstOrNull { it.first == selectedGroup }?.second
                        ?: t("分类/系列", "Category")
                    FilterChip(
                        selected = selectedGroup.isNotEmpty(),
                        onClick = { groupMenuExpanded = true },
                        label = { Text("$activeLabel  ▾", style = MaterialTheme.typography.labelMedium) }
                    )
                    DropdownMenu(
                        expanded = groupMenuExpanded,
                        onDismissRequest = { groupMenuExpanded = false }
                    ) {
                        DropdownMenuItem(
                            text = { Text((if (selectedGroup.isEmpty()) "✓  " else "    ") + t("全部分类", "All categories")) },
                            onClick = {
                                selectedGroup = ""
                                groupMenuExpanded = false
                            }
                        )
                        serverFacets.forEach { (key, label) ->
                            DropdownMenuItem(
                                text = { Text((if (selectedGroup == key) "✓  " else "    ") + label) },
                                onClick = {
                                    selectedGroup = key
                                    groupMenuExpanded = false
                                }
                            )
                        }
                    }
                }
            }
            Box {
                TextButton(onClick = { sortMenuExpanded = true }) {
                    val sortOptions = listOf(
                        0 to t("按书名", "Title"),
                        1 to t("最近收听", "Recently played"),
                        2 to t("按时长", "Duration"),
                        3 to t("按作者", "Author"),
                        4 to t("最近入库", "Recently added")
                    )
                    val sortLabel = sortOptions.firstOrNull { it.first == sortMode }?.second
                        ?: t("按书名", "Title")
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
                        3 to t("按作者", "Author"),
                        4 to t("最近入库", "Recently added")
                    ).forEach { (mode, label) ->
                        DropdownMenuItem(
                            text = { Text((if (sortMode == mode) "✓  " else "    ") + label) },
                            onClick = {
                                vm.updateLibrarySortMode(mode)
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
                        val message = when {
                            !vm.isConnected -> t(
                                "还没有连接服务器，请到「设置」登录",
                                "No server connected. Sign in from Settings."
                            )
                            vm.statusMessage.isNotBlank() -> vm.statusMessage
                            else -> t("书库是空的", "This library is empty")
                        }
                        Text(
                            message,
                            style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                        if (vm.isConnected) {
                            Button(onClick = { vm.refreshAll(force = true) }) {
                                Text(t("重新同步", "Sync again"))
                            }
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
}

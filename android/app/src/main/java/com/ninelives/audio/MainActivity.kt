package com.ninelives.audio

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.pager.HorizontalPager
import androidx.compose.foundation.pager.rememberPagerState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Surface
import androidx.compose.material3.TextButton
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.rotate
import androidx.compose.ui.draw.scale
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.drawscope.rotate as drawRotate
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import coil.compose.AsyncImage
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Download
import androidx.compose.material.icons.outlined.Home
import androidx.compose.material.icons.outlined.Settings
import androidx.compose.material.icons.outlined.Timer
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import kotlinx.coroutines.launch
import androidx.compose.ui.Modifier
import androidx.lifecycle.viewmodel.compose.viewModel
import com.ninelives.audio.ui.HomeScreen
import com.ninelives.audio.ui.LibraryScreen
import com.ninelives.audio.ui.MainViewModel
import com.ninelives.audio.ui.PlayerScreen
import com.ninelives.audio.ui.SettingsScreen
import com.ninelives.audio.ui.theme.NineLivesTheme

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent {
            val vm: MainViewModel = viewModel()
            val isDark = when (vm.themeMode) {
                "dark" -> true
                "light" -> false
                else -> isSystemInDarkTheme()
            }
            NineLivesTheme(darkTheme = isDark) {
                NineLivesApp(vm)
            }
        }
    }
}

@Composable
fun NineLivesApp(vm: MainViewModel = viewModel()) {
    val pagerState = rememberPagerState(
        initialPage = if (vm.isConnected) 0 else 5,
        pageCount = { 6 }
    )
    val scope = rememberCoroutineScope()
    LaunchedEffect(pagerState.currentPage) {
        if (pagerState.currentPage == 0) {
            vm.refreshProgressOnly()
        }
    }
    val tabs = listOf(
        "九命" to Icons.Outlined.Home,
        "书库" to Icons.Outlined.Home,
        "播放" to Icons.Outlined.Home,
        "下载" to Icons.Outlined.Download,
        "统计" to Icons.Outlined.Timer,
        "设置" to Icons.Outlined.Settings
    )

    Scaffold(
        bottomBar = {
            Column {
                val sess = vm.currentSession
                if (sess != null && pagerState.currentPage != 2) {
                    Surface(
                        tonalElevation = 3.dp,
                        modifier = Modifier
                            .fillMaxWidth()
                            .clickable { scope.launch { pagerState.animateScrollToPage(2) } }
                    ) {
                        Row(
                            modifier = Modifier.padding(horizontal = 12.dp, vertical = 8.dp),
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            AsyncImage(
                                model = sess.coverUrl,
                                contentDescription = sess.title,
                                contentScale = ContentScale.Crop,
                                modifier = Modifier
                                    .size(40.dp)
                                    .clip(RoundedCornerShape(6.dp))
                            )
                            Spacer(modifier = Modifier.width(10.dp))
                            Column(modifier = Modifier.weight(1f)) {
                                Text(
                                    text = sess.title,
                                    style = MaterialTheme.typography.bodyMedium,
                                    fontWeight = FontWeight.SemiBold,
                                    maxLines = 1,
                                    overflow = TextOverflow.Ellipsis
                                )
                                val ch = sess.chapters.lastOrNull { vm.currentPositionSec >= it.start }
                                if (ch != null) {
                                    Text(
                                        text = ch.title,
                                        style = MaterialTheme.typography.bodySmall,
                                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                                        maxLines = 1,
                                        overflow = TextOverflow.Ellipsis
                                    )
                                }
                            }
                            TextButton(onClick = { vm.togglePlayPause() }) {
                                Text(if (vm.isPlaying) "暂停" else "播放")
                            }
                        }
                    }
                }
                Surface(
                    tonalElevation = 2.dp,
                    modifier = Modifier
                        .fillMaxWidth()
                        .navigationBarsPadding()
                ) {
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .height(50.dp),
                        horizontalArrangement = Arrangement.SpaceEvenly,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        tabs.forEachIndexed { index, (label, icon) ->
                            val selected = pagerState.currentPage == index
                            val scale by animateFloatAsState(
                                targetValue = if (selected) 1.25f else 1.0f,
                                label = "navScale"
                            )
                            val gearRotation by animateFloatAsState(
                                targetValue = if (index == 5 && selected) 180f else 0f,
                                animationSpec = tween(450),
                                label = "gearRotate"
                            )
                            Box(
                                contentAlignment = Alignment.Center,
                                modifier = Modifier
                                    .weight(1f)
                                    .height(50.dp)
                                    .clickable { scope.launch { pagerState.animateScrollToPage(index) } }
                            ) {
                                val tint = if (selected) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurfaceVariant
                                if (index == 1) {
                                    Canvas(
                                        modifier = Modifier
                                            .size(22.dp)
                                            .scale(scale)
                                    ) {
                                        val s = 1.5.dp.toPx()
                                        val w = size.width
                                        val h = size.height
                                        drawRoundRect(
                                            color = tint,
                                            topLeft = Offset(w * 0.06f, h * 0.14f),
                                            size = Size(w * 0.18f, h * 0.72f),
                                            cornerRadius = CornerRadius(1.5.dp.toPx()),
                                            style = Stroke(width = s)
                                        )
                                        drawRoundRect(
                                            color = tint,
                                            topLeft = Offset(w * 0.34f, h * 0.14f),
                                            size = Size(w * 0.18f, h * 0.72f),
                                            cornerRadius = CornerRadius(1.5.dp.toPx()),
                                            style = Stroke(width = s)
                                        )
                                        drawRotate(
                                            degrees = -16f,
                                            pivot = Offset(w * 0.82f, h * 0.86f)
                                        ) {
                                            drawRoundRect(
                                                color = tint,
                                                topLeft = Offset(w * 0.72f, h * 0.24f),
                                                size = Size(w * 0.18f, h * 0.62f),
                                                cornerRadius = CornerRadius(1.5.dp.toPx()),
                                                style = Stroke(width = s)
                                            )
                                        }
                                    }
                                } else if (index == 2) {
                                    Canvas(
                                        modifier = Modifier
                                            .size(20.dp)
                                            .scale(scale)
                                    ) {
                                        val w = size.width
                                        val h = size.height
                                        val path = androidx.compose.ui.graphics.Path().apply {
                                            moveTo(w * 0.20f, h * 0.12f)
                                            lineTo(w * 0.88f, h * 0.50f)
                                            lineTo(w * 0.20f, h * 0.88f)
                                            close()
                                        }
                                        drawPath(
                                            path = path,
                                            color = tint,
                                            style = Stroke(
                                                width = 2.dp.toPx(),
                                                join = androidx.compose.ui.graphics.StrokeJoin.Round
                                            )
                                        )
                                    }
                                } else {
                                    Icon(
                                        icon,
                                        contentDescription = label,
                                        tint = tint,
                                        modifier = Modifier
                                            .size(22.dp)
                                            .scale(scale)
                                            .rotate(gearRotation)
                                    )
                                }
                            }
                        }
                    }
                }
            }
        }
    ) { innerPadding ->
        HorizontalPager(
            state = pagerState,
            modifier = Modifier
                .fillMaxSize()
                .padding(innerPadding)
        ) { page ->
            when (page) {
                0 -> HomeScreen(vm, onBookClick = { book ->
                    vm.playBook(book)
                    scope.launch { pagerState.animateScrollToPage(2) }
                })
                1 -> LibraryScreen(vm, onBookClick = { book ->
                    vm.playBook(book)
                    scope.launch { pagerState.animateScrollToPage(2) }
                })
                2 -> PlayerScreen(vm)
                3 -> SettingsScreen(vm)
            }
        }
    }
}

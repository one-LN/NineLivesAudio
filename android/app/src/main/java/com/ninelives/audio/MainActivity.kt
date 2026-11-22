package com.ninelives.audio

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
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
import androidx.compose.ui.draw.scale
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import coil.compose.AsyncImage
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Home
import androidx.compose.material.icons.filled.Menu
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material.icons.filled.Settings
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
            NineLivesTheme {
                NineLivesApp()
            }
        }
    }
}

@Composable
fun NineLivesApp(vm: MainViewModel = viewModel()) {
    val pagerState = rememberPagerState(
        initialPage = if (vm.isConnected) 0 else 3,
        pageCount = { 4 }
    )
    val scope = rememberCoroutineScope()
    LaunchedEffect(pagerState.currentPage) {
        if (pagerState.currentPage == 0) {
            vm.refreshProgressOnly()
        }
    }
    val tabs = listOf(
        "九命" to Icons.Default.Home,
        "书库" to Icons.Default.Menu,
        "播放" to Icons.Default.PlayArrow,
        "设置" to Icons.Default.Settings
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
                NavigationBar {
                    tabs.forEachIndexed { index, (label, icon) ->
                        val selected = pagerState.currentPage == index
                        val scale by animateFloatAsState(
                            targetValue = if (selected) 1.22f else 1.0f,
                            label = "navScale"
                        )
                        NavigationBarItem(
                            selected = selected,
                            onClick = { scope.launch { pagerState.animateScrollToPage(index) } },
                            icon = {
                                Icon(
                                    icon,
                                    contentDescription = label,
                                    modifier = Modifier.scale(scale)
                                )
                            }
                        )
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

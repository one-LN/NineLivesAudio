package com.ninelives.audio

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Home
import androidx.compose.material.icons.filled.Menu
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.Icon
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
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
    var selectedTab by rememberSaveable { mutableIntStateOf(if (vm.isConnected) 0 else 3) }
    val tabs = listOf(
        "九命" to Icons.Default.Home,
        "书库" to Icons.Default.Menu,
        "播放" to Icons.Default.PlayArrow,
        "设置" to Icons.Default.Settings
    )

    Scaffold(
        bottomBar = {
            NavigationBar {
                tabs.forEachIndexed { index, (label, icon) ->
                    NavigationBarItem(
                        selected = selectedTab == index,
                        onClick = { selectedTab = index },
                        icon = { Icon(icon, contentDescription = label) },
                        label = { Text(label) }
                    )
                }
            }
        }
    ) { innerPadding ->
        Box(
            modifier = Modifier
                .fillMaxSize()
                .padding(innerPadding)
        ) {
            when (selectedTab) {
                0 -> HomeScreen(vm, onBookClick = { book ->
                    vm.playBook(book)
                    selectedTab = 2
                })
                1 -> LibraryScreen(vm, onBookClick = { book ->
                    vm.playBook(book)
                    selectedTab = 2
                })
                2 -> PlayerScreen(vm)
                3 -> SettingsScreen(vm)
            }
        }
    }
}

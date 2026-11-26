package com.ninelives.audio.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp

@Composable
fun SettingsScreen(vm: MainViewModel) {
    var isAdding by remember { mutableStateOf(!vm.isConnected) }
    var serverName by remember { mutableStateOf("") }
    var serverUrl by remember { mutableStateOf(vm.activeUrl) }
    var username by remember { mutableStateOf(vm.activeUsername) }
    var password by remember { mutableStateOf("") }
    var expanded by remember { mutableStateOf(false) }

    val currentSaved = vm.savedServers.firstOrNull {
        it.url == vm.activeUrl && it.username == vm.activeUsername
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp)
    ) {
        Text("设置", style = MaterialTheme.typography.headlineMedium, fontWeight = FontWeight.Bold)

        Card(modifier = Modifier.fillMaxWidth()) {
            Column(modifier = Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                Text("服务器连接", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)

                if (vm.savedServers.isNotEmpty()) {
                    OutlinedButton(
                        onClick = { expanded = !expanded },
                        modifier = Modifier.fillMaxWidth()
                    ) {
                        Text(currentSaved?.displayName ?: "选择已保存的服务器")
                    }
                    if (expanded) {
                        Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                            vm.savedServers.forEach { s ->
                                OutlinedButton(
                                    onClick = {
                                        expanded = false
                                        isAdding = false
                                        vm.switchServer(s)
                                    },
                                    modifier = Modifier.fillMaxWidth()
                                ) {
                                    Text(s.displayName)
                                }
                            }
                        }
                    }
                }

                if (vm.isConnected && !isAdding) {
                    Text("已连接到 ${vm.activeUrl} (${vm.activeUsername})", color = MaterialTheme.colorScheme.primary)
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        OutlinedButton(onClick = { vm.disconnect() }) { Text("断开") }
                        Button(onClick = {
                            isAdding = true
                            serverName = ""
                            serverUrl = ""
                            username = ""
                            password = ""
                        }) { Text("添加服务器") }
                        if (currentSaved != null) {
                            OutlinedButton(onClick = { vm.deleteServer(currentSaved) }) { Text("删除") }
                        }
                    }
                } else {
                    OutlinedTextField(
                        value = serverName,
                        onValueChange = { serverName = it },
                        label = { Text("服务器名称（可选）") },
                        placeholder = { Text("例如：家里 NAS") },
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth()
                    )
                    OutlinedTextField(
                        value = serverUrl,
                        onValueChange = { serverUrl = it },
                        label = { Text("服务器地址") },
                        placeholder = { Text("https://example.com") },
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth()
                    )
                    OutlinedTextField(
                        value = username,
                        onValueChange = { username = it },
                        label = { Text("用户名") },
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth()
                    )
                    OutlinedTextField(
                        value = password,
                        onValueChange = { password = it },
                        label = { Text("密码") },
                        visualTransformation = PasswordVisualTransformation(),
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth()
                    )
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        Button(onClick = {
                            vm.loginAndConnect(serverName, serverUrl, username, password)
                            isAdding = false
                            password = ""
                        }) { Text("连接") }
                        if (vm.isConnected) {
                            OutlinedButton(onClick = { isAdding = false }) { Text("取消") }
                        }
                    }
                }

                if (vm.statusMessage.isNotBlank()) {
                    Spacer(modifier = Modifier.height(4.dp))
                    Text(vm.statusMessage, style = MaterialTheme.typography.bodySmall)
                }
            }
        }

        Card(modifier = Modifier.fillMaxWidth()) {
            Column(
                modifier = Modifier.padding(16.dp),
                verticalArrangement = Arrangement.spacedBy(10.dp)
            ) {
                Text("外观", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    listOf("system" to "跟随系统", "dark" to "深色", "light" to "浅色").forEach { (mode, label) ->
                        if (vm.themeMode == mode) {
                            Button(onClick = { vm.themeMode = mode }) { Text(label) }
                        } else {
                            OutlinedButton(onClick = { vm.themeMode = mode }) { Text(label) }
                        }
                    }
                }
            }
        }

        Card(modifier = Modifier.fillMaxWidth()) {
            Column(
                modifier = Modifier.padding(16.dp),
                verticalArrangement = Arrangement.spacedBy(10.dp)
            ) {
                Text("下载与同步", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text("仅在 Wi-Fi 下下载", style = MaterialTheme.typography.bodyMedium)
                    Switch(checked = vm.wifiOnlyDownload, onCheckedChange = { vm.updateWifiOnlyDownload(it) })
                }
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text("自动同步播放进度", style = MaterialTheme.typography.bodyMedium)
                    Switch(checked = vm.autoSyncEnabled, onCheckedChange = { vm.autoSyncEnabled = it })
                }
            }
        }

        Card(modifier = Modifier.fillMaxWidth()) {
            Column(
                modifier = Modifier.padding(16.dp),
                verticalArrangement = Arrangement.spacedBy(10.dp)
            ) {
                Text("数据", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
                OutlinedButton(
                    onClick = { vm.refreshAll() },
                    modifier = Modifier.fillMaxWidth()
                ) {
                    Text("立即同步服务器数据")
                }
            }
        }

        Card(modifier = Modifier.fillMaxWidth()) {
            Column(
                modifier = Modifier.padding(16.dp),
                verticalArrangement = Arrangement.spacedBy(10.dp)
            ) {
                Text("关于与更新", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
                Text(
                    "当前版本 ${vm.currentVersion}",
                    style = MaterialTheme.typography.bodyMedium
                )
                if (vm.updateStatus.isNotBlank()) {
                    Text(
                        vm.updateStatus,
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }
                if (vm.isDownloadingUpdate) {
                    LinearProgressIndicator(
                        progress = { vm.updateProgress / 100f },
                        modifier = Modifier.fillMaxWidth()
                    )
                }
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    OutlinedButton(
                        onClick = { vm.checkForUpdate() },
                        enabled = !vm.isCheckingUpdate && !vm.isDownloadingUpdate
                    ) { Text(if (vm.isCheckingUpdate) "检查中…" else "检查更新") }

                    val pending = vm.pendingUpdate
                    if (pending != null) {
                        Button(
                            onClick = { vm.installUpdate() },
                            enabled = !vm.isDownloadingUpdate
                        ) {
                            Text(
                                when {
                                    vm.isDownloadingUpdate -> "${vm.updateProgress}%"
                                    else -> "一键升级到 ${pending.versionName}"
                                }
                            )
                        }
                    }
                }
            }
        }
    }
}

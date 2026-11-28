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
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
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
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.style.TextOverflow
import com.ninelives.audio.R

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
        Text(t("设置", "Settings"), style = MaterialTheme.typography.headlineMedium, fontWeight = FontWeight.Bold)

        Card(modifier = Modifier.fillMaxWidth()) {
            Column(modifier = Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                Text(t("服务器连接", "Server"), style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)

                if (vm.savedServers.isNotEmpty()) {
                    OutlinedButton(
                        onClick = { expanded = !expanded },
                        modifier = Modifier.fillMaxWidth()
                    ) {
                        Text(currentSaved?.displayName ?: t("选择已保存的服务器", "Saved servers"))
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
                    Text(t("已连接到 ${vm.activeUrl} (${vm.activeUsername})", "Connected to ${vm.activeUrl} (${vm.activeUsername})"), color = MaterialTheme.colorScheme.primary)
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        OutlinedButton(onClick = { vm.disconnect() }) { Text(t("断开", "Disconnect")) }
                        Button(onClick = {
                            isAdding = true
                            serverName = ""
                            serverUrl = ""
                            username = ""
                            password = ""
                        }) { Text(t("添加服务器", "Add server")) }
                        if (currentSaved != null) {
                            OutlinedButton(onClick = { vm.deleteServer(currentSaved) }) { Text(t("删除", "Delete")) }
                        }
                    }
                } else {
                    OutlinedTextField(
                        value = serverName,
                        onValueChange = { serverName = it },
                        label = { Text(t("服务器名称（可选）", "Server name (optional)")) },
                        placeholder = { Text(t("例如：家里 NAS", "e.g. Home NAS")) },
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth()
                    )
                    OutlinedTextField(
                        value = serverUrl,
                        onValueChange = { serverUrl = it },
                        label = { Text(t("服务器地址", "Server address")) },
                        placeholder = { Text("https://example.com") },
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth()
                    )
                    OutlinedTextField(
                        value = username,
                        onValueChange = { username = it },
                        label = { Text(t("用户名", "Username")) },
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth()
                    )
                    OutlinedTextField(
                        value = password,
                        onValueChange = { password = it },
                        label = { Text(t("密码", "Password")) },
                        visualTransformation = PasswordVisualTransformation(),
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth()
                    )
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        Button(onClick = {
                            vm.loginAndConnect(serverName, serverUrl, username, password)
                            isAdding = false
                            password = ""
                        }) { Text(t("连接", "Connect")) }
                        if (vm.isConnected) {
                            OutlinedButton(onClick = { isAdding = false }) { Text(t("取消", "Cancel")) }
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
                Text(t("外观", "Appearance"), style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    listOf("system" to t("跟随系统", "System"), "dark" to t("深色", "Dark"), "light" to t("浅色", "Light")).forEach { (mode, label) ->
                        if (vm.themeMode == mode) {
                            Button(onClick = { vm.updateThemeMode(mode) }) { Text(label) }
                        } else {
                            OutlinedButton(onClick = { vm.updateThemeMode(mode) }) { Text(label) }
                        }
                    }
                }
                Spacer(modifier = Modifier.height(2.dp))
                Text(t("语言", "Language"), style = MaterialTheme.typography.bodyMedium)
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    listOf("zh" to "中文", "en" to "English").forEach { (code, label) ->
                        if (Lang.code == code) {
                            Button(onClick = { vm.updateLanguage(code) }) { Text(label) }
                        } else {
                            OutlinedButton(onClick = { vm.updateLanguage(code) }) { Text(label) }
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
                Text(t("应用图标", "App icon"), style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
                Text(
                    t("选择桌面显示的图标，部分桌面需要几秒刷新", "Choose the launcher icon; some launchers take a few seconds to refresh"),
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
                Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    listOf(
                        Triple("default", t("默认", "Default"), R.drawable.icon_preview_default),
                        Triple("forest", t("森林", "Forest"), R.drawable.icon_preview_forest)
                    ).forEach { (id, label, res) ->
                        val selected = vm.appIcon == id
                        Column(
                            horizontalAlignment = Alignment.CenterHorizontally,
                            modifier = Modifier
                                .clickable { vm.updateAppIcon(id) }
                                .border(
                                    width = if (selected) 2.dp else 1.dp,
                                    color = if (selected) MaterialTheme.colorScheme.primary
                                    else MaterialTheme.colorScheme.outlineVariant,
                                    shape = RoundedCornerShape(14.dp)
                                )
                                .padding(8.dp)
                        ) {
                            Image(
                                painter = painterResource(res),
                                contentDescription = label,
                                modifier = Modifier
                                    .size(64.dp)
                                    .clip(RoundedCornerShape(14.dp))
                            )
                            Spacer(modifier = Modifier.height(6.dp))
                            Text(
                                if (selected) "✓ $label" else label,
                                style = MaterialTheme.typography.labelMedium,
                                color = if (selected) MaterialTheme.colorScheme.primary
                                else MaterialTheme.colorScheme.onSurfaceVariant
                            )
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
                Text(t("下载与同步", "Downloads & sync"), style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text(t("仅在 Wi-Fi 下下载", "Download on Wi-Fi only"), style = MaterialTheme.typography.bodyMedium)
                    Switch(checked = vm.wifiOnlyDownload, onCheckedChange = { vm.updateWifiOnlyDownload(it) })
                }
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text(t("自动同步播放进度", "Auto-sync progress"), style = MaterialTheme.typography.bodyMedium)
                    Switch(checked = vm.autoSyncEnabled, onCheckedChange = { vm.updateAutoSync(it) })
                }
            }
        }

        Card(modifier = Modifier.fillMaxWidth()) {
            Column(
                modifier = Modifier.padding(16.dp),
                verticalArrangement = Arrangement.spacedBy(10.dp)
            ) {
                Text(t("数据", "Data"), style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
                if (vm.lastSyncInfo.isNotBlank()) {
                    Text(
                        t("最近回传：${vm.lastSyncInfo}", "Last sync: ${vm.lastSyncInfo}"),
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }
                OutlinedButton(
                    onClick = { vm.refreshAll() },
                    modifier = Modifier.fillMaxWidth()
                ) {
                    Text(t("立即同步服务器数据", "Sync now"))
                }
                OutlinedButton(
                    onClick = { vm.flushProgressNow() },
                    modifier = Modifier.fillMaxWidth()
                ) {
                    Text(t("立即回传当前进度", "Upload progress now"))
                }
            }
        }

        Card(modifier = Modifier.fillMaxWidth()) {
            Column(
                modifier = Modifier.padding(16.dp),
                verticalArrangement = Arrangement.spacedBy(10.dp)
            ) {
                Text(t("关于与更新", "About & updates"), style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
                Text(
                    t("当前版本 ${vm.currentVersion}", "Version ${vm.currentVersion}"),
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
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    OutlinedButton(
                        onClick = { vm.checkForUpdate() },
                        enabled = !vm.isCheckingUpdate && !vm.isDownloadingUpdate
                    ) { Text(if (vm.isCheckingUpdate) t("检查中…", "Checking...") else t("检查更新", "Check for updates")) }

                    TextButton(onClick = { vm.openReleasesPage() }) {
                        Text(
                            t("浏览器下载", "Releases page"),
                            style = MaterialTheme.typography.labelSmall
                        )
                    }
                }

                // Kept out of the Row on purpose: inside a Row the label wraps one
                // character per line and the button collapses into a thin bar.
                val pending = vm.pendingUpdate
                if (pending != null) {
                    Button(
                        onClick = { vm.installUpdate() },
                        enabled = !vm.isDownloadingUpdate,
                        modifier = Modifier.fillMaxWidth()
                    ) {
                        Text(
                            when {
                                vm.isDownloadingUpdate -> t("下载中 ${vm.updateProgress}%", "Downloading ${vm.updateProgress}%")
                                else -> t("一键升级到 ${pending.versionName}", "Update to ${pending.versionName}")
                            },
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis
                        )
                    }
                }
            }
        }

        Card(modifier = Modifier.fillMaxWidth()) {
            Column(
                modifier = Modifier.padding(16.dp),
                verticalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text(
                        t("诊断日志", "Diagnostics"),
                        style = MaterialTheme.typography.titleMedium,
                        fontWeight = FontWeight.SemiBold
                    )
                    TextButton(onClick = { vm.clearLogs() }) { Text(t("清空", "Clear")) }
                }
                if (vm.logs.isEmpty()) {
                    Text(
                        t("暂无日志", "No log entries"),
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                } else {
                    vm.logs.take(12).forEach { line ->
                        Text(
                            line,
                            style = MaterialTheme.typography.bodySmall,
                            fontFamily = FontFamily.Monospace,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    }
                }
            }
        }
    }
}

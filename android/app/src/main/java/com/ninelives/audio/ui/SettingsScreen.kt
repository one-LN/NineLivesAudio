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
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
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
                    Box(modifier = Modifier.fillMaxWidth()) {
                        OutlinedButton(
                            onClick = { expanded = true },
                            modifier = Modifier.fillMaxWidth()
                        ) {
                            Text(currentSaved?.displayName ?: "选择已保存的服务器")
                        }
                        DropdownMenu(
                            expanded = expanded,
                            onDismissRequest = { expanded = false }
                        ) {
                            vm.savedServers.forEach { s ->
                                DropdownMenuItem(
                                    text = { Text(s.displayName) },
                                    onClick = {
                                        expanded = false
                                        isAdding = false
                                        vm.switchServer(s)
                                    }
                                )
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

        if (vm.logs.isNotEmpty()) {
            Card(modifier = Modifier.fillMaxWidth()) {
                Column(modifier = Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    Text("运行日志", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
                    vm.logs.forEach { line ->
                        Text(line, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                }
            }
        }
    }
}

package com.ninelives.audio.ui.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

val SigilGold = Color(0xFFD4A853)
val VoidDeep = Color(0xFF0E1117)
val VoidSurface = Color(0xFF161B24)
val LightBg = Color(0xFFF6F7F9)
val LightSurface = Color(0xFFFFFFFF)

private val DarkColors = darkColorScheme(
    primary = SigilGold,
    onPrimary = VoidDeep,
    background = VoidDeep,
    surface = VoidSurface,
    surfaceVariant = Color(0xFF1A202C),
    surfaceContainerLowest = VoidDeep,
    surfaceContainerLow = Color(0xFF12161F),
    surfaceContainer = VoidSurface,
    surfaceContainerHigh = Color(0xFF1C222D),
    surfaceContainerHighest = Color(0xFF222936),
    onBackground = Color(0xFFE6EDF3),
    onSurface = Color(0xFFE6EDF3),
    onSurfaceVariant = Color(0xFF8B949E)
)

private val LightColors = lightColorScheme(
    primary = Color(0xFF9A6F1C),
    onPrimary = Color.White,
    background = LightBg,
    surface = LightSurface,
    surfaceVariant = Color(0xFFEDEFF2),
    surfaceContainerLowest = LightSurface,
    surfaceContainerLow = Color(0xFFFAFBFD),
    surfaceContainer = LightSurface,
    surfaceContainerHigh = Color(0xFFF0F2F5),
    surfaceContainerHighest = Color(0xFFE8EBF0),
    onBackground = Color(0xFF1F2328),
    onSurface = Color(0xFF1F2328),
    onSurfaceVariant = Color(0xFF656D76)
)

@Composable
fun NineLivesTheme(
    darkTheme: Boolean = isSystemInDarkTheme(),
    content: @Composable () -> Unit
) {
    MaterialTheme(
        colorScheme = if (darkTheme) DarkColors else LightColors,
        content = content
    )
}

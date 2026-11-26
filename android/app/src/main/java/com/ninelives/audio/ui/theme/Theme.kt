package com.ninelives.audio.ui.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

// Palette copied 1:1 from the desktop client (Resources/Styles.xaml).
val SigilGold = Color(0xFFC5A55A)
val SigilGoldBright = Color(0xFFD4AF37)
val SigilGoldDim = Color(0xFF8A7339)
val VoidDeep = Color(0xFF050810)
val VoidBase = Color(0xFF0A0E1A)
val VoidSurface = Color(0xFF111827)
val VoidElevated = Color(0xFF1A2236)
val NebulaLight = Color(0xFF2C5F6E)
val Starlight = Color(0xFFFFFFFF)
val StarlightDim = Color(0xFFE0E0E8)
val Mist = Color(0xFF9BA4B5)
val MistFaint = Color(0xFF6B7280)
val Dust = Color(0xFF3D4451)

val LightBg = Color(0xFFF6F7F9)
val LightSurface = Color(0xFFFFFFFF)
val LightText = Color(0xFF1E2735)

private val DarkColors = darkColorScheme(
    primary = SigilGold,
    onPrimary = VoidDeep,
    primaryContainer = VoidElevated,
    onPrimaryContainer = StarlightDim,
    secondary = NebulaLight,
    onSecondary = Starlight,
    background = VoidDeep,
    onBackground = StarlightDim,
    surface = VoidSurface,
    onSurface = StarlightDim,
    surfaceVariant = VoidElevated,
    onSurfaceVariant = Mist,
    outline = Dust,
    outlineVariant = Color(0xFF2A3342),
    error = Color(0xFFEF4444),
    surfaceContainerLowest = VoidDeep,
    surfaceContainerLow = VoidBase,
    surfaceContainer = VoidSurface,
    surfaceContainerHigh = VoidElevated,
    surfaceContainerHighest = Color(0xFF202A3D)
)

private val LightColors = lightColorScheme(
    primary = SigilGoldDim,
    onPrimary = Color.White,
    primaryContainer = Color(0xFFF3EAD3),
    onPrimaryContainer = Color(0xFF3D3216),
    secondary = NebulaLight,
    onSecondary = Color.White,
    background = LightBg,
    onBackground = LightText,
    surface = LightSurface,
    onSurface = LightText,
    surfaceVariant = Color(0xFFEAEDF2),
    onSurfaceVariant = MistFaint,
    outline = Color(0xFFD0D5DD),
    outlineVariant = Color(0xFFE4E8EE),
    error = Color(0xFFDC2626),
    surfaceContainerLowest = LightSurface,
    surfaceContainerLow = Color(0xFFFAFBFD),
    surfaceContainer = LightSurface,
    surfaceContainerHigh = Color(0xFFF2F4F7),
    surfaceContainerHighest = Color(0xFFEAEDF2)
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

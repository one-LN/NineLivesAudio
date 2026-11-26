#!/usr/bin/env python3
"""Structural checks for the Android project and platform-specific CI boundaries."""
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
ANDROID = ROOT / "android"
REQUIRED = [
    "settings.gradle.kts", "build.gradle.kts", "gradle.properties",
    "app/build.gradle.kts", "app/src/main/AndroidManifest.xml",
    "app/src/main/java/com/ninelives/audio/MainActivity.kt",
    "app/src/main/java/com/ninelives/audio/data/AbsApiClient.kt",
    "app/src/main/java/com/ninelives/audio/data/Models.kt",
    "app/src/main/java/com/ninelives/audio/data/SettingsStore.kt",
    "app/src/main/java/com/ninelives/audio/data/DownloadModels.kt",
    "app/src/main/java/com/ninelives/audio/data/DownloadStore.kt",
    "app/src/main/java/com/ninelives/audio/data/DownloadManager.kt",
    "app/src/main/java/com/ninelives/audio/data/LibraryCache.kt",
    "app/src/main/java/com/ninelives/audio/data/UpdateChecker.kt",
    "app/src/main/java/com/ninelives/audio/data/ListenLog.kt",
    "app/src/main/java/com/ninelives/audio/data/BookmarkStore.kt",
    "app/src/main/java/com/ninelives/audio/ui/Strings.kt",
    "app/src/main/java/com/ninelives/audio/player/PlaybackService.kt",
    "app/src/main/java/com/ninelives/audio/ui/HomeScreen.kt",
    "app/src/main/java/com/ninelives/audio/ui/LibraryScreen.kt",
    "app/src/main/java/com/ninelives/audio/ui/MainViewModel.kt",
    "app/src/main/java/com/ninelives/audio/ui/PlayerScreen.kt",
    "app/src/main/java/com/ninelives/audio/ui/SettingsScreen.kt",
    "app/src/main/java/com/ninelives/audio/ui/DownloadsScreen.kt",
    "app/src/main/java/com/ninelives/audio/ui/StatsScreen.kt",
    "app/src/main/res/drawable/ic_launcher.png",
]
problems = []
for rel in REQUIRED:
    if not (ANDROID / rel).is_file():
        problems.append(f"missing android/{rel}")

app_gradle = ANDROID / "app/build.gradle.kts"
if app_gradle.is_file():
    text = app_gradle.read_text(encoding="utf-8")
    code = re.search(r"\bversionCode\s*=\s*(\d+)", text)
    name = re.search(r'\bversionName\s*=\s*"([0-9]+(?:\.[0-9]+)+)"', text)
    if not code or int(code.group(1)) <= 1:
        problems.append("Android versionCode must exceed the published APK's versionCode 1")
    if not name or tuple(map(int, name.group(1).split("."))) <= (0, 22, 0):
        problems.append("Android versionName must be newer than the published v0.22.0 APK")
    if 'applicationId = "com.ninelives.audio"' not in text:
        problems.append("Android applicationId must remain stable for in-place APK upgrades")

manifest = ANDROID / "app/src/main/AndroidManifest.xml"
if manifest.is_file():
    try:
        ET.parse(manifest)
    except ET.ParseError as exc:
        problems.append(f"AndroidManifest.xml is malformed: {exc}")

# Catch unresolved Compose modifier imports without needing the Android SDK.
library_screen = ANDROID / "app/src/main/java/com/ninelives/audio/ui/LibraryScreen.kt"
if library_screen.is_file():
    text = library_screen.read_text(encoding="utf-8")
    if ".size(18.dp)" in text and "import androidx.compose.foundation.layout.size" not in text:
        problems.append("LibraryScreen.kt uses Modifier.size but is missing the size import")
    imports = re.findall(r"^import ([\\w.]+)$", text, re.M)
    repeated = sorted({imp for imp in imports if imports.count(imp) > 1})
    if repeated:
        problems.append(f"LibraryScreen.kt has duplicate imports: {', '.join(repeated)}")

main_activity = ANDROID / "app/src/main/java/com/ninelives/audio/MainActivity.kt"
if main_activity.is_file():
    text = main_activity.read_text(encoding="utf-8")
    for page, screen in ((3, "DownloadsScreen"), (4, "StatsScreen")):
        if f"{page} -> {screen}(vm" not in text:
            problems.append(f"pager page {page} does not route to {screen}")
        if f"import com.ninelives.audio.ui.{screen}" not in text:
            problems.append(f"MainActivity.kt is missing the {screen} import")
    if "pageCount = { 6 }" not in text:
        problems.append("MainActivity pager should expose six matching navigation tabs")

# Offline download pipeline: metadata endpoint, byte endpoint and UI wiring must stay connected.
api_client = ANDROID / "app/src/main/java/com/ninelives/audio/data/AbsApiClient.kt"
if api_client.is_file():
    api_text = api_client.read_text(encoding="utf-8")
    for snippet, message in (
        ("expanded=1", "Download metadata must be read from the expanded item endpoint"),
        ("/file/${segment(", "Audio bytes must come from the ABS file endpoint"),
        ('header("Range", "bytes=', "Resumable downloads must send a Range header"),
    ):
        if snippet not in api_text:
            problems.append(message)
download_store = ANDROID / "app/src/main/java/com/ninelives/audio/data/DownloadStore.kt"
if download_store.is_file() and "hashCode()" in download_store.read_text(encoding="utf-8"):
    problems.append("Download directories must not be keyed by a 32-bit hashCode")
download_screen = ANDROID / "app/src/main/java/com/ninelives/audio/ui/DownloadsScreen.kt"
if download_screen.is_file():
    screen_text = download_screen.read_text(encoding="utf-8")
    for snippet, message in (
        ("vm.downloadTasks", "Downloads screen must render the real queue"),
        ("vm.downloadedBooks", "Downloads screen must render completed offline books"),
        ("retryDownload", "Downloads screen must offer resume/retry"),
    ):
        if snippet not in screen_text:
            problems.append(message)
if "ACCESS_NETWORK_STATE" not in (ANDROID / "app/src/main/AndroidManifest.xml").read_text(encoding="utf-8"):
    problems.append("Wi-Fi-only downloads need the ACCESS_NETWORK_STATE permission")

if api_client.is_file():
    api_text = api_client.read_text(encoding="utf-8")
    for snippet, message in (
        ("/bookmark", "Bookmarks must use the ABS bookmark endpoints"),
        ("fun deleteBookmark(", "Bookmarks must be deletable"),
    ):
        if snippet not in api_text:
            problems.append(message)
    for name in ("getBookmarks", "createBookmark", "deleteBookmark"):
        if api_text.count("suspend fun " + name + "(") != 1:
            problems.append("Duplicate or missing API definition: " + name)
player_screen = ANDROID / "app/src/main/java/com/ninelives/audio/ui/PlayerScreen.kt"
if player_screen.is_file():
    text = player_screen.read_text(encoding="utf-8")
    for snippet, message in (
        ("vm.addBookmarkAtCurrent", "Player must let the listener bookmark the current position"),
        ("vm.jumpToBookmark", "Bookmarks must be jumpable"),
        ("vm.deleteBookmark", "Bookmarks must be deletable from the player"),
    ):
        if snippet not in text:
            problems.append(message)
library_screen = ANDROID / "app/src/main/java/com/ninelives/audio/ui/LibraryScreen.kt"
if library_screen.is_file():
    text = library_screen.read_text(encoding="utf-8")
    for snippet, message in (
        ("FilterChip(", "Library must expose status filter chips"),
        ("vm.selectLibrary(", "Library title must switch between libraries"),
    ):
        if snippet not in text:
            problems.append(message)
stats_screen = ANDROID / "app/src/main/java/com/ninelives/audio/ui/StatsScreen.kt"
if stats_screen.is_file() and "vm.listenWeek" not in stats_screen.read_text(encoding="utf-8"):
    problems.append("Stats screen must render the 7-day listening chart")
strings = ANDROID / "app/src/main/java/com/ninelives/audio/ui/Strings.kt"
if strings.is_file():
    text = strings.read_text(encoding="utf-8")
    for snippet, message in (
        ("fun t(", "Missing translation helper"),
        ('mutableStateOf("zh")', "Language state must be observable"),
    ):
        if snippet not in text:
            problems.append(message)
update_checker = ANDROID / "app/src/main/java/com/ninelives/audio/data/UpdateChecker.kt"
if update_checker.is_file() and "isJsonNull" not in update_checker.read_text(encoding="utf-8"):
    problems.append("Release JSON must be read defensively (null body fields crash Gson)")
vm_source = ANDROID / "app/src/main/java/com/ninelives/audio/ui/MainViewModel.kt"
if vm_source.is_file():
    text = vm_source.read_text(encoding="utf-8")
    if "refreshAll()" not in text or "isSyncing = false" not in text:
        problems.append("Login must release isSyncing before refreshing the library")
    login = text.split("fun loginAndConnect", 1)[-1]
    login = login.split("fun switchServer", 1)[0]
    if "isSyncing = false" not in login or "refreshAll()" not in login:
        problems.append("Adding a server must load libraries and books immediately")
settings_store = ANDROID / "app/src/main/java/com/ninelives/audio/data/SettingsStore.kt"
if settings_store.is_file() and "theme_mode" not in settings_store.read_text(encoding="utf-8"):
    problems.append("Theme choice must survive app restarts")

android_workflow_path = ROOT / ".github/workflows/android.yml"
android_workflow = android_workflow_path.read_text(encoding="utf-8") if android_workflow_path.is_file() else ""
if not android_workflow:
    problems.append("missing .github/workflows/android.yml")
else:
    checks = [
        ('branches: [ master ]', "Android push builds should target master only"),
        ('  pull_request:\n    branches: [ master ]', "Android CI should validate pull requests into master"),
        ('"android-v*"', "Android CI should run on android-v* version tags"),
        ("make_latest: false", "Android prereleases must not become the repository latest release"),
        ("prerelease: true", "Android version releases must remain prereleases"),
        ("NineLivesAudio-android.apk", "Android release must keep its stable APK filename"),
        ("release-android:", "Android tag release must be a separate job"),
        ("needs: build-android", "Android release must wait for a successful APK build"),
        ("contents: write", "Android release job must declare contents: write"),
        ('android-build.log', "Android build failure diagnostics must capture the Gradle log"),
        ("::error title=Gradle::", "Gradle failures must surface as public annotations"),
        ("::warning::Could not post Android build diagnostics", "Android log-comment errors must not mask the build failure"),
    ]
    for snippet, message in checks:
        if snippet not in android_workflow:
            problems.append(message)
windows_workflow_path = ROOT / ".github/workflows/build.yml"
windows_workflow = windows_workflow_path.read_text(encoding="utf-8") if windows_workflow_path.is_file() else ""
if not windows_workflow:
    problems.append("missing .github/workflows/build.yml")
else:
    if 'paths-ignore:' not in windows_workflow or '"android/**"' not in windows_workflow:
        problems.append("Android-only changes must not start a Windows desktop/release build")
    if "latest-build" in windows_workflow.split("- name: Publish download release", 1)[-1]:
        problems.append("development builds must not overwrite the stable latest release")
    stable_guard = "if: github.event_name == 'push' && (github.ref == 'refs/heads/master' || github.ref == 'refs/heads/main')"
    if stable_guard not in windows_workflow:
        problems.append("stable Windows releases must publish only from master/main")

if problems:
    print(f"Android structural check failed ({len(problems)} issue(s)):")
    for problem in problems:
        print(" -", problem)
    sys.exit(1)
count = sum(1 for path in ANDROID.rglob("*") if path.is_file())
print(f"Android structural check passed: {count} project files, manifest XML valid, and CI release boundaries present.")

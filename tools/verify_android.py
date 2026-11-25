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

main_activity = ANDROID / "app/src/main/java/com/ninelives/audio/MainActivity.kt"
if main_activity.is_file():
    text = main_activity.read_text(encoding="utf-8")
    for page, screen in ((3, "DownloadsScreen"), (4, "StatsScreen")):
        if f"{page} -> {screen}(vm)" not in text:
            problems.append(f"pager page {page} does not route to {screen}")
        if f"import com.ninelives.audio.ui.{screen}" not in text:
            problems.append(f"MainActivity.kt is missing the {screen} import")
    if "pageCount = { 6 }" not in text:
        problems.append("MainActivity pager should expose six matching navigation tabs")

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
    ]
    for snippet, message in checks:
        if snippet not in android_workflow:
            problems.append(message)
    if "paths:" in android_workflow:
        problems.append("Android workflow must not have a paths filter that suppresses tag builds")

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

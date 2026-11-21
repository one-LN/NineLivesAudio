# Install-Experimental.ps1
# Nine Lives Audio v1.1 Experimental Installer
# Self-extracting portable installer for Windows 10/11 (x64)

param(
    [string]$InstallDir = "$env:LOCALAPPDATA\NineLivesAudio",
    [switch]$NoDesktopShortcut,
    [switch]$NoStartMenu,
    [switch]$Uninstall
)

$ErrorActionPreference = "Stop"
$AppName = "Nine Lives Audio"
$ExeName = "NineLivesAudio.exe"
$Version = "1.1.0-Experimental"

function Write-Header {
    Write-Host ""
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host "  $AppName v$Version" -ForegroundColor Cyan
    Write-Host "  Experimental Build Installer" -ForegroundColor DarkCyan
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host ""
}

function Remove-Installation {
    Write-Host "Uninstalling $AppName..." -ForegroundColor Yellow

    # Stop running instances
    Get-Process -Name "NineLivesAudio" -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 1

    # Remove shortcuts
    $desktopLink = "$env:USERPROFILE\Desktop\$AppName.lnk"
    $startMenuDir = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\$AppName"
    if (Test-Path $desktopLink) { Remove-Item $desktopLink -Force }
    if (Test-Path $startMenuDir) { Remove-Item $startMenuDir -Recurse -Force }

    # Remove app directory (but not user data)
    if (Test-Path $InstallDir) {
        Remove-Item $InstallDir -Recurse -Force
        Write-Host "Removed: $InstallDir" -ForegroundColor Green
    }

    Write-Host ""
    Write-Host "Uninstall complete." -ForegroundColor Green
    Write-Host "User data preserved at: $env:LOCALAPPDATA\NineLivesAudio" -ForegroundColor DarkGray
    Write-Host "(Delete that folder manually to remove all data)" -ForegroundColor DarkGray
    return
}

function New-Shortcut {
    param([string]$Path, [string]$Target, [string]$Icon)
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($Path)
    $shortcut.TargetPath = $Target
    if ($Icon) { $shortcut.IconLocation = $Icon }
    $shortcut.Save()
}

# ── Main ──

Write-Header

if ($Uninstall) {
    Remove-Installation
    exit 0
}

# Check for running instances
$running = Get-Process -Name "NineLivesAudio" -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Nine Lives Audio is currently running." -ForegroundColor Yellow
    $choice = Read-Host "Close it and continue? (Y/n)"
    if ($choice -eq "n" -or $choice -eq "N") {
        Write-Host "Installation cancelled." -ForegroundColor Red
        exit 1
    }
    $running | Stop-Process -Force
    Start-Sleep -Seconds 2
}

Write-Host "Install directory: $InstallDir" -ForegroundColor White
Write-Host ""

# Locate source files (same directory as this script)
$sourceDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceApp = Join-Path $sourceDir $ExeName

if (-not (Test-Path $sourceApp)) {
    # Try looking in a subfolder named 'app'
    $sourceDir = Join-Path $sourceDir "app"
    $sourceApp = Join-Path $sourceDir $ExeName
}

if (-not (Test-Path $sourceApp)) {
    Write-Host "ERROR: Cannot find $ExeName in the extracted folder." -ForegroundColor Red
    Write-Host "Make sure you extracted the entire ZIP before running this script." -ForegroundColor Yellow
    exit 1
}

# Create install directory
if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
}

# Copy files
Write-Host "Copying files..." -ForegroundColor Cyan
$fileCount = (Get-ChildItem -Path $sourceDir -Recurse -File).Count
$copied = 0

Get-ChildItem -Path $sourceDir -Recurse -File | ForEach-Object {
    $relativePath = $_.FullName.Substring($sourceDir.Length + 1)
    $destPath = Join-Path $InstallDir $relativePath
    $destDir = Split-Path -Parent $destPath

    if (-not (Test-Path $destDir)) {
        New-Item -ItemType Directory -Path $destDir -Force | Out-Null
    }

    Copy-Item $_.FullName $destPath -Force
    $copied++
    if ($copied % 50 -eq 0) {
        Write-Progress -Activity "Installing $AppName" -Status "$copied / $fileCount files" -PercentComplete (($copied / $fileCount) * 100)
    }
}
Write-Progress -Activity "Installing $AppName" -Completed

Write-Host "  Copied $fileCount files." -ForegroundColor Green

# Desktop shortcut
if (-not $NoDesktopShortcut) {
    $desktopLink = "$env:USERPROFILE\Desktop\$AppName.lnk"
    $exePath = Join-Path $InstallDir $ExeName
    New-Shortcut -Path $desktopLink -Target $exePath -Icon $exePath
    Write-Host "  Desktop shortcut created." -ForegroundColor Green
}

# Start Menu
if (-not $NoStartMenu) {
    $startMenuDir = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\$AppName"
    if (-not (Test-Path $startMenuDir)) {
        New-Item -ItemType Directory -Path $startMenuDir -Force | Out-Null
    }
    $exePath = Join-Path $InstallDir $ExeName
    New-Shortcut -Path "$startMenuDir\$AppName.lnk" -Target $exePath -Icon $exePath
    Write-Host "  Start Menu entry created." -ForegroundColor Green
}

Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "  Installation complete!" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Location: $InstallDir" -ForegroundColor White
Write-Host "  Version:  $Version" -ForegroundColor White
Write-Host ""

$launch = Read-Host "Launch $AppName now? (Y/n)"
if ($launch -ne "n" -and $launch -ne "N") {
    Start-Process (Join-Path $InstallDir $ExeName)
}

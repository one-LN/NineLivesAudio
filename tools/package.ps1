# Nine Lives Audio — one-shot build + packaging check (run on Windows)
#
#   powershell -ExecutionPolicy Bypass -File tools\package.ps1            # build + verify + publish
#   powershell -ExecutionPolicy Bypass -File tools\package.ps1 -Installer # ... and compile the installer
#
# Requirements: .NET 9 SDK, (optional) Inno Setup 6 for -Installer.

[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SkipVerify,
    [switch]$Installer
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
$failed = $false

function Step($name, [scriptblock]$body) {
    Write-Host "`n=== $name ===" -ForegroundColor Cyan
    & $body
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED: $name" -ForegroundColor Red
        $script:failed = $true
    }
}

# 1. Offline static self-check (i18n parity, XAML sanity, DI graph, packaging consistency)
if (-not $SkipVerify) {
    Step "Static self-check" {
        $python = Get-Command python -ErrorAction SilentlyContinue
        if ($null -eq $python) { $python = Get-Command python3 -ErrorAction SilentlyContinue }
        if ($null -ne $python) {
            & $python.Source "tools/verify.py"
        } else {
            Write-Host "Python not found — skipping tools/verify.py" -ForegroundColor Yellow
            $global:LASTEXITCODE = 0
        }
    }
}

# 2. Restore + build
Step "Restore"        { dotnet restore -r $Runtime }
Step "Build"          { dotnet build -r $Runtime -c $Configuration --no-restore }

# 3. Publish (profile drives WindowsAppSDKSelfContained + binpublish output used by installer.iss)
Step "Publish"        { dotnet publish -p:PublishProfile=win-x64 -c $Configuration --no-restore }

# 4. Optional installer compile
if ($Installer -and -not $failed) {
    $iscc = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1

    if ($null -eq $iscc) {
        Write-Host "Inno Setup 6 (ISCC.exe) not found — skipping installer." -ForegroundColor Yellow
    } else {
        Step "Installer (Inno Setup)" { & $iscc "installer.iss" }
    }
}

Pop-Location

if ($failed) {
    Write-Host "`nPackaging finished with errors." -ForegroundColor Red
    exit 1
}

Write-Host "`nBuild + packaging check passed." -ForegroundColor Green
if (Test-Path "binpublish") {
    $size = (Get-ChildItem "binpublish" -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB
    Write-Host ("Publish output: binpublish ({0:N1} MB)" -f $size)
}

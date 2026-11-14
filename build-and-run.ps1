# Build, sign, and run NineLivesAudio
# This script handles Windows Smart App Control by code-signing the output

# Stop existing instance
Stop-Process -Name NineLivesAudio -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

# Build
Write-Host "Building..." -ForegroundColor Cyan
dotnet build -r win-x64 -c Debug 2>&1 | Select-Object -Last 5

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

# Sign
$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object { $_.Subject -eq 'CN=Static Hum Studio' } | Select-Object -First 1
if ($cert) {
    Write-Host "Signing assemblies..." -ForegroundColor Yellow
    $binPath = "bin\Debug\net9.0-windows10.0.22621.0\win-x64"
    Set-AuthenticodeSignature -FilePath "$binPath\NineLivesAudio.dll" -Certificate $cert | Out-Null
    Set-AuthenticodeSignature -FilePath "$binPath\NineLivesAudio.exe" -Certificate $cert | Out-Null
    Write-Host "Signed successfully" -ForegroundColor Green
} else {
    Write-Host "WARNING: No signing certificate found. App may be blocked by Smart App Control." -ForegroundColor Yellow
}

# Run
Write-Host "Starting app..." -ForegroundColor Cyan
Start-Process "$binPath\NineLivesAudio.exe"
Write-Host "App started!" -ForegroundColor Green

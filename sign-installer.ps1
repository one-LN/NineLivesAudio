# Sign the Inno Setup installer executable
$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object { $_.Subject -eq 'CN=Static Hum Studio' } | Select-Object -First 1

if ($cert) {
    $installerPath = "$PSScriptRoot\installer_output\NineLivesAudioSetup.exe"

    if (Test-Path $installerPath) {
        Write-Host "Signing installer: $installerPath" -ForegroundColor Cyan

        $result = Set-AuthenticodeSignature -FilePath $installerPath -Certificate $cert -TimestampServer "http://timestamp.digicert.com"

        if ($result.Status -eq 'Valid') {
            Write-Host "[SUCCESS] Installer signed successfully!" -ForegroundColor Green
            Write-Host "Subject: $($result.SignerCertificate.Subject)" -ForegroundColor Gray
        } else {
            Write-Host "[ERROR] Signing failed: $($result.Status)" -ForegroundColor Red
            exit 1
        }
    } else {
        Write-Host "[ERROR] Installer not found: $installerPath" -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "[ERROR] No certificate found with subject 'CN=Static Hum Studio'" -ForegroundColor Red
    exit 1
}

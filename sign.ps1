$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object { $_.Subject -eq 'CN=Static Hum Studio' } | Select-Object -First 1
if ($cert) {
    Write-Host "Found certificate: $($cert.Thumbprint)"

    $dllPath = "$PSScriptRoot\bin\Debug\net9.0-windows10.0.22621.0\win-x64\NineLivesAudio.dll"
    $exePath = "$PSScriptRoot\bin\Debug\net9.0-windows10.0.22621.0\win-x64\NineLivesAudio.exe"

    Write-Host "Signing DLL..."
    Set-AuthenticodeSignature -FilePath $dllPath -Certificate $cert -TimestampServer "http://timestamp.digicert.com"

    Write-Host "Signing EXE..."
    Set-AuthenticodeSignature -FilePath $exePath -Certificate $cert -TimestampServer "http://timestamp.digicert.com"

    Write-Host "Done!"
} else {
    Write-Host "No certificate found!"
}

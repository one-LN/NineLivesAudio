$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object { $_.Subject -eq 'CN=Static Hum Studio' } | Select-Object -First 1
if ($cert) {
    Write-Host "Found cert: $($cert.Thumbprint)"

    # Export cert
    $certBytes = $cert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert)
    $certPath = "$PSScriptRoot\NineLivesAudio.cer"
    [System.IO.File]::WriteAllBytes($certPath, $certBytes)
    Write-Host "Exported cert to $certPath"

    # Import to Trusted Publishers
    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store("TrustedPublisher", "CurrentUser")
    $store.Open("ReadWrite")
    $store.Add($cert)
    $store.Close()
    Write-Host "Added to TrustedPublisher store"

    # Import to Root CA
    $store2 = New-Object System.Security.Cryptography.X509Certificates.X509Store("Root", "CurrentUser")
    $store2.Open("ReadWrite")
    $store2.Add($cert)
    $store2.Close()
    Write-Host "Added to Root store"
}

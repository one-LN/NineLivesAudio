<#
  Removes localized resource folders that the app does not ship.

  SatelliteResourceLanguages trims *managed* satellites only. A self-contained WinUI 3
  publish also copies native MUI resources (Microsoft.ui.xaml.dll.mui, ...) plus whatever
  a dependency pulls in, one folder per culture — that is what leaves cs/ de/ fr/ ja/ ...
  behind in the install directory.

  Keep: en* and zh* (the two UI languages the app supports). Remove: everything else that
  is named like a culture folder. Non-culture folders (runtimes, Assets, build) are left
  alone.
#>
param(
    [Parameter(Mandatory = $true)][string]$PublishDir,
    [string[]]$Keep = @('en', 'zh')
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $PublishDir)) {
    Write-Error "Publish directory not found: $PublishDir"
    exit 1
}

$culturePattern = '^[a-z]{2,3}(-[a-z]{2,4}|-[a-z]{4})?$'
$removed = @()

foreach ($dir in Get-ChildItem -LiteralPath $PublishDir -Directory) {
    if ($dir.Name -notmatch $culturePattern) { continue }

    $base = ($dir.Name -split '-')[0].ToLowerInvariant()
    if ($Keep -contains $base) { continue }

    Remove-Item -LiteralPath $dir.FullName -Recurse -Force
    $removed += $dir.Name
}

if ($removed.Count -gt 0) {
    Write-Host "Trimmed $($removed.Count) culture folder(s): $($removed -join ', ')"
} else {
    Write-Host "No culture folders to trim."
}

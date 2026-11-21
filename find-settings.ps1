Get-ChildItem -Path "$env:APPDATA\.." -Recurse -Filter "settings.json" -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName } | Where-Object { $_ -like '*Audio*' }

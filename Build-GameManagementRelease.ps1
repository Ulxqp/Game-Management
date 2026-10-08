$ErrorActionPreference = 'Stop'
$package = Split-Path -Parent $MyInvocation.MyCommand.Path
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$version = (Get-Item -LiteralPath (Join-Path $package 'GameManagement.exe')).VersionInfo.FileVersion
$releaseVersion = $version -replace '\.0$', ''
$dist = Join-Path $package 'dist'
[IO.Directory]::CreateDirectory($dist) | Out-Null
$archivePath = Join-Path $dist "GameManagement-v$releaseVersion.zip"
$temporaryPath = "$archivePath.tmp"
$files = [ordered]@{
    'GameManagement.exe' = 'GameManagement.exe'
    'GameManagement.ico' = 'GameManagement.ico'
    'GameManagement.ps1' = 'GameManagement.ps1'
    'Install-GameManagement.ps1' = 'Install-GameManagement.ps1'
    'Pause-GameManagement.ps1' = 'Pause-GameManagement.ps1'
    'Undo-GameManagement.ps1' = 'Undo-GameManagement.ps1'
    'Uninstall-GameManagement.ps1' = 'Uninstall-GameManagement.ps1'
    'README.txt' = 'README.txt'
    'settings.json' = 'settings.json'
    'Source/GameManagement.cs' = 'GameManagement.cs'
    'Tests/Test-GameManagementSecurity.ps1' = 'Test-GameManagementSecurity.ps1'
    'Tests/Test-GameManagementFeatures.ps1' = 'Test-GameManagementFeatures.ps1'
    'Documentation/CHANGELOG.md' = 'CHANGELOG.md'
    'Documentation/TEMPERATURE-GUIDE.md' = 'TEMPERATURE-GUIDE.md'
}
$defaults = Get-Content -Raw -LiteralPath (Join-Path $package 'settings.json') | ConvertFrom-Json
if (@($defaults.workApps).Count -gt 0 -or @($defaults.gameApps).Count -gt 0 -or
    $defaults.activeMode -ne 'game' -or [bool]$defaults.automaticModeDetection) {
    throw 'Release settings must be clean defaults, without personal app assignments.'
}
$archive = [IO.Compression.ZipFile]::Open($temporaryPath, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($entryName in $files.Keys) {
        $source = Join-Path $package $files[$entryName]
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $source,
            "GameManagement/$entryName", [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose() }

$verification = [IO.Compression.ZipFile]::OpenRead($temporaryPath)
try {
    if ($verification.Entries.Count -ne $files.Count) { throw 'Unexpected archive entries.' }
    foreach ($entryName in $files.Keys) {
        $entry = $verification.GetEntry("GameManagement/$entryName")
        if ($null -eq $entry) { throw "Missing archive entry: $entryName" }
        $stream = $entry.Open()
        $hasher = [Security.Cryptography.SHA256]::Create()
        try { $actual = [BitConverter]::ToString($hasher.ComputeHash($stream)).Replace('-', '') }
        finally { $stream.Dispose(); $hasher.Dispose() }
        $expected = (Get-FileHash -LiteralPath (Join-Path $package $files[$entryName]) -Algorithm SHA256).Hash
        if ($actual -ne $expected) { throw "Archive verification failed: $entryName" }
    }
} finally { $verification.Dispose() }
Copy-Item -LiteralPath $temporaryPath -Destination $archivePath -Force
Remove-Item -LiteralPath $temporaryPath -Force
$assets = @(Join-Path $dist 'GameManagement-Setup.exe'; $archivePath)
$checksums = foreach ($path in $assets) {
    $asset = Get-Item -LiteralPath $path
    if ($asset.Length -ge 1000000000) { throw "Release asset exceeds the 1 GB limit: $($asset.Name)" }
    "$((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant())  $($asset.Name)"
}
[IO.File]::WriteAllLines((Join-Path $dist 'SHA256SUMS.txt'), $checksums, [Text.UTF8Encoding]::new($false))
Get-Item -LiteralPath $assets | Select-Object Name, Length
Write-Host 'Compressed release verified: every entry matches its source; all assets are below 1 GB.'

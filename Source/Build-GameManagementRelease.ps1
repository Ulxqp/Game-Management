param([string]$PackagePath = (Split-Path -Parent $MyInvocation.MyCommand.Path))
$ErrorActionPreference = 'Stop'
if ((Split-Path -Leaf $PackagePath) -eq 'Source') { $PackagePath = Split-Path -Parent $PackagePath }
$package = [IO.Path]::GetFullPath($PackagePath)
function Resolve-ReleaseFile([string]$name) {
    foreach ($folder in @('', 'Source', 'Tests', 'Documentation', 'Documentation\Reports')) {
        $candidate = if ($folder) { Join-Path (Join-Path $package $folder) $name } else { Join-Path $package $name }
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }
    throw "Missing release payload: $name"
}
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$version = (Get-Item -LiteralPath (Join-Path $package 'GameManagement.exe')).VersionInfo.FileVersion
$releaseVersion = $version -replace '\.0$', ''
$dist = Join-Path $package 'dist'
[IO.Directory]::CreateDirectory($dist) | Out-Null
$archivePath = Join-Path $dist "GameManagement-v$releaseVersion.zip"
$temporaryPath = "$archivePath.$([guid]::NewGuid().ToString('N')).tmp"
$files = [ordered]@{
    'GameManagement.exe' = 'GameManagement.exe'
    'GameManagement.Desktop.exe' = 'GameManagement.Desktop.exe'
    'Microsoft.Web.WebView2.Core.dll' = 'Microsoft.Web.WebView2.Core.dll'
    'Microsoft.Web.WebView2.WinForms.dll' = 'Microsoft.Web.WebView2.WinForms.dll'
    'WebView2Loader.dll' = 'WebView2Loader.dll'
    'Microsoft.Web.WebView2.LICENSE.txt' = 'Microsoft.Web.WebView2.LICENSE.txt'
    'Microsoft.Web.WebView2.NOTICE.txt' = 'Microsoft.Web.WebView2.NOTICE.txt'
    'frontend/dist/index.html' = 'frontend\dist\index.html'
    'frontend/dist/assets/app.js' = 'frontend\dist\assets\app.js'
    'frontend/dist/assets/app.css' = 'frontend\dist\assets\app.css'
    'GameManagement.ico' = 'GameManagement.ico'
    'GameManagement.ps1' = 'GameManagement.ps1'
    'Install-GameManagement.ps1' = 'Install-GameManagement.ps1'
    'Pause-GameManagement.ps1' = 'Pause-GameManagement.ps1'
    'Undo-GameManagement.ps1' = 'Undo-GameManagement.ps1'
    'Uninstall-GameManagement.ps1' = 'Uninstall-GameManagement.ps1'
    'README.txt' = 'README.txt'
    'settings.json' = 'settings.json'
    'Source/GameManagement.cs' = 'GameManagement.cs'
    'Source/ModernDesign.cs' = 'ModernDesign.cs'
    'Source/ModernDesktop.cs' = 'ModernDesktop.cs'
    'Source/ModernHome.cs' = 'ModernHome.cs'
    'Source/ModernAlerts.cs' = 'ModernAlerts.cs'
    'Source/ModernSessionOverview.cs' = 'ModernSessionOverview.cs'
    'Source/ModernPanels.cs' = 'ModernPanels.cs'
    'Source/ModernReports.cs' = 'ModernReports.cs'
    'Source/GameManagementSetup.cs' = 'GameManagementSetup.cs'
    'Source/Build-GameManagement.ps1' = 'Build-GameManagement.ps1'
    'Source/Build-GameManagementInstaller.ps1' = 'Build-GameManagementInstaller.ps1'
    'Source/Build-GameManagementRelease.ps1' = 'Build-GameManagementRelease.ps1'
    'Tests/Test-GameManagementSecurity.ps1' = 'Test-GameManagementSecurity.ps1'
    'Tests/Test-GameManagementFeatures.ps1' = 'Test-GameManagementFeatures.ps1'
    'Tests/Test-GameManagementTimerLifecycle.ps1' = 'Test-GameManagementTimerLifecycle.ps1'
    'Tests/Test-GameManagementV5.ps1' = 'Test-GameManagementV5.ps1'
    'Tests/Test-GameManagementIntegration.ps1' = 'Test-GameManagementIntegration.ps1'
    'Tests/Test-PanelsIntegration.ps1' = 'Test-PanelsIntegration.ps1'
    'Documentation/README.md' = 'README.md'
    'Documentation/AGENTS.md' = 'AGENTS.md'
    'AGENTS.md' = 'AGENTS.md'
    'DESIGN_SPEC.md' = 'DESIGN_SPEC.md'
    'PROMPT_SOL_5_6_ULTRA.md' = 'PROMPT_SOL_5_6_ULTRA.md'
    'SOL-5.6-ULTRA-PACK.md' = 'SOL-5.6-ULTRA-PACK.md'
    'references/TARGET_dashboard_full_window.png' = 'references/TARGET_dashboard_full_window.png'
    'references/TARGET_dashboard_main.png' = 'references/TARGET_dashboard_main.png'
    'references/TARGET_states_and_design_board.png' = 'references/TARGET_states_and_design_board.png'
    'references/CURRENT_implementation_do_not_copy.png' = 'references/CURRENT_implementation_do_not_copy.png'
    'Documentation/CHANGELOG.md' = 'CHANGELOG.md'
    'Documentation/DESIGN-SYSTEM.md' = 'DESIGN-SYSTEM.md'
    'Documentation/V5-IMPLEMENTATION.md' = 'V5-IMPLEMENTATION.md'
    'Documentation/Reports/V5-TEST-REPORT.md' = 'V5-TEST-REPORT.md'
    'Documentation/COMPONENT-REFERENCES.md' = 'COMPONENT-REFERENCES.md'
    'Documentation/Reports/INTEGRATION-TEST-REPORT.md' = 'INTEGRATION-TEST-REPORT.md'
    'Documentation/Reports/SECURITY-REPORT.md' = 'SECURITY-REPORT.md'
    'Documentation/Reports/FEATURE-TEST-REPORT.md' = 'FEATURE-TEST-REPORT.md'
    'Documentation/Reports/TIMER-LIFECYCLE-TEST-REPORT.md' = 'TIMER-LIFECYCLE-TEST-REPORT.md'
    'Documentation/Reports/MODE-SWITCH-TEST-REPORT.md' = 'MODE-SWITCH-TEST-REPORT.md'
    'Documentation/TEMPERATURE-GUIDE.md' = 'TEMPERATURE-GUIDE.md'
    'Documentation/TIMER-STATE-GRAPH.md' = 'TIMER-STATE-GRAPH.md'
}
$defaults = Get-Content -Raw -LiteralPath (Join-Path $package 'settings.json') | ConvertFrom-Json
if (@($defaults.workApps).Count -gt 0 -or @($defaults.gameApps).Count -gt 0 -or
    $defaults.activeMode -ne 'game' -or [bool]$defaults.automaticModeDetection) {
    throw 'Release settings must be clean defaults, without personal app assignments.'
}
$archive = [IO.Compression.ZipFile]::Open($temporaryPath, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($entryName in $files.Keys) {
        $source = Resolve-ReleaseFile $files[$entryName]
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
        $expected = (Get-FileHash -LiteralPath (Resolve-ReleaseFile $files[$entryName]) -Algorithm SHA256).Hash
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

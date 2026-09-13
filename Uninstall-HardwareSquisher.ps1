param([switch]$Quiet)

$ErrorActionPreference = 'Stop'
$installRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$expectedRoot = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'GameBoost'

if (-not [string]::Equals(
    [IO.Path]::GetFullPath($installRoot).TrimEnd('\'),
    [IO.Path]::GetFullPath($expectedRoot).TrimEnd('\'),
    [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Hardware Squisher is not running from its expected installation folder.'
}

if (-not $Quiet) {
    Add-Type -AssemblyName System.Windows.Forms
    $choice = [Windows.Forms.MessageBox]::Show(
        'Remove Hardware Squisher and restore the captured Windows settings? Your settings and activity log will be kept.',
        'Hardware Squisher Uninstall',
        [Windows.Forms.MessageBoxButtons]::YesNo,
        [Windows.Forms.MessageBoxIcon]::Question)
    if ($choice -ne [Windows.Forms.DialogResult]::Yes) { exit 0 }
}

Get-Process -Name 'HardwareSquisher' -ErrorAction SilentlyContinue |
    ForEach-Object {
        $_.Kill()
        [void]$_.WaitForExit(5000)
    }

$undo = Join-Path $installRoot 'Undo-HardwareSquisher.ps1'
if (Test-Path -LiteralPath $undo) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $undo
    if ($LASTEXITCODE -ne 0) { throw 'Windows settings could not be restored.' }
}

$desktopShortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Hardware Squisher.lnk'
$startMenuFolder = Join-Path ([Environment]::GetFolderPath('Programs')) 'Hardware Squisher'
Remove-Item -LiteralPath $desktopShortcut -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $startMenuFolder -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\HardwareSquisher' -Recurse -Force -ErrorAction SilentlyContinue

$cleanupPath = Join-Path ([IO.Path]::GetTempPath()) ('HardwareSquisher-cleanup-' + [Guid]::NewGuid().ToString('N') + '.ps1')
$cleanup = @'
param([string]$InstallRoot, [int]$ParentProcessId)
$ErrorActionPreference = 'SilentlyContinue'
Wait-Process -Id $ParentProcessId -Timeout 15
$programFiles = @(
    'HardwareSquisher.exe',
    'HardwareSquisher.ico',
    'HardwareSquisher.ps1',
    'Install-HardwareSquisher.ps1',
    'Pause-HardwareSquisher.ps1',
    'Undo-HardwareSquisher.ps1',
    'Uninstall-HardwareSquisher.ps1',
    'Test-HardwareSquisherSecurity.ps1',
    'README.txt',
    'SECURITY-REPORT.md'
)
foreach ($name in $programFiles) {
    Remove-Item -LiteralPath (Join-Path $InstallRoot $name) -Force
}
Remove-Item -LiteralPath $InstallRoot -Force
'@
Set-Content -LiteralPath $cleanupPath -Value $cleanup -Encoding UTF8
Start-Process -FilePath 'powershell.exe' -WindowStyle Hidden -ArgumentList @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $cleanupPath,
    '-InstallRoot', $installRoot, '-ParentProcessId', $PID)

if (-not $Quiet) {
    [Windows.Forms.MessageBox]::Show(
        'Hardware Squisher was removed. Your settings and activity log remain in Documents\GameBoost.',
        'Hardware Squisher Uninstall',
        [Windows.Forms.MessageBoxButtons]::OK,
        [Windows.Forms.MessageBoxIcon]::Information) | Out-Null
}


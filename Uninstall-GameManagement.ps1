param([switch]$Quiet)

$ErrorActionPreference = 'Stop'
$installRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$expectedRoot = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'GameManagement'

if (-not [string]::Equals(
    [IO.Path]::GetFullPath($installRoot).TrimEnd('\'),
    [IO.Path]::GetFullPath($expectedRoot).TrimEnd('\'),
    [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Game Management is not running from its expected installation folder.'
}

if (-not $Quiet) {
    Add-Type -AssemblyName System.Windows.Forms
    $choice = [Windows.Forms.MessageBox]::Show(
        'Remove Game Management and restore the captured Windows settings? Your settings and activity log will be kept.',
        'Game Management Uninstall',
        [Windows.Forms.MessageBoxButtons]::YesNo,
        [Windows.Forms.MessageBoxIcon]::Question)
    if ($choice -ne [Windows.Forms.DialogResult]::Yes) { exit 0 }
}

Get-Process -Name 'GameManagement' -ErrorAction SilentlyContinue |
    ForEach-Object {
        $_.Kill()
        [void]$_.WaitForExit(5000)
    }

$undo = Join-Path $installRoot 'Undo-GameManagement.ps1'
if (Test-Path -LiteralPath $undo) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $undo
    if ($LASTEXITCODE -ne 0) { throw 'Windows settings could not be restored.' }
}

$desktopShortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Game Management.lnk'
$startMenuFolder = Join-Path ([Environment]::GetFolderPath('Programs')) 'Game Management'
Remove-Item -LiteralPath $desktopShortcut -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $startMenuFolder -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\GameManagement' -Recurse -Force -ErrorAction SilentlyContinue

$cleanupPath = Join-Path ([IO.Path]::GetTempPath()) ('GameManagement-cleanup-' + [Guid]::NewGuid().ToString('N') + '.ps1')
$cleanup = @'
param([string]$InstallRoot, [int]$ParentProcessId)
$ErrorActionPreference = 'SilentlyContinue'
Wait-Process -Id $ParentProcessId -Timeout 15
$programFiles = @(
    'GameManagement.exe',
    'GameManagement.ico',
    'GameManagement.ps1',
    'Install-GameManagement.ps1',
    'Pause-GameManagement.ps1',
    'Undo-GameManagement.ps1',
    'Uninstall-GameManagement.ps1',
    'Test-GameManagementSecurity.ps1',
    'README.txt',
    'SECURITY-REPORT.md',
    'HARDWARE-COMPATIBILITY.txt'
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
        'Game Management was removed. Your settings and activity log remain in Documents\GameManagement.',
        'Game Management Uninstall',
        [Windows.Forms.MessageBoxButtons]::OK,
        [Windows.Forms.MessageBoxIcon]::Information) | Out-Null
}

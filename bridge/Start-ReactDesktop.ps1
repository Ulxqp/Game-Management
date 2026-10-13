param([string]$PreviewRoot)
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$hostPath = Join-Path $project 'GameManagement.Desktop.exe'
if (-not (Test-Path -LiteralPath $hostPath)) { & (Join-Path $project 'bridge\Build-ReactDesktopHost.ps1') -ProjectRoot $project }
$arguments = @()
if ($PreviewRoot) { $arguments += '--preview-root=' + $PreviewRoot }
Start-Process -FilePath $hostPath -ArgumentList $arguments

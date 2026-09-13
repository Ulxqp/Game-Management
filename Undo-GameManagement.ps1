$ErrorActionPreference = 'SilentlyContinue'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$backupPath = Join-Path $root 'install-backup.json'
$statePath = Join-Path $root 'runtime-state.json'
$timerStatePath = Join-Path $root 'timer-state.json'
$engineEnabledPath = Join-Path $root 'engine-enabled.flag'
$managementPlanGuid = 'c8b1a303-89f5-4b03-ae3f-10b46a186527'
$balancedGuid = '381b4222-f694-41f0-9685-ff5bb260df2e'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

function Set-DisplayBrightness([int]$percent) {
    try {
        $monitor = Get-CimInstance -Namespace root/WMI -ClassName WmiMonitorBrightness -ErrorAction Stop |
            Where-Object Active | Select-Object -First 1
        $method = Get-CimInstance -Namespace root/WMI -ClassName WmiMonitorBrightnessMethods -ErrorAction Stop |
            Where-Object InstanceName -eq $monitor.InstanceName | Select-Object -First 1
        if ($null -eq $monitor -or $null -eq $method) { return $false }
        Invoke-CimMethod -InputObject $method -MethodName WmiSetBrightness -Arguments @{
            Timeout = [uint32]1
            Brightness = [byte]$percent
        } -ErrorAction Stop | Out-Null
        return $true
    } catch { return $false }
}

function Restore-SavedPriorities($savedPriorities) {
    $allowed = @('Idle','BelowNormal','Normal','AboveNormal','High')
    foreach ($saved in @($savedPriorities)) {
        try {
            if ([string]$saved.PriorityClass -notin $allowed) { continue }
            $process = Get-Process -Id ([int]$saved.ProcessId) -ErrorAction Stop
            if ($process.ProcessName -eq [string]$saved.ProcessName) {
                $process.PriorityClass = [string]$saved.PriorityClass
            }
        } catch {}
    }
}

$watcherPath = Join-Path $root 'GameManagement.ps1'
$watcherPattern = [regex]::Escape($watcherPath)
$watchers = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Name -in @('powershell.exe','pwsh.exe') -and
        $_.CommandLine -match $watcherPattern -and
        $_.ProcessId -ne $PID
    }
foreach ($watcher in $watchers) { Stop-Process -Id $watcher.ProcessId -Force }

$restoreGuid = $balancedGuid
$backup = $null
if (Test-Path -LiteralPath $backupPath) {
    $backup = Get-Content -Raw -LiteralPath $backupPath | ConvertFrom-Json
    if ($backup.OriginalPowerScheme) { $restoreGuid = $backup.OriginalPowerScheme }
}
if (Test-Path -LiteralPath $statePath) {
    $state = Get-Content -Raw -LiteralPath $statePath | ConvertFrom-Json
    if ($state.OriginalPowerScheme) { $restoreGuid = $state.OriginalPowerScheme }
    if ($null -ne $state.OriginalBrightness) {
        Set-DisplayBrightness ([int]$state.OriginalBrightness) | Out-Null
    }
    Restore-SavedPriorities @($state.OriginalPriorities)
}
powercfg /setactive $restoreGuid | Out-Null

if ($backup -and $backup.RunExisted) {
    New-Item -Path $runKey -Force | Out-Null
    New-ItemProperty -LiteralPath $runKey -Name 'GameManagementEngine' -Value $backup.RunValue -PropertyType String -Force | Out-Null
} else {
    Remove-ItemProperty -LiteralPath $runKey -Name 'GameManagementEngine' -Force
    Remove-ItemProperty -LiteralPath $runKey -Name 'CodexGameManagement' -Force
}

powercfg /delete $managementPlanGuid | Out-Null
Remove-Item -LiteralPath $statePath,$timerStatePath,$engineEnabledPath,$backupPath -Force
Write-Host 'Game Management was removed and the captured Windows settings were restored.' -ForegroundColor Green

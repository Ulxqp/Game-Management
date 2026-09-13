$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$backupPath = Join-Path $root 'install-backup.json'
$boostGuid = 'c8b1a303-89f5-4b03-ae3f-10b46a186527'
$highGuid = 'aa5b4fa5-cac4-4211-b1d5-a151db2f975e'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runName = 'HardwareSquisherEngine'

function Invoke-PowerCfg([string[]]$Arguments) {
    & powercfg.exe @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "powercfg failed: $($Arguments -join ' ')" }
}

function Try-PowerCfg([string[]]$Arguments) {
    & powercfg.exe @Arguments 2>$null | Out-Null
    return ($LASTEXITCODE -eq 0)
}

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

if (-not (Test-Path -LiteralPath $backupPath)) {
    $activeLine = powercfg /getactivescheme
    $originalPowerScheme = if ($activeLine -match '([0-9a-fA-F-]{36})') { $Matches[1].ToLowerInvariant() } else { '381b4222-f694-41f0-9685-ff5bb260df2e' }
    $oldRun = (Get-ItemProperty -LiteralPath $runKey -Name $runName -ErrorAction SilentlyContinue).$runName
    @{
        BackupVersion = 2
        CreatedAt = (Get-Date).ToString('o')
        OriginalPowerScheme = $originalPowerScheme
        RunExisted = ($null -ne $oldRun)
        RunValue = $oldRun
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $backupPath -Encoding UTF8
}

$watcher = Join-Path $root 'HardwareSquisher.ps1'
$command = "powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$watcher`""
$watcherPattern = [regex]::Escape($watcher)
$watchers = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Name -in @('powershell.exe','pwsh.exe') -and
        $_.CommandLine -match $watcherPattern -and
        $_.ProcessId -ne $PID
    }
foreach ($runningWatcher in $watchers) { Stop-Process -Id $runningWatcher.ProcessId -Force }

$statePath = Join-Path $root 'runtime-state.json'
$timerStatePath = Join-Path $root 'timer-state.json'
$engineEnabledPath = Join-Path $root 'engine-enabled.flag'
$runtimeState = $null
if (Test-Path -LiteralPath $statePath) {
    $runtimeState = Get-Content -Raw -LiteralPath $statePath | ConvertFrom-Json
}
$activeLine = powercfg /getactivescheme
if ($activeLine -match [regex]::Escape($boostGuid)) {
    $restoreGuid = $null
    if ($runtimeState) { $restoreGuid = $runtimeState.OriginalPowerScheme }
    if (-not $restoreGuid -and (Test-Path -LiteralPath $backupPath)) {
        $restoreGuid = (Get-Content -Raw -LiteralPath $backupPath | ConvertFrom-Json).OriginalPowerScheme
    }
    if (-not $restoreGuid) { $restoreGuid = '381b4222-f694-41f0-9685-ff5bb260df2e' }
    Invoke-PowerCfg -Arguments @('/setactive', $restoreGuid)
}
if ($runtimeState -and $null -ne $runtimeState.OriginalBrightness) {
    if (-not (Set-DisplayBrightness ([int]$runtimeState.OriginalBrightness))) {
        Write-Warning "Previous brightness $($runtimeState.OriginalBrightness)% could not be restored during reinstall."
    }
}
if ($runtimeState) { Restore-SavedPriorities @($runtimeState.OriginalPriorities) }
Remove-Item -LiteralPath $statePath -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $timerStatePath -Force -ErrorAction SilentlyContinue

$existingPlans = powercfg /list
if (($existingPlans -join "`n") -notmatch [regex]::Escape($boostGuid)) {
    if (-not (Try-PowerCfg -Arguments @('/duplicatescheme', $highGuid, $boostGuid))) {
        $balancedGuid = '381b4222-f694-41f0-9685-ff5bb260df2e'
        if (-not (Try-PowerCfg -Arguments @('/duplicatescheme', $balancedGuid, $boostGuid))) {
            throw 'Windows could not create a compatible Hardware Squisher power plan.'
        }
    }
}
Invoke-PowerCfg -Arguments @('/changename', $boostGuid, 'Hardware Squisher', 'Safe plugged-in gaming performance; removed by Undo-HardwareSquisher.ps1')
$powerCapabilities = [ordered]@{
    ProcessorMaximum = (Try-PowerCfg -Arguments @('/setacvalueindex', $boostGuid, 'SUB_PROCESSOR', 'PROCTHROTTLEMAX', '100'))
    ProcessorBoostMode = (Try-PowerCfg -Arguments @('/setacvalueindex', $boostGuid, 'SUB_PROCESSOR', 'PERFBOOSTMODE', '2'))
    PcieLinkState = (Try-PowerCfg -Arguments @('/setacvalueindex', $boostGuid, 'SUB_PCIEXPRESS', 'ASPM', '0'))
}
if (-not (Try-PowerCfg -Arguments @('/query', $boostGuid))) {
    throw 'The Hardware Squisher power plan could not be verified.'
}
$compatibilityPath = Join-Path $root 'HARDWARE-COMPATIBILITY.txt'
Add-Content -LiteralPath $compatibilityPath -Encoding UTF8 -Value @(
    '',
    'Windows power-plan capabilities:',
    "- Processor maximum: $($powerCapabilities.ProcessorMaximum)",
    "- Processor boost mode: $($powerCapabilities.ProcessorBoostMode)",
    "- PCIe link-state control: $($powerCapabilities.PcieLinkState)",
    '',
    'Unsupported optional settings are skipped so vendor firmware remains in control.'
)

New-Item -Path $runKey -Force | Out-Null
New-ItemProperty -LiteralPath $runKey -Name $runName -Value $command -PropertyType String -Force | Out-Null
Remove-ItemProperty -LiteralPath $runKey -Name 'CodexGameBoost' -Force -ErrorAction SilentlyContinue
Set-Content -LiteralPath $engineEnabledPath -Value 'enabled' -Encoding ASCII
Start-Process -FilePath 'powershell.exe' -WindowStyle Hidden -ArgumentList @('-NoProfile','-WindowStyle','Hidden','-ExecutionPolicy','Bypass','-File',$watcher)
Start-Sleep -Seconds 1
Write-Host 'Hardware Squisher installed and running with normal user permissions.' -ForegroundColor Green
Write-Host 'GPU clocks, drivers, firmware, and vendor performance modes remain untouched.'

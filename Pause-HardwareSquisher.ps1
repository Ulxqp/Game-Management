$ErrorActionPreference = 'SilentlyContinue'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$statePath = Join-Path $root 'runtime-state.json'
$timerStatePath = Join-Path $root 'timer-state.json'
$engineEnabledPath = Join-Path $root 'engine-enabled.flag'
$logPath = Join-Path $root 'HardwareSquisher.log'
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

$watchers = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Name -in @('powershell.exe','pwsh.exe') -and
        $_.CommandLine -like '*HardwareSquisher.ps1*' -and
        $_.CommandLine -notlike '*Pause-HardwareSquisher.ps1*' -and
        $_.CommandLine -notlike '*Install-HardwareSquisher.ps1*' -and
        $_.ProcessId -ne $PID
    }
foreach ($watcher in $watchers) { Stop-Process -Id $watcher.ProcessId -Force }

$restoreGuid = $balancedGuid
$state = $null
if (Test-Path -LiteralPath $statePath) {
    $state = Get-Content -Raw -LiteralPath $statePath | ConvertFrom-Json
    if ($state.OriginalPowerScheme -match '^[0-9a-fA-F-]{36}$') {
        $restoreGuid = [string]$state.OriginalPowerScheme
    }
}

powercfg.exe /setactive $restoreGuid | Out-Null
if ($state -and $null -ne $state.OriginalBrightness) {
    Set-DisplayBrightness ([int]$state.OriginalBrightness) | Out-Null
}
if ($state) { Restore-SavedPriorities @($state.OriginalPriorities) }
Remove-Item -LiteralPath $statePath -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $timerStatePath -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $engineEnabledPath -Force -ErrorAction SilentlyContinue
Remove-ItemProperty -LiteralPath $runKey -Name 'HardwareSquisherEngine' -Force -ErrorAction SilentlyContinue
Remove-ItemProperty -LiteralPath $runKey -Name 'CodexGameBoost' -Force -ErrorAction SilentlyContinue
Add-Content -LiteralPath $logPath -Value "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') Hardware Squisher paused; restored plan $restoreGuid" -Encoding UTF8

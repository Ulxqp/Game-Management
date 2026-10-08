param([switch]$TimerStateOnly)

$ErrorActionPreference = 'SilentlyContinue'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$statePath = Join-Path $root 'runtime-state.json'
$timerStatePath = Join-Path $root 'timer-state.json'
$workTimerStatePath = Join-Path $root 'work-timer-state.json'
$engineEnabledPath = Join-Path $root 'engine-enabled.flag'
$logPath = Join-Path $root 'GameManagement.log'
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

function Freeze-TimerState([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return }
    try {
        $timer = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
        if ([bool]$timer.Paused -and $null -ne $timer.RemainingSeconds) { return }
        $deadline = [datetime]::Parse([string]$timer.Deadline, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind)
        $remainingSeconds = [Math]::Max(0, [Math]::Ceiling(($deadline.ToLocalTime() - (Get-Date)).TotalSeconds))
        $timer | Add-Member -NotePropertyName Paused -NotePropertyValue $true -Force
        $timer | Add-Member -NotePropertyName RemainingSeconds -NotePropertyValue $remainingSeconds -Force
        $timer | Add-Member -NotePropertyName PausedAt -NotePropertyValue (Get-Date).ToString('o') -Force
        $temporaryPath = "$path.pause.tmp"
        $backupPath = "$path.pause.bak"
        [IO.File]::WriteAllText($temporaryPath, ($timer | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
        if (Test-Path -LiteralPath $path) {
            try { [IO.File]::Replace($temporaryPath, $path, $backupPath) }
            catch {
                [IO.File]::Copy($temporaryPath, $path, $true)
                Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
            }
            Remove-Item -LiteralPath $backupPath -Force -ErrorAction SilentlyContinue
        }
        else { [IO.File]::Move($temporaryPath, $path) }
        Add-Content -LiteralPath $logPath -Value "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') Timer paused with $remainingSeconds seconds left in $($timer.Phase), cycle $($timer.Cycle)" -Encoding UTF8
    } catch {
        Remove-Item -LiteralPath "$path.pause.tmp" -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath "$path.pause.bak" -Force -ErrorAction SilentlyContinue
        Add-Content -LiteralPath $logPath -Value "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') Timer could not be frozen: $($_.Exception.Message)" -Encoding UTF8
    }
}

if ($TimerStateOnly) {
    Freeze-TimerState $timerStatePath
    Freeze-TimerState $workTimerStatePath
    exit 0
}

$watchers = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Name -in @('powershell.exe','pwsh.exe') -and
        $_.CommandLine -like '*GameManagement.ps1*' -and
        $_.CommandLine -notlike '*Pause-GameManagement.ps1*' -and
        $_.CommandLine -notlike '*Install-GameManagement.ps1*' -and
        $_.ProcessId -ne $PID
    }
foreach ($watcher in $watchers) { Stop-Process -Id $watcher.ProcessId -Force }
Freeze-TimerState $timerStatePath
Freeze-TimerState $workTimerStatePath

$restoreGuid = $balancedGuid
$state = $null
if (Test-Path -LiteralPath $statePath) {
    $state = Get-Content -Raw -LiteralPath $statePath | ConvertFrom-Json
    if ($state.OriginalPowerScheme -match '^[0-9a-fA-F-]{36}$') {
        $restoreGuid = [string]$state.OriginalPowerScheme
    }
}

if ($state) {
    powercfg.exe /setactive $restoreGuid | Out-Null
    if ($null -ne $state.OriginalBrightness) {
        Set-DisplayBrightness ([int]$state.OriginalBrightness) | Out-Null
    }
    Restore-SavedPriorities @($state.OriginalPriorities)
}
Remove-Item -LiteralPath $statePath -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $engineEnabledPath -Force -ErrorAction SilentlyContinue
Remove-ItemProperty -LiteralPath $runKey -Name 'GameManagementEngine' -Force -ErrorAction SilentlyContinue
Remove-ItemProperty -LiteralPath $runKey -Name 'CodexGameManagement' -Force -ErrorAction SilentlyContinue
$pauseLog = if ($state) { "Game Management paused; restored plan $restoreGuid; timer preserved" } else { 'Game Management paused; timer preserved' }
Add-Content -LiteralPath $logPath -Value "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') $pauseLog" -Encoding UTF8

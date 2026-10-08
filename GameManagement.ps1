param(
    [switch]$Once,
    [switch]$NoInterface,
    [string]$ConfigPath,
    [string]$MutexName = 'Local\GameManagementWatcher_v1',
    [string]$WakeEventName = 'Local\GameManagementWatcherWake_v1'
)

$ErrorActionPreference = 'SilentlyContinue'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $ConfigPath) { $ConfigPath = Join-Path $root 'settings.json' }
$statePath = Join-Path $root 'runtime-state.json'
$timerStatePath = Join-Path $root 'timer-state.json'
$workTimerStatePath = Join-Path $root 'work-timer-state.json'
$timerCountersPath = Join-Path $root 'timer-counters.json'
$timerResetRequestPath = Join-Path $root 'timer-reset-request.json'
$timerResetAckPath = Join-Path $root 'timer-reset-ack.json'
$modeSwitchRequestPath = Join-Path $root 'mode-switch-request.json'
$modeSwitchAckPath = Join-Path $root 'mode-switch-ack.json'
$performanceStatePath = Join-Path $root 'performance-state.json'
$lastSessionPath = Join-Path $root 'last-session.json'
$sessionHistoryPath = Join-Path $root 'GameSessionHistory.txt'
$logPath = Join-Path $root 'GameManagement.log'
$managementPlanGuid = 'c8b1a303-89f5-4b03-ae3f-10b46a186527'
$balancedGuid = '381b4222-f694-41f0-9685-ff5bb260df2e'
$active = $false
$originalGuid = $null
$originalBrightness = $null
$lastIdleBrightness = $null
$lastIdleBrightnessRefresh = [datetime]::MinValue
$idleBrightnessRefreshIntervalSeconds = 30
$processClassificationCache = @{}
$gameBrightnessPercent = 75
$gameTimerEnabled = $true
$gameTimerMinutes = 30
$breakTimerMinutes = 5
$pauseGameWithEscape = $true
$sessionSummaryEnabled = $true
$timerPhase = 'Game'
$timerDeadline = $null
$timerAlerted = $false
$timerCycle = 0
$gameCompletedCycles = 0
$workCompletedCycles = 0
$sessionStartedAt = $null
$sessionId = $null
$sessionGameNames = @()
$sessionCycleStart = 0
$activeMode = 'game'
$mutex = $null
$wakeEvent = $null

Add-Type -AssemblyName System.Windows.Forms
if (-not ('GameManagement.GameInput' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace GameManagement {
    public static class GameInput {
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint attach, uint attachTo, bool value);
        [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr hWnd, int command);
        [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);
    }
}
'@
}

function Write-Log([string]$message) {
    Add-Content -LiteralPath $logPath -Value "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') $message" -Encoding UTF8
}

function Load-CycleCounters {
    $script:gameCompletedCycles = 0
    $script:workCompletedCycles = 0
    if (-not (Test-Path -LiteralPath $timerCountersPath)) { return }
    try {
        $saved = Get-Content -Raw -LiteralPath $timerCountersPath | ConvertFrom-Json
        if ($null -ne $saved.Game) { $script:gameCompletedCycles = [Math]::Max(0, [int]$saved.Game) }
        if ($null -ne $saved.Work) { $script:workCompletedCycles = [Math]::Max(0, [int]$saved.Work) }
    } catch { Write-Log "Cycle counters could not be loaded: $($_.Exception.Message)" }
}

function Save-CycleCounters {
    $contents = [ordered]@{
        Game = [Math]::Max(0, [int]$script:gameCompletedCycles)
        Work = [Math]::Max(0, [int]$script:workCompletedCycles)
        UpdatedAt = (Get-Date).ToString('o')
    } | ConvertTo-Json
    $temporaryPath = "$timerCountersPath.tmp"
    $backupPath = "$timerCountersPath.bak"
    try {
        [IO.File]::WriteAllText($temporaryPath, $contents, [Text.UTF8Encoding]::new($false))
        if (Test-Path -LiteralPath $timerCountersPath) {
            try { [IO.File]::Replace($temporaryPath, $timerCountersPath, $backupPath) }
            catch {
                [IO.File]::Copy($temporaryPath, $timerCountersPath, $true)
                Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
            }
            Remove-Item -LiteralPath $backupPath -Force -ErrorAction SilentlyContinue
        } else { [IO.File]::Move($temporaryPath, $timerCountersPath) }
    } finally {
        Remove-Item -LiteralPath $temporaryPath,$backupPath -Force -ErrorAction SilentlyContinue
    }
}

function Get-CompletedCycles([string]$mode) {
    if ($mode -eq 'work') { return [Math]::Max(0, [int]$script:workCompletedCycles) }
    return [Math]::Max(0, [int]$script:gameCompletedCycles)
}

function Set-CompletedCycles([string]$mode, [int]$value) {
    $bounded = [Math]::Max(0, $value)
    if ($mode -eq 'work') { $script:workCompletedCycles = $bounded }
    else { $script:gameCompletedCycles = $bounded }
    if ($mode -eq $script:activeMode) { $script:timerCycle = $bounded }
    Save-CycleCounters
}

function Set-BreakInterface([bool]$show) {
    $app = Join-Path $root 'GameManagement.exe'
    if (-not (Test-Path -LiteralPath $app)) { return }
    $argument = if ($show) { '--break-ui-show' } else { '--break-ui-hide' }
    Start-Process -FilePath $app -ArgumentList $argument
}

function Start-BackgroundInterface {
    $app = Join-Path $root 'GameManagement.exe'
    if (Test-Path -LiteralPath $app) {
        Start-Process -FilePath $app -ArgumentList '--ui-start-hidden'
    }
}

function Format-SessionDuration([TimeSpan]$duration) {
    $hours = [Math]::Floor($duration.TotalHours)
    $minutes = [Math]::Max(0, [int][Math]::Floor($duration.TotalMinutes % 60))
    if ($hours -gt 0) { return "$hours hr $minutes min" }
    if ($minutes -gt 0) { return "$minutes min" }
    return "$([Math]::Max(1, [int][Math]::Ceiling($duration.TotalSeconds))) sec"
}

function Write-GameSessionSummary([bool]$showDialog = $true) {
    $endedAt = Get-Date
    $startedAt = $script:sessionStartedAt
    if ($null -eq $startedAt -and (Test-Path -LiteralPath $statePath)) {
        try { $startedAt = [datetime](Get-Content -Raw -LiteralPath $statePath | ConvertFrom-Json).ManagementStarted } catch {}
    }
    if ($null -eq $startedAt) { $startedAt = $endedAt }
    $duration = $endedAt - $startedAt
    $sessionLabel = if ($script:activeMode -eq 'work') { 'Work' } else { 'Game' }
    $timeLabel = if ($script:activeMode -eq 'work') { 'Total work time' } else { 'Total game time' }
    if ($duration.TotalSeconds -le 120) {
        Remove-Item -LiteralPath $lastSessionPath -Force -ErrorAction SilentlyContinue
        Write-Log "$sessionLabel session not saved because it lasted 2 minutes or less ($(Format-SessionDuration $duration))"
        return
    }
    $gameName = if (@($script:sessionGameNames).Count) { @($script:sessionGameNames) -join ', ' } else { 'Detected game' }
    $currentSessionId = [string]$script:sessionId
    if ([string]::IsNullOrWhiteSpace($currentSessionId)) {
        $currentSessionId = "$($script:activeMode)-$($startedAt.Ticks)"
    }
    $cycles = if ($script:gameTimerEnabled) { [Math]::Max(0, [int]$script:timerCycle - [int]$script:sessionCycleStart) } else { 0 }
    $peakCpu = $null
    $peakGpu = $null
    if (Test-Path -LiteralPath $performanceStatePath) {
        try {
            $performance = Get-Content -Raw -LiteralPath $performanceStatePath | ConvertFrom-Json
            if ($null -ne $performance.PeakCpuC) { $peakCpu = [double]$performance.PeakCpuC }
            if ($null -ne $performance.PeakGpuC) { $peakGpu = [double]$performance.PeakGpuC }
        } catch {}
    }
    $degreeC = [char]0x00B0 + 'C'
    $peakCpuText = if ($null -eq $peakCpu) { 'Unavailable' } else { "$([Math]::Round($peakCpu, 1))$degreeC" }
    $peakGpuText = if ($null -eq $peakGpu) { 'Unavailable' } else { "$([Math]::Round($peakGpu, 1))$degreeC" }
    $summary = [ordered]@{
        SessionId = $currentSessionId
        Mode = $script:activeMode
        Game = $gameName
        StartedAt = $startedAt.ToString('yyyy-MM-dd HH:mm:ss')
        EndedAt = $endedAt.ToString('yyyy-MM-dd HH:mm:ss')
        DurationText = Format-SessionDuration $duration
        TotalHours = [Math]::Round($duration.TotalHours, 3)
        Cycles = $cycles
        PeakCpu = $peakCpuText
        PeakGpu = $peakGpuText
    }
    $historyLines = @(
        '------------------------------------------------------------'
        "SessionId: $currentSessionId"
        "Date: $($endedAt.ToString('yyyy-MM-dd'))"
        "Mode: $script:activeMode"
        "Applications: $gameName"
        "Started: $($startedAt.ToString('yyyy-MM-dd HH:mm:ss'))"
        "Finished: $($endedAt.ToString('yyyy-MM-dd HH:mm:ss'))"
        "${timeLabel}: $(Format-SessionDuration $duration) ($([Math]::Round($duration.TotalHours, 3)) hours)"
        "Timer cycles: $cycles"
        "Peak CPU temperature: $peakCpuText"
        "Peak GPU temperature: $peakGpuText"
    )
    $existingHistory = if (Test-Path -LiteralPath $sessionHistoryPath) { [IO.File]::ReadAllText($sessionHistoryPath) } else { '' }
    $alreadyWritten = $existingHistory.Contains("SessionId: $currentSessionId")
    if (-not $alreadyWritten) {
        $historyBlock = ($historyLines -join [Environment]::NewLine) + [Environment]::NewLine
        $separator = if ($existingHistory.Length -gt 0 -and -not $existingHistory.EndsWith("`n")) { [Environment]::NewLine } else { '' }
        $historyText = $existingHistory + $separator + $historyBlock
        $temporaryHistoryPath = Join-Path $root "GameSessionHistory.$currentSessionId.tmp"
        $backupHistoryPath = Join-Path $root "GameSessionHistory.$currentSessionId.bak"
        [IO.File]::WriteAllText($temporaryHistoryPath, $historyText, [Text.UTF8Encoding]::new($false))
        if (Test-Path -LiteralPath $sessionHistoryPath) {
            try { [IO.File]::Replace($temporaryHistoryPath, $sessionHistoryPath, $backupHistoryPath) }
            catch {
                [IO.File]::Copy($temporaryHistoryPath, $sessionHistoryPath, $true)
                Remove-Item -LiteralPath $temporaryHistoryPath -Force -ErrorAction SilentlyContinue
            }
            Remove-Item -LiteralPath $backupHistoryPath -Force -ErrorAction SilentlyContinue
        } else {
            try { [IO.File]::Move($temporaryHistoryPath, $sessionHistoryPath) }
            catch {
                [IO.File]::Copy($temporaryHistoryPath, $sessionHistoryPath, $true)
                Remove-Item -LiteralPath $temporaryHistoryPath -Force -ErrorAction SilentlyContinue
            }
        }
    }
    $summary | ConvertTo-Json | Set-Content -LiteralPath $lastSessionPath -Encoding UTF8 -ErrorAction Stop
    Write-Log "$script:activeMode session saved for $gameName; duration $(Format-SessionDuration $duration); cycles $cycles; peak CPU temperature $peakCpuText; peak GPU temperature $peakGpuText"
    $app = Join-Path $root 'GameManagement.exe'
    if ($showDialog -and $script:sessionSummaryEnabled -and (Test-Path -LiteralPath $app)) { Start-Process -FilePath $app -ArgumentList '--session-summary' }
}

function Recover-InterruptedSession($stale) {
    try {
        if ($null -eq $stale -or [string]::IsNullOrWhiteSpace([string]$stale.ManagementStarted)) { return }
        $script:activeMode = if ([string]$stale.Mode -eq 'work') { 'work' } else { 'game' }
        $script:sessionStartedAt = [datetime]$stale.ManagementStarted
        $script:sessionId = [string]$stale.SessionId
        $script:sessionGameNames = @($stale.Applications)
        $script:sessionCycleStart = if ($null -ne $stale.SessionCycleStart) { [Math]::Max(0, [int]$stale.SessionCycleStart) } else { 0 }
        $script:gameTimerEnabled = if ($null -ne $stale.TimerEnabled) { [bool]$stale.TimerEnabled } else { $true }
        $script:timerCycle = if ($null -ne $stale.TimerCycle) { [Math]::Max(0, [int]$stale.TimerCycle) } else { Get-CompletedCycles $script:activeMode }
        $savedTimerPath = if ($script:activeMode -eq 'work') { $workTimerStatePath } else { $timerStatePath }
        if (Test-Path -LiteralPath $savedTimerPath) {
            try {
                $savedTimer = Get-Content -Raw -LiteralPath $savedTimerPath | ConvertFrom-Json
                if ($null -ne $savedTimer.Cycle) { $script:timerCycle = [Math]::Max(0, [int]$savedTimer.Cycle) }
            } catch {}
        }
        Write-GameSessionSummary $false
        Write-Log "Recovered interrupted $script:activeMode session $script:sessionId"
    } catch {
        Write-Log "Interrupted session recovery failed: $($_.Exception.Message)"
    } finally {
        $script:sessionStartedAt = $null
        $script:sessionId = $null
        $script:sessionGameNames = @()
        $script:sessionCycleStart = 0
        $script:timerCycle = Get-CompletedCycles $script:activeMode
    }
}

function Send-GameEscape($gameProcesses, [string]$reason) {
    if (-not $script:pauseGameWithEscape) { return $false }
    $target = @(
        foreach ($candidate in @($gameProcesses)) {
            try {
                $process = Get-Process -Id ([int]$candidate.Id) -ErrorAction Stop
                $process.Refresh()
                if ($process.MainWindowHandle -ne [IntPtr]::Zero) { $process }
            } catch {}
        }
    ) | Sort-Object StartTime | Select-Object -First 1

    if ($null -eq $target) {
        Write-Log "Escape skipped safely for $reason because no detected game window was available"
        return $false
    }

    $targetHandle = [IntPtr]$target.MainWindowHandle
    $foregroundHandle = [GameManagement.GameInput]::GetForegroundWindow()
    $foregroundProcessId = [uint32]0
    $targetProcessId = [uint32]0
    $foregroundThread = if ($foregroundHandle -ne [IntPtr]::Zero) { [GameManagement.GameInput]::GetWindowThreadProcessId($foregroundHandle, [ref]$foregroundProcessId) } else { [uint32]0 }
    $targetThread = [GameManagement.GameInput]::GetWindowThreadProcessId($targetHandle, [ref]$targetProcessId)
    $currentThread = [GameManagement.GameInput]::GetCurrentThreadId()
    $attachedForeground = $false
    $attachedTarget = $false

    try {
        if ($foregroundThread -ne 0 -and $foregroundThread -ne $currentThread) {
            $attachedForeground = [GameManagement.GameInput]::AttachThreadInput($currentThread, $foregroundThread, $true)
        }
        if ($targetThread -ne 0 -and $targetThread -ne $currentThread) {
            $attachedTarget = [GameManagement.GameInput]::AttachThreadInput($currentThread, $targetThread, $true)
        }
        [GameManagement.GameInput]::ShowWindowAsync($targetHandle, 9) | Out-Null
        [GameManagement.GameInput]::BringWindowToTop($targetHandle) | Out-Null
        [GameManagement.GameInput]::SetForegroundWindow($targetHandle) | Out-Null
    } finally {
        if ($attachedTarget) { [GameManagement.GameInput]::AttachThreadInput($currentThread, $targetThread, $false) | Out-Null }
        if ($attachedForeground) { [GameManagement.GameInput]::AttachThreadInput($currentThread, $foregroundThread, $false) | Out-Null }
    }

    Start-Sleep -Milliseconds 150
    if ([GameManagement.GameInput]::GetForegroundWindow() -ne $targetHandle) {
        Write-Log "Escape skipped safely for $reason because Windows did not focus $($target.ProcessName)"
        return $false
    }

    [GameManagement.GameInput]::keybd_event(0x1B, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 50
    [GameManagement.GameInput]::keybd_event(0x1B, 0, 2, [UIntPtr]::Zero)
    Write-Log "Escape sent to detected game $($target.ProcessName) for $reason"
    return $true
}

function Get-ActiveScheme {
    $line = powercfg /getactivescheme 2>$null
    if ($line -match '([0-9a-fA-F-]{36})') { return $Matches[1].ToLowerInvariant() }
    return $null
}

function Set-Scheme([string]$guid) {
    powercfg /setactive $guid 2>$null | Out-Null
    return ((Get-ActiveScheme) -eq $guid.ToLowerInvariant())
}

function Test-AcPower {
    try {
        $lineStatus = [System.Windows.Forms.SystemInformation]::PowerStatus.PowerLineStatus
        if ($lineStatus -eq [System.Windows.Forms.PowerLineStatus]::Online) { return $true }
        if ($lineStatus -eq [System.Windows.Forms.PowerLineStatus]::Offline) { return $false }
    } catch {}
    try {
        $status = Get-CimInstance -Namespace root/WMI -ClassName BatteryStatus -ErrorAction Stop |
            Select-Object -First 1
        if ($null -ne $status) { return [bool]$status.PowerOnline }
    } catch {}
    try {
        $battery = Get-CimInstance -ClassName Win32_Battery -ErrorAction Stop | Select-Object -First 1
        if ($null -eq $battery) { return $true }
        return ([int]$battery.BatteryStatus -in @(2,3,6,7,8,9,11))
    } catch {}
    return $false
}

function Get-DisplayBrightness {
    try {
        $monitor = Get-CimInstance -Namespace root/WMI -ClassName WmiMonitorBrightness -ErrorAction Stop |
            Where-Object Active | Select-Object -First 1
        if ($null -ne $monitor) { return [int]$monitor.CurrentBrightness }
    } catch {}
    return $null
}

function Update-IdleBrightness([bool]$force = $false) {
    if (-not $force -and ((Get-Date) - $script:lastIdleBrightnessRefresh).TotalSeconds -lt $script:idleBrightnessRefreshIntervalSeconds) {
        return
    }
    $script:lastIdleBrightnessRefresh = Get-Date
    $idleBrightness = Get-DisplayBrightness
    if ($null -ne $idleBrightness) { $script:lastIdleBrightness = $idleBrightness }
}

function Set-DisplayBrightness([int]$percent) {
    if ($percent -lt 0 -or $percent -gt 100) { return $false }
    try {
        $monitor = Get-CimInstance -Namespace root/WMI -ClassName WmiMonitorBrightness -ErrorAction Stop |
            Where-Object Active | Select-Object -First 1
        if ($null -eq $monitor) { return $false }
        $method = Get-CimInstance -Namespace root/WMI -ClassName WmiMonitorBrightnessMethods -ErrorAction Stop |
            Where-Object InstanceName -eq $monitor.InstanceName | Select-Object -First 1
        if ($null -eq $method) { return $false }
        Invoke-CimMethod -InputObject $method -MethodName WmiSetBrightness -Arguments @{
            Timeout = [uint32]1
            Brightness = [byte]$percent
        } -ErrorAction Stop | Out-Null
        return ((Get-DisplayBrightness) -eq $percent)
    } catch {}
    return $false
}

function Save-State {
    @{
        SessionId = $script:sessionId
        OriginalPowerScheme = $script:originalGuid
        OriginalBrightness = $script:originalBrightness
        ManagementStarted = (Get-Date).ToString('o')
        Mode = $script:activeMode
        Applications = @($script:sessionGameNames)
        TimerEnabled = $script:gameTimerEnabled
        TimerCycle = $script:timerCycle
        SessionCycleStart = $script:sessionCycleStart
    } |
        ConvertTo-Json | Set-Content -LiteralPath $statePath -Encoding UTF8
}

function Clear-State {
    Remove-Item -LiteralPath $statePath -Force -ErrorAction SilentlyContinue
}

function Save-GameTimerState {
    $phaseMinutes = if ($script:timerPhase -eq 'Break') { $script:breakTimerMinutes } else { $script:gameTimerMinutes }
    @{
        Phase = $script:timerPhase
        StartedAt = $script:timerDeadline.AddMinutes(-$phaseMinutes).ToString('o')
        Deadline = $script:timerDeadline.ToString('o')
        Minutes = $phaseMinutes
        Alerted = $script:timerAlerted
        Cycle = $script:timerCycle
        Mode = $script:activeMode
        Paused = $false
        RemainingSeconds = $null
        Status = if ($script:timerPhase -eq 'Break') { 'Break' } else { 'Running' }
        Revision = [guid]::NewGuid().ToString('N')
    } | ConvertTo-Json | Set-Content -LiteralPath $(if ($script:activeMode -eq 'work') { $workTimerStatePath } else { $timerStatePath }) -Encoding UTF8
}

function Freeze-ActiveTimerState {
    if (-not $script:gameTimerEnabled -or $null -eq $script:timerDeadline) { return }
    $remainingSeconds = [Math]::Max(0, [Math]::Ceiling(($script:timerDeadline - (Get-Date)).TotalSeconds))
    $phaseMinutes = if ($script:timerPhase -eq 'Break') { $script:breakTimerMinutes } else { $script:gameTimerMinutes }
    $paused = [ordered]@{
        Phase = $script:timerPhase
        StartedAt = $script:timerDeadline.AddMinutes(-$phaseMinutes).ToString('o')
        Deadline = $script:timerDeadline.ToString('o')
        Minutes = $phaseMinutes
        Alerted = $false
        Cycle = [Math]::Max(0, [int]$script:timerCycle)
        Mode = $script:activeMode
        Paused = $true
        RemainingSeconds = $remainingSeconds
        PausedAt = (Get-Date).ToString('o')
        Status = 'Paused'
        Revision = [guid]::NewGuid().ToString('N')
    }
    $targetPath = if ($script:activeMode -eq 'work') { $workTimerStatePath } else { $timerStatePath }
    $paused | ConvertTo-Json | Set-Content -LiteralPath $targetPath -Encoding UTF8
    Write-Log "$($script:timerPhase) timer frozen with $remainingSeconds seconds left in cycle $script:timerCycle"
}

function Update-InactiveTimerState([string]$mode, [bool]$resetTimer, [bool]$resetCycles) {
    $path = if ($mode -eq 'work') { $workTimerStatePath } else { $timerStatePath }
    if (-not (Test-Path -LiteralPath $path)) { return }
    try {
        $state = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
        $cycle = if ($resetCycles) { 0 } else { Get-CompletedCycles $mode }
        $state | Add-Member -NotePropertyName Cycle -NotePropertyValue $cycle -Force
        if ($resetTimer) {
            $minutes = if ($mode -eq 'work') { [Math]::Max(1, [int]$config.workTimerMinutes) } else { [Math]::Max(1, [int]$config.gameTimerMinutes) }
            $phase = if ($mode -eq 'work') { 'Work' } else { 'Game' }
            $state | Add-Member -NotePropertyName Phase -NotePropertyValue $phase -Force
            $state | Add-Member -NotePropertyName Minutes -NotePropertyValue $minutes -Force
            $state | Add-Member -NotePropertyName Deadline -NotePropertyValue (Get-Date).AddMinutes($minutes).ToString('o') -Force
            $state | Add-Member -NotePropertyName Paused -NotePropertyValue $true -Force
            $state | Add-Member -NotePropertyName RemainingSeconds -NotePropertyValue ($minutes * 60) -Force
            $state | Add-Member -NotePropertyName Status -NotePropertyValue 'Paused' -Force
        }
        $state | Add-Member -NotePropertyName Revision -NotePropertyValue ([guid]::NewGuid().ToString('N')) -Force
        $state | ConvertTo-Json | Set-Content -LiteralPath $path -Encoding UTF8
    } catch { Write-Log "Timer state reset for $mode failed: $($_.Exception.Message)" }
}

function Process-TimerResetRequest {
    if (-not (Test-Path -LiteralPath $timerResetRequestPath)) { return }
    $request = $null
    try { $request = Get-Content -Raw -LiteralPath $timerResetRequestPath | ConvertFrom-Json } catch {}
    if ($null -eq $request -or [string]::IsNullOrWhiteSpace([string]$request.RequestId)) {
        Remove-Item -LiteralPath $timerResetRequestPath -Force -ErrorAction SilentlyContinue
        return
    }
    $validRequest = $false
    try {
        $expiry = [datetime]::Parse([string]$request.ExpiresUtc, [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
        $owner = Get-Process -Id ([int]$request.RequesterPid) -ErrorAction Stop
        $validRequest = (Get-Date).ToUniversalTime() -lt $expiry -and
            $owner.StartTime.ToUniversalTime().Ticks -eq [long]$request.RequesterStartTicks
    } catch { $validRequest = $false }
    if (-not $validRequest) {
        Remove-Item -LiteralPath $timerResetRequestPath -Force -ErrorAction SilentlyContinue
        return
    }
    $mode = if ([string]$request.Mode -eq 'work') { 'work' } else { 'game' }
    $action = [string]$request.Action
    $resetTimer = $action -in @('reset-timer','reset-all')
    $resetCycles = $action -in @('reset-cycles','reset-all')
    $succeeded = $resetTimer -or $resetCycles
    $message = 'Unsupported timer command.'
    try {
        if (-not $succeeded) { throw $message }
        if ($resetCycles) { Set-CompletedCycles $mode 0 }
        if ($mode -eq $script:activeMode -and $script:active) {
            if ($resetTimer) {
                $script:timerPhase = if ($mode -eq 'work') { 'Work' } else { 'Game' }
                $script:timerDeadline = (Get-Date).AddMinutes($script:gameTimerMinutes)
                $script:timerAlerted = $false
            }
            if ($resetCycles) { $script:timerCycle = 0; $script:sessionCycleStart = 0 }
            Save-GameTimerState
        } else { Update-InactiveTimerState $mode $resetTimer $resetCycles }
        $message = if ($action -eq 'reset-timer') { 'Timer reset.' } elseif ($action -eq 'reset-cycles') { 'Completed cycles reset.' } else { 'Timer and completed cycles reset.' }
        Write-Log "$mode $message"
    } catch {
        $succeeded = $false
        $message = $_.Exception.Message
    }
    [ordered]@{ RequestId = [string]$request.RequestId; Succeeded = $succeeded; Message = $message } |
        ConvertTo-Json | Set-Content -LiteralPath $timerResetAckPath -Encoding UTF8
    Remove-Item -LiteralPath $timerResetRequestPath -Force -ErrorAction SilentlyContinue
}

function Start-GameTimer {
    $activeTimerPath = if ($script:activeMode -eq 'work') { $workTimerStatePath } else { $timerStatePath }
    if (-not $script:gameTimerEnabled -or $script:gameTimerMinutes -lt 1) {
        Remove-Item -LiteralPath $activeTimerPath -Force -ErrorAction SilentlyContinue
        return
    }
    if (Test-Path -LiteralPath $activeTimerPath) {
        try {
            $pausedTimer = Get-Content -Raw -LiteralPath $activeTimerPath | ConvertFrom-Json
            if ([bool]$pausedTimer.Paused -and [string]::Equals([string]$pausedTimer.Mode, $script:activeMode, [StringComparison]::OrdinalIgnoreCase)) {
                $remainingSeconds = [Math]::Max(0, [double]$pausedTimer.RemainingSeconds)
                $script:timerPhase = if ([string]$pausedTimer.Phase -eq 'Break') { 'Break' } elseif ($script:activeMode -eq 'work') { 'Work' } else { 'Game' }
                $script:timerCycle = [Math]::Max(0, [int]$pausedTimer.Cycle)
                $script:timerDeadline = (Get-Date).AddSeconds($remainingSeconds)
                $script:timerAlerted = $false
                Save-GameTimerState
                Write-Log "$($script:timerPhase) timer resumed with $([Math]::Ceiling($remainingSeconds)) seconds left in cycle $script:timerCycle"
                return
            }
        } catch {
            Write-Log "Paused timer could not be resumed: $($_.Exception.Message)"
        }
    }
    $script:timerPhase = if ($script:activeMode -eq 'work') { 'Work' } else { 'Game' }
    $script:timerCycle = Get-CompletedCycles $script:activeMode
    $script:timerDeadline = (Get-Date).AddMinutes($script:gameTimerMinutes)
    $script:timerAlerted = $false
    Save-GameTimerState
    Write-Log "$($script:timerPhase) timer started for $script:gameTimerMinutes minutes"
}

function Update-GameTimer($gameProcesses) {
    if (-not $script:gameTimerEnabled -or $null -eq $script:timerDeadline -or $script:timerAlerted) { return }
    if ((Get-Date) -lt $script:timerDeadline) { return }

    $alertApp = Join-Path $root 'GameManagement.exe'
    if ($script:timerPhase -ne 'Break') {
        Write-Log "$($script:timerPhase) timer finished after $script:gameTimerMinutes minutes; break timer started for $script:breakTimerMinutes minutes"
        $script:timerPhase = 'Break'
        $script:timerDeadline = (Get-Date).AddMinutes($script:breakTimerMinutes)
        $script:timerAlerted = $false
        Save-GameTimerState
        if ($script:activeMode -eq 'game') { Send-GameEscape $gameProcesses 'break start' | Out-Null }
        if (Test-Path -LiteralPath $alertApp) {
            $alertType = if ($script:activeMode -eq 'work') { 'work-finished' } else { 'game-finished' }
            Start-Process -FilePath $alertApp -ArgumentList @('--timer-alert', $alertType, [string]$script:gameTimerMinutes, [string]$script:breakTimerMinutes)
        } else {
            try { [Console]::Beep(880, 700) } catch {}
        }
    } else {
        $script:timerPhase = if ($script:activeMode -eq 'work') { 'Work' } else { 'Game' }
        Write-Log "Break timer finished after $script:breakTimerMinutes minutes; $($script:timerPhase) timer restarted for $script:gameTimerMinutes minutes"
        $script:timerCycle++
        Set-CompletedCycles $script:activeMode $script:timerCycle
        $script:timerDeadline = (Get-Date).AddMinutes($script:gameTimerMinutes)
        $script:timerAlerted = $false
        Save-GameTimerState
        if ($script:activeMode -eq 'game') { Send-GameEscape $gameProcesses 'break end' | Out-Null }
        if (Test-Path -LiteralPath $alertApp) {
            $alertType = if ($script:activeMode -eq 'work') { 'work-break-finished' } else { 'break-finished' }
            Start-Process -FilePath $alertApp -ArgumentList @('--timer-alert', $alertType, [string]$script:breakTimerMinutes, [string]$script:gameTimerMinutes)
        } else {
            try { [Console]::Beep(1047, 700) } catch {}
        }
    }
}

function Clear-GameTimer {
    $activeTimerPath = if ($script:activeMode -eq 'work') { $workTimerStatePath } else { $timerStatePath }
    Remove-Item -LiteralPath $activeTimerPath -Force -ErrorAction SilentlyContinue
    $script:timerDeadline = $null
    $script:timerAlerted = $false
    $script:timerPhase = 'Game'
    $script:timerCycle = Get-CompletedCycles $script:activeMode
}

function Remove-StaleTimerState([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return }
    try {
        $savedTimer = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
        if ([bool]$savedTimer.Paused -and $null -ne $savedTimer.RemainingSeconds) { return }
    } catch {}
    Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
}

function Normalize-Folder([string]$path) {
    try { return [IO.Path]::GetFullPath($path).TrimEnd('\') + '\' }
    catch { return $null }
}

function Get-GameFolders($config) {
    $folders = [Collections.Generic.List[string]]::new()
    foreach ($folder in @($config.gameFolders)) {
        $normalized = Normalize-Folder $folder
        if ($normalized -and -not $folders.Contains($normalized)) { $folders.Add($normalized) }
    }

    $steamVdf = 'C:\Program Files (x86)\Steam\steamapps\libraryfolders.vdf'
    if (Test-Path -LiteralPath $steamVdf) {
        $vdfText = Get-Content -Raw -LiteralPath $steamVdf
        foreach ($match in [regex]::Matches($vdfText, '"path"\s+"([^"]+)"')) {
            $library = $match.Groups[1].Value -replace '\\\\','\'
            $normalized = Normalize-Folder (Join-Path $library 'steamapps\common')
            if ($normalized -and -not $folders.Contains($normalized)) { $folders.Add($normalized) }
        }
    }
    return @($folders)
}

function Get-GameApplications($config) {
    $applications = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($application in @($config.gameApps)) {
        try {
            $fullPath = [IO.Path]::GetFullPath([string]$application)
            if (Test-Path -LiteralPath $fullPath -PathType Leaf) { [void]$applications.Add($fullPath) }
        } catch {}
    }
    return @($applications)
}

function Find-RunningGames($folders, $selectedPaths, $excluded) {
    $selected = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($selectedPath in @($selectedPaths)) {
        try { [void]$selected.Add([IO.Path]::GetFullPath([string]$selectedPath)) } catch {}
    }
    $matches = [Collections.Generic.List[object]]::new()
    $seenProcessIds = [Collections.Generic.HashSet[int]]::new()
    foreach ($process in @(Get-Process)) {
        try {
            if ($process.HasExited) { continue }
            $processId = [int]$process.Id
            [void]$seenProcessIds.Add($processId)
            $processName = [string]$process.ProcessName
            $startTicks = try { [long]$process.StartTime.Ticks } catch { [long]0 }
            $cached = $script:processClassificationCache[$processId]
            $isGame = $false
            if ($null -ne $cached -and $cached.ProcessName -eq $processName -and $cached.StartTicks -eq $startTicks) {
                $isGame = [bool]$cached.IsGame
            } else {
                $path = [IO.Path]::GetFullPath([string]$process.Path)
                if ($path -and $selected.Contains($path)) {
                    $isGame = $true
                } elseif (-not $excluded.Contains($processName)) {
                    if ($path) {
                        foreach ($folder in $folders) {
                            if ($path.StartsWith([string]$folder, [StringComparison]::OrdinalIgnoreCase)) {
                                $isGame = $true
                                break
                            }
                        }
                    }
                }
                $script:processClassificationCache[$processId] = [pscustomobject]@{
                    ProcessName = $processName
                    StartTicks = $startTicks
                    IsGame = $isGame
                }
            }
            if ($isGame) { $matches.Add($process) }
        } catch {}
    }
    foreach ($cachedProcessId in @($script:processClassificationCache.Keys)) {
        if (-not $seenProcessIds.Contains([int]$cachedProcessId)) {
            $script:processClassificationCache.Remove($cachedProcessId)
        }
    }
    return @($matches)
}

function Find-RunningWorkApps($selectedPaths) {
    $selected = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($selectedPath in @($selectedPaths)) {
        try {
            $fullPath = [IO.Path]::GetFullPath([string]$selectedPath)
            if (Test-Path -LiteralPath $fullPath -PathType Leaf) { [void]$selected.Add($fullPath) }
        } catch {}
    }
    if ($selected.Count -eq 0) { return @() }

    $matches = [Collections.Generic.List[object]]::new()
    foreach ($process in @(Get-Process)) {
        try {
            if ($process.HasExited) { continue }
            $path = [IO.Path]::GetFullPath([string]$process.Path)
            if ($selected.Contains($path)) { $matches.Add($process) }
        } catch {}
    }
    return @($matches)
}

function Start-Management($gameProcesses) {
    $script:sessionStartedAt = Get-Date
    $script:sessionId = [guid]::NewGuid().ToString('N')
    $script:sessionGameNames = @($gameProcesses.ProcessName | Sort-Object -Unique)
    $script:sessionCycleStart = Get-CompletedCycles $script:activeMode
    Remove-Item -LiteralPath $performanceStatePath -Force -ErrorAction SilentlyContinue
    if (-not $NoInterface) { Start-BackgroundInterface }
    $script:originalGuid = Get-ActiveScheme
    if (-not $script:originalGuid) { $script:originalGuid = $balancedGuid }
    Update-IdleBrightness $true
    $script:originalBrightness = $script:lastIdleBrightness
    Save-State
    if ($script:activeMode -eq 'work') {
        $script:active = $true
        Start-GameTimer
        Write-Log "Work Management ON; applications: $($gameProcesses.ProcessName -join ', ')"
        return
    }
    if (-not (Set-Scheme $managementPlanGuid)) {
        Write-Log 'Game Management could not activate because the dedicated power plan was unavailable'
        Clear-State
        return
    }
    if ($null -ne $script:originalBrightness) {
        if (Set-DisplayBrightness $script:gameBrightnessPercent) {
            Write-Log "Brightness set to $script:gameBrightnessPercent%; previous brightness $script:originalBrightness%"
        } else {
            Write-Log "Brightness could not be set to $script:gameBrightnessPercent%"
        }
    } else {
        Write-Log 'Brightness control is unavailable for the active display'
    }
    $script:active = $true
    Start-GameTimer
    Write-Log "Game Management ON; previous plan $script:originalGuid; games: $($gameProcesses.ProcessName -join ', ')"
}

function Stop-Management([bool]$showSessionSummary = $false, [bool]$showSummaryDialog = $true, [bool]$preserveTimer = $false) {
    $savedState = $null
    if (Test-Path -LiteralPath $statePath) {
        $savedState = Get-Content -Raw -LiteralPath $statePath | ConvertFrom-Json
    }
    $hasManagedState = $script:active -or ($null -ne $savedState)
    $restoreGuid = $script:originalGuid
    if (-not $restoreGuid -and $savedState) {
        $restoreGuid = $savedState.OriginalPowerScheme
    }
    $restoreBrightness = $script:originalBrightness
    if ($null -eq $restoreBrightness -and $savedState -and $null -ne $savedState.OriginalBrightness) {
        $restoreBrightness = [int]$savedState.OriginalBrightness
    }
    if ($script:activeMode -eq 'work') {
        if ($hasManagedState) { Write-Log 'Work Management OFF; selected applications closed' }
    } elseif ($hasManagedState) {
        if (-not $restoreGuid) { $restoreGuid = $balancedGuid }
        if (Set-Scheme $restoreGuid) { Write-Log "Game Management OFF; restored plan $restoreGuid" }
        else { Write-Log "Game Management OFF but restoring plan $restoreGuid failed" }
        if ($null -ne $restoreBrightness) {
            if (Set-DisplayBrightness $restoreBrightness) { Write-Log "Brightness restored to $restoreBrightness%" }
            else { Write-Log "Brightness restoration to $restoreBrightness% failed" }
        }
    }
    if ($showSessionSummary -and $script:active) { Write-GameSessionSummary $showSummaryDialog }
    Clear-State
    if (-not $preserveTimer) { Clear-GameTimer }
    else {
        $script:timerDeadline = $null
        $script:timerAlerted = $false
    }
    $script:active = $false
    $script:originalGuid = $null
    $script:originalBrightness = $null
    $script:sessionStartedAt = $null
    $script:sessionId = $null
    $script:sessionGameNames = @()
    $script:sessionCycleStart = 0
    if ($null -ne $restoreBrightness) { $script:lastIdleBrightness = $restoreBrightness }
}

function Test-ModeSwitchRequest($request) {
    if ($null -eq $request -or [string]::IsNullOrWhiteSpace([string]$request.RequestId) -or
        -not [string]::Equals([string]$request.Mode, $script:activeMode, [StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }
    try {
        $expiry = [datetime]::Parse([string]$request.ExpiresUtc, [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
        if ((Get-Date).ToUniversalTime() -ge $expiry) { return $false }
        $owner = Get-Process -Id ([int]$request.RequesterPid) -ErrorAction Stop
        return $owner.StartTime.ToUniversalTime().Ticks -eq [long]$request.RequesterStartTicks
    } catch { return $false }
}

try {
    $createdNew = $false
    $mutex = [Threading.Mutex]::new($true, $MutexName, [ref]$createdNew)
    if (-not $createdNew) { exit 0 }
    $wakeEvent = [Threading.EventWaitHandle]::new($false, [Threading.EventResetMode]::AutoReset, $WakeEventName)

    Load-CycleCounters

    if (Test-Path -LiteralPath $statePath) {
        $stale = Get-Content -Raw -LiteralPath $statePath | ConvertFrom-Json
        if ($stale.OriginalPowerScheme -and (Set-Scheme $stale.OriginalPowerScheme)) {
            Write-Log "Recovered previous plan $($stale.OriginalPowerScheme) after an interrupted run"
        }
        if ($null -ne $stale.OriginalBrightness -and (Set-DisplayBrightness ([int]$stale.OriginalBrightness))) {
            Write-Log "Recovered previous brightness $($stale.OriginalBrightness)% after an interrupted run"
        }
        Recover-InterruptedSession $stale
        Clear-State
    }
    Remove-StaleTimerState $timerStatePath
    Remove-StaleTimerState $workTimerStatePath

    $config = Get-Content -Raw -LiteralPath $ConfigPath | ConvertFrom-Json
    if ($null -ne $config.gameBrightnessPercent) {
        $script:gameBrightnessPercent = [Math]::Max(0, [Math]::Min(100, [int]$config.gameBrightnessPercent))
    }
    if ($null -ne $config.gameTimerEnabled) {
        $script:gameTimerEnabled = [bool]$config.gameTimerEnabled
    }
    if ($null -ne $config.gameTimerMinutes) {
        $script:gameTimerMinutes = [Math]::Max(1, [Math]::Min(240, [int]$config.gameTimerMinutes))
    }
    if ($null -ne $config.breakTimerMinutes) {
        $script:breakTimerMinutes = [Math]::Max(1, [Math]::Min(60, [int]$config.breakTimerMinutes))
    }
    if ($null -ne $config.pauseGameWithEscape) {
        $script:pauseGameWithEscape = [bool]$config.pauseGameWithEscape
    }
    if ($null -ne $config.sessionSummaryEnabled) {
        $script:sessionSummaryEnabled = [bool]$config.sessionSummaryEnabled
    }
    if ([string]$config.activeMode -eq 'work') { $script:activeMode = 'work' }
    $script:timerCycle = Get-CompletedCycles $script:activeMode
    if ($script:activeMode -eq 'work') {
        $script:gameTimerEnabled = if ($null -ne $config.workTimerEnabled) { [bool]$config.workTimerEnabled } else { $true }
        $script:gameTimerMinutes = if ($null -ne $config.workTimerMinutes) { [Math]::Max(1, [Math]::Min(240, [int]$config.workTimerMinutes)) } else { 30 }
        $script:breakTimerMinutes = if ($null -ne $config.workBreakMinutes) { [Math]::Max(1, [Math]::Min(60, [int]$config.workBreakMinutes)) } else { 5 }
    }
    Update-IdleBrightness $true
    $folders = Get-GameFolders $config
    $gameApplications = Get-GameApplications $config
    $excluded = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($processName in @($config.excludedProcesses) + @('crash_reporter','crashreporter')) {
        if (-not [string]::IsNullOrWhiteSpace([string]$processName)) { [void]$excluded.Add([string]$processName) }
    }
    $cachedBrightnessText = if ($null -eq $script:lastIdleBrightness) { 'unavailable' } else { "$script:lastIdleBrightness%" }
    $workApps = @($config.workApps)
    $monitoringText = if ($script:activeMode -eq 'work') { $workApps -join '; ' } else { (@($gameApplications) + @($folders)) -join '; ' }
    Write-Log "Watcher started in AC-only $script:activeMode mode; cached idle brightness: $cachedBrightnessText; monitoring: $monitoringText"

    do {
        Process-TimerResetRequest

        if (Test-Path -LiteralPath $modeSwitchRequestPath) {
            $transitionMutex = [Threading.Mutex]::new($false, 'Local\GameManagementTransition_v1')
            $transitionHeld = $false
            $handoffFinished = $false
            try {
                try { $transitionHeld = $transitionMutex.WaitOne() }
                catch [Threading.AbandonedMutexException] { $transitionHeld = $true }
                $modeSwitchRequest = $null
                try { $modeSwitchRequest = Get-Content -Raw -LiteralPath $modeSwitchRequestPath -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop } catch {}
                if (-not (Test-ModeSwitchRequest $modeSwitchRequest)) {
                    Remove-Item -LiteralPath $modeSwitchRequestPath -Force -ErrorAction SilentlyContinue
                } else {
                    $requestId = [string]$modeSwitchRequest.RequestId
                    try {
                        if ($active -and $script:gameTimerEnabled) { Freeze-ActiveTimerState }
                        Stop-Management $true $false $true
                        $ack = [ordered]@{
                            RequestId = $requestId
                            Mode = $script:activeMode
                            Saved = $true
                        } | ConvertTo-Json -Compress
                        $ackTemporaryPath = Join-Path $root "mode-switch-ack.$requestId.tmp"
                        [IO.File]::WriteAllText($ackTemporaryPath, $ack, [Text.UTF8Encoding]::new($false))
                        [IO.File]::Move($ackTemporaryPath, $modeSwitchAckPath)
                        Remove-Item -LiteralPath $modeSwitchRequestPath -Force -ErrorAction SilentlyContinue
                        Write-Log "Mode-switch stop request $requestId completed for $script:activeMode mode"
                        $handoffFinished = $true
                    } catch {
                        Write-Log "Mode-switch stop request $requestId failed: $($_.Exception.Message)"
                    }
                }
            } finally {
                if ($transitionHeld) { $transitionMutex.ReleaseMutex() }
                $transitionMutex.Dispose()
            }
            if ($handoffFinished) { break }
        }

        $running = if ($script:activeMode -eq 'work') { Find-RunningWorkApps $workApps } else { Find-RunningGames $folders $gameApplications $excluded }
        $onAcPower = Test-AcPower
        if (-not $onAcPower) {
            if ($active) {
                Write-Log "AC power disconnected; disabling $script:activeMode management"
                Stop-Management $false
            }
            Update-IdleBrightness
        } elseif ($running.Count -gt 0) {
            if (-not $active) { Start-Management $running }
            elseif ($script:activeMode -eq 'game' -and (Get-ActiveScheme) -ne $managementPlanGuid) { Set-Scheme $managementPlanGuid | Out-Null }
            if ($active) {
                Update-GameTimer $running
            }
        } else {
            if ($active) { Stop-Management $true }
            Update-IdleBrightness
        }
        if ($Once) { break }
        [void]$wakeEvent.WaitOne([Math]::Max(2000, [int]$config.pollSeconds * 1000))
    } while ($true)
}
finally {
    if ($active) { Stop-Management $true $false }
    if ($wakeEvent) { $wakeEvent.Dispose() }
    if ($mutex) {
        try { $mutex.ReleaseMutex() } catch {}
        $mutex.Dispose()
    }
}

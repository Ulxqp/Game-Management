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
$gameTimerSeconds = 1800
$breakTimerSeconds = 300
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
$sessionElapsedSeconds = 0.0
$sessionRunningSince = $null
$sessionActiveSegments = @()
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

function Write-AtomicJson([string]$path, $value) {
    $contents = $value | ConvertTo-Json -Depth 6
    $temporaryPath = "$path.tmp"
    $backupPath = "$path.bak"
    try {
        [IO.File]::WriteAllText($temporaryPath, $contents, [Text.UTF8Encoding]::new($false))
        if (Test-Path -LiteralPath $path) {
            try { [IO.File]::Replace($temporaryPath, $path, $backupPath) }
            catch {
                [IO.File]::Copy($temporaryPath, $path, $true)
                Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
            }
            Remove-Item -LiteralPath $backupPath -Force -ErrorAction SilentlyContinue
        } else {
            try { [IO.File]::Move($temporaryPath, $path) }
            catch {
                [IO.File]::Copy($temporaryPath, $path, $true)
                Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
            }
        }
    } finally {
        Remove-Item -LiteralPath $temporaryPath,$backupPath -Force -ErrorAction SilentlyContinue
    }
}

function Get-SessionStatePath([string]$mode = $script:activeMode) {
    if ($mode -eq 'work') { return (Join-Path $root 'work-session-state.json') }
    return (Join-Path $root 'game-session-state.json')
}

function Get-SessionElapsedSeconds([datetime]$at = (Get-Date)) {
    $elapsed = [Math]::Max(0.0, [double]$script:sessionElapsedSeconds)
    if ($null -ne $script:sessionRunningSince) {
        $running = ($at - [datetime]$script:sessionRunningSince).TotalSeconds
        if ($running -gt 0) { $elapsed += $running }
    }
    return [Math]::Max(0.0, $elapsed)
}

function Close-SessionActiveSegment([datetime]$at = (Get-Date)) {
    if ($null -eq $script:sessionRunningSince) { return }
    $segmentStart = [datetime]$script:sessionRunningSince
    if ($at -gt $segmentStart) {
        $script:sessionElapsedSeconds += ($at - $segmentStart).TotalSeconds
        $script:sessionActiveSegments = @($script:sessionActiveSegments) + @([pscustomobject][ordered]@{
            StartedAt = $segmentStart.ToString('o')
            EndedAt = $at.ToString('o')
        })
    }
    $script:sessionRunningSince = $null
}

function Save-SessionState([bool]$paused = $false) {
    if ([string]::IsNullOrWhiteSpace([string]$script:sessionId) -or $null -eq $script:sessionStartedAt) { return }
    $checkpoint = Get-Date
    if ($paused) { Close-SessionActiveSegment $checkpoint }
    elseif ($null -eq $script:sessionRunningSince) { $script:sessionRunningSince = $checkpoint }
    $session = [ordered]@{
        Mode = $script:activeMode
        SessionId = $script:sessionId
        StartedAt = ([datetime]$script:sessionStartedAt).ToString('o')
        ElapsedSeconds = [Math]::Round([double]$script:sessionElapsedSeconds, 3)
        RunningSince = if ($null -eq $script:sessionRunningSince) { $null } else { ([datetime]$script:sessionRunningSince).ToString('o') }
        Paused = $paused
        SessionCycleStart = [Math]::Max(0, [int]$script:sessionCycleStart)
        Applications = @($script:sessionGameNames)
        ActiveSegments = @($script:sessionActiveSegments)
    }
    Write-AtomicJson (Get-SessionStatePath) $session
}

function Clear-SessionState([string]$mode = $script:activeMode) {
    Remove-Item -LiteralPath (Get-SessionStatePath $mode) -Force -ErrorAction SilentlyContinue
}

function Get-MatchingPausedSession($runtimeState) {
    if ($null -eq $runtimeState -or [string]::IsNullOrWhiteSpace([string]$runtimeState.SessionId)) { return $null }
    $mode = if ([string]$runtimeState.Mode -eq 'work') { 'work' } else { 'game' }
    $path = Get-SessionStatePath $mode
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    try {
        $saved = Get-Content -Raw -LiteralPath $path -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
        if ([bool]$saved.Paused -and [string]$saved.SessionId -eq [string]$runtimeState.SessionId) { return $saved }
    } catch {}
    return $null
}

function Resume-SavedSession($processes) {
    $path = Get-SessionStatePath
    if (-not (Test-Path -LiteralPath $path)) { return $false }
    try {
        $saved = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
        if (-not [string]::Equals([string]$saved.Mode, $script:activeMode, [StringComparison]::OrdinalIgnoreCase) -or
            [string]::IsNullOrWhiteSpace([string]$saved.SessionId) -or
            [string]::IsNullOrWhiteSpace([string]$saved.StartedAt)) {
            return $false
        }
        $script:sessionStartedAt = [datetime]$saved.StartedAt
        $script:sessionId = [string]$saved.SessionId
        $script:sessionElapsedSeconds = if ($null -ne $saved.ElapsedSeconds) { [Math]::Max(0.0, [double]$saved.ElapsedSeconds) } else { 0.0 }
        $script:sessionRunningSince = Get-Date
        $script:sessionActiveSegments = @($saved.ActiveSegments)
        $script:sessionCycleStart = if ($null -ne $saved.SessionCycleStart) { [Math]::Max(0, [int]$saved.SessionCycleStart) } else { Get-CompletedCycles $script:activeMode }
        $names = @($saved.Applications) + @($processes.ProcessName)
        $script:sessionGameNames = @($names | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Sort-Object -Unique)
        Write-Log "$script:activeMode session $script:sessionId resumed with $([Math]::Floor($script:sessionElapsedSeconds)) elapsed seconds"
        return $true
    } catch {
        Write-Log "Saved $script:activeMode session could not be resumed: $($_.Exception.Message)"
        return $false
    }
}

function Get-ConfiguredTimerSeconds($settings, [string]$secondsName, [string]$minutesName, [int]$defaultSeconds) {
    $secondsValue = $settings.$secondsName
    if ($null -ne $secondsValue) {
        return [Math]::Max(1, [Math]::Min(86400, [int][Math]::Round([double]$secondsValue)))
    }
    $minutesValue = $settings.$minutesName
    if ($null -ne $minutesValue) {
        return [Math]::Max(1, [Math]::Min(86400, [int][Math]::Round(([double]$minutesValue) * 60.0)))
    }
    return [Math]::Max(1, [Math]::Min(86400, $defaultSeconds))
}

function Format-InvariantMinutes([double]$minutes) {
    return $minutes.ToString('0.################', [Globalization.CultureInfo]::InvariantCulture)
}

function Get-NextWatcherWaitMilliseconds([datetime]$nextProcessScanAt, [datetime]$now = (Get-Date)) {
    $wakeAt = $nextProcessScanAt
    if ($script:active -and $script:gameTimerEnabled -and $null -ne $script:timerDeadline -and
        $script:timerDeadline -lt $wakeAt) {
        $wakeAt = $script:timerDeadline
    }
    return [Math]::Max(50, [int][Math]::Ceiling(($wakeAt - $now).TotalMilliseconds))
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
        } else {
            try { [IO.File]::Move($temporaryPath, $timerCountersPath) }
            catch {
                [IO.File]::Copy($temporaryPath, $timerCountersPath, $true)
                Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
            }
        }
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
    $activeSeconds = Get-SessionElapsedSeconds $endedAt
    $duration = [TimeSpan]::FromSeconds($activeSeconds)
    $historySegments = @($script:sessionActiveSegments)
    if ($null -ne $script:sessionRunningSince -and $endedAt -gt [datetime]$script:sessionRunningSince) {
        $historySegments += [pscustomobject][ordered]@{
            StartedAt = ([datetime]$script:sessionRunningSince).ToString('o')
            EndedAt = $endedAt.ToString('o')
        }
    }
    $historySegmentsJson = ConvertTo-Json -InputObject @($historySegments) -Compress -Depth 4
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
        ActiveSeconds = [Math]::Round($activeSeconds, 3)
        ActiveSegments = @($historySegments)
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
        "ActiveSeconds: $(([Math]::Round($activeSeconds, 3)).ToString([Globalization.CultureInfo]::InvariantCulture))"
        "ActiveSegments: $historySegmentsJson"
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
    Write-AtomicJson $lastSessionPath $summary
    Write-Log "$script:activeMode session saved for $gameName; duration $(Format-SessionDuration $duration); cycles $cycles; peak CPU temperature $peakCpuText; peak GPU temperature $peakGpuText"
    $app = Join-Path $root 'GameManagement.exe'
    if ($showDialog -and $script:sessionSummaryEnabled -and (Test-Path -LiteralPath $app)) { Start-Process -FilePath $app -ArgumentList '--session-summary' }
}

function Recover-InterruptedSession($stale, [datetime]$recoveryAt) {
    $persistedPausedSession = Get-MatchingPausedSession $stale
    if ($null -ne $persistedPausedSession) {
        Write-Log "Interrupted runtime state matched paused $($persistedPausedSession.Mode) session $($persistedPausedSession.SessionId); paused session preserved"
        return
    }
    try {
        if ($null -eq $stale -or [string]::IsNullOrWhiteSpace([string]$stale.ManagementStarted)) { return }
        $script:activeMode = if ([string]$stale.Mode -eq 'work') { 'work' } else { 'game' }
        $script:sessionStartedAt = [datetime]$stale.ManagementStarted
        $script:sessionId = [string]$stale.SessionId
        $script:sessionGameNames = @($stale.Applications)
        $script:sessionCycleStart = if ($null -ne $stale.SessionCycleStart) { [Math]::Max(0, [int]$stale.SessionCycleStart) } else { 0 }
        $script:sessionActiveSegments = @($stale.ActiveSegments)
        if ($null -ne $stale.SessionElapsedSeconds) {
            $script:sessionElapsedSeconds = [Math]::Max(0.0, [double]$stale.SessionElapsedSeconds)
            if ($null -ne $stale.SessionRunningSince -and -not [string]::IsNullOrWhiteSpace([string]$stale.SessionRunningSince)) {
                $runningSince = [datetime]$stale.SessionRunningSince
                if ($recoveryAt -gt $runningSince) {
                    $script:sessionElapsedSeconds += ($recoveryAt - $runningSince).TotalSeconds
                    $script:sessionActiveSegments += [pscustomobject][ordered]@{
                        StartedAt = $runningSince.ToString('o')
                        EndedAt = $recoveryAt.ToString('o')
                    }
                }
            }
        } else {
            $script:sessionElapsedSeconds = [Math]::Max(0.0, ($recoveryAt - $script:sessionStartedAt).TotalSeconds)
            if ($recoveryAt -gt $script:sessionStartedAt) {
                $script:sessionActiveSegments = @([pscustomobject][ordered]@{
                    StartedAt = $script:sessionStartedAt.ToString('o')
                    EndedAt = $recoveryAt.ToString('o')
                })
            }
        }
        $script:sessionRunningSince = $null
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
        Clear-SessionState $script:activeMode
        $script:sessionStartedAt = $null
        $script:sessionId = $null
        $script:sessionGameNames = @()
        $script:sessionCycleStart = 0
        $script:sessionElapsedSeconds = 0.0
        $script:sessionRunningSince = $null
        $script:sessionActiveSegments = @()
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
    $runtime = [ordered]@{
        SessionId = $script:sessionId
        OriginalPowerScheme = $script:originalGuid
        OriginalBrightness = $script:originalBrightness
        ManagementStarted = if ($null -eq $script:sessionStartedAt) { (Get-Date).ToString('o') } else { ([datetime]$script:sessionStartedAt).ToString('o') }
        Mode = $script:activeMode
        Applications = @($script:sessionGameNames)
        TimerEnabled = $script:gameTimerEnabled
        TimerCycle = $script:timerCycle
        SessionCycleStart = $script:sessionCycleStart
        SessionElapsedSeconds = [Math]::Round([double]$script:sessionElapsedSeconds, 3)
        SessionRunningSince = if ($null -eq $script:sessionRunningSince) { $null } else { ([datetime]$script:sessionRunningSince).ToString('o') }
        ActiveSegments = @($script:sessionActiveSegments)
    }
    Write-AtomicJson $statePath $runtime
}

function Clear-State {
    Remove-Item -LiteralPath $statePath -Force -ErrorAction SilentlyContinue
}

function Save-GameTimerState {
    $phaseSeconds = if ($script:timerPhase -eq 'Break') { $script:breakTimerSeconds } else { $script:gameTimerSeconds }
    $phaseMinutes = if ($script:timerPhase -eq 'Break') { $script:breakTimerMinutes } else { $script:gameTimerMinutes }
    $timer = [ordered]@{
        Phase = $script:timerPhase
        StartedAt = $script:timerDeadline.AddSeconds(-$phaseSeconds).ToString('o')
        Deadline = $script:timerDeadline.ToString('o')
        Minutes = $phaseMinutes
        Seconds = $phaseSeconds
        SessionSeconds = $script:gameTimerSeconds
        BreakSeconds = $script:breakTimerSeconds
        Alerted = $script:timerAlerted
        Cycle = $script:timerCycle
        Mode = $script:activeMode
        Paused = $false
        RemainingSeconds = $null
        Status = if ($script:timerPhase -eq 'Break') { 'Break' } else { 'Running' }
        Revision = [guid]::NewGuid().ToString('N')
    }
    $targetPath = if ($script:activeMode -eq 'work') { $workTimerStatePath } else { $timerStatePath }
    Write-AtomicJson $targetPath $timer
}

function Freeze-ActiveTimerState {
    if (-not $script:gameTimerEnabled -or $null -eq $script:timerDeadline) { return }
    $remainingSeconds = [Math]::Max(0, [Math]::Ceiling(($script:timerDeadline - (Get-Date)).TotalSeconds))
    $phaseSeconds = if ($script:timerPhase -eq 'Break') { $script:breakTimerSeconds } else { $script:gameTimerSeconds }
    $phaseMinutes = if ($script:timerPhase -eq 'Break') { $script:breakTimerMinutes } else { $script:gameTimerMinutes }
    $paused = [ordered]@{
        Phase = $script:timerPhase
        StartedAt = $script:timerDeadline.AddSeconds(-$phaseSeconds).ToString('o')
        Deadline = $script:timerDeadline.ToString('o')
        Minutes = $phaseMinutes
        Seconds = $phaseSeconds
        SessionSeconds = $script:gameTimerSeconds
        BreakSeconds = $script:breakTimerSeconds
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
    Write-AtomicJson $targetPath $paused
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
            $seconds = if ($mode -eq 'work') {
                Get-ConfiguredTimerSeconds $config 'workTimerSeconds' 'workTimerMinutes' 1800
            } else {
                Get-ConfiguredTimerSeconds $config 'gameTimerSeconds' 'gameTimerMinutes' 1800
            }
            $breakSeconds = if ($mode -eq 'work') {
                Get-ConfiguredTimerSeconds $config 'workBreakSeconds' 'workBreakMinutes' 300
            } else {
                Get-ConfiguredTimerSeconds $config 'breakTimerSeconds' 'breakTimerMinutes' 300
            }
            $minutes = [double]$seconds / 60.0
            $phase = if ($mode -eq 'work') { 'Work' } else { 'Game' }
            $state | Add-Member -NotePropertyName Phase -NotePropertyValue $phase -Force
            $state | Add-Member -NotePropertyName Minutes -NotePropertyValue $minutes -Force
            $state | Add-Member -NotePropertyName Seconds -NotePropertyValue $seconds -Force
            $state | Add-Member -NotePropertyName SessionSeconds -NotePropertyValue $seconds -Force
            $state | Add-Member -NotePropertyName BreakSeconds -NotePropertyValue $breakSeconds -Force
            $state | Add-Member -NotePropertyName StartedAt -NotePropertyValue (Get-Date).ToString('o') -Force
            $state | Add-Member -NotePropertyName Deadline -NotePropertyValue (Get-Date).AddSeconds($seconds).ToString('o') -Force
            $state | Add-Member -NotePropertyName Paused -NotePropertyValue $true -Force
            $state | Add-Member -NotePropertyName RemainingSeconds -NotePropertyValue $seconds -Force
            $state | Add-Member -NotePropertyName Status -NotePropertyValue 'Paused' -Force
        }
        $state | Add-Member -NotePropertyName Revision -NotePropertyValue ([guid]::NewGuid().ToString('N')) -Force
        Write-AtomicJson $path $state
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
                $script:timerDeadline = if ($script:gameTimerEnabled) { (Get-Date).AddSeconds($script:gameTimerSeconds) } else { $null }
                $script:timerAlerted = $false
            }
            if ($resetCycles) { $script:timerCycle = 0; $script:sessionCycleStart = 0 }
            if ($script:gameTimerEnabled -and $null -ne $script:timerDeadline) {
                Save-GameTimerState
            } elseif ($resetTimer) {
                $activeTimerPath = if ($script:activeMode -eq 'work') { $workTimerStatePath } else { $timerStatePath }
                Remove-Item -LiteralPath $activeTimerPath -Force -ErrorAction SilentlyContinue
            }
        } else { Update-InactiveTimerState $mode $resetTimer $resetCycles }
        $message = if ($action -eq 'reset-timer') { 'Timer reset.' } elseif ($action -eq 'reset-cycles') { 'Completed cycles reset.' } else { 'Timer and completed cycles reset.' }
        Write-Log "$mode $message"
    } catch {
        $succeeded = $false
        $message = $_.Exception.Message
    }
    Write-AtomicJson $timerResetAckPath ([ordered]@{ RequestId = [string]$request.RequestId; Succeeded = $succeeded; Message = $message })
    Remove-Item -LiteralPath $timerResetRequestPath -Force -ErrorAction SilentlyContinue
}

function Start-GameTimer {
    $activeTimerPath = if ($script:activeMode -eq 'work') { $workTimerStatePath } else { $timerStatePath }
    if (-not $script:gameTimerEnabled -or $script:gameTimerSeconds -lt 1) {
        Remove-Item -LiteralPath $activeTimerPath -Force -ErrorAction SilentlyContinue
        return
    }
    if (Test-Path -LiteralPath $activeTimerPath) {
        try {
            $pausedTimer = Get-Content -Raw -LiteralPath $activeTimerPath | ConvertFrom-Json
            $durationMatches = $true
            if ($null -ne $pausedTimer.SessionSeconds -or $null -ne $pausedTimer.BreakSeconds) {
                $durationMatches = [int]$pausedTimer.SessionSeconds -eq $script:gameTimerSeconds -and
                    [int]$pausedTimer.BreakSeconds -eq $script:breakTimerSeconds
            } elseif ($null -ne $pausedTimer.Seconds) {
                $expectedSeconds = if ([string]$pausedTimer.Phase -eq 'Break') { $script:breakTimerSeconds } else { $script:gameTimerSeconds }
                $durationMatches = [int]$pausedTimer.Seconds -eq $expectedSeconds
            } elseif ($null -ne $pausedTimer.Minutes) {
                $expectedSeconds = if ([string]$pausedTimer.Phase -eq 'Break') { $script:breakTimerSeconds } else { $script:gameTimerSeconds }
                $durationMatches = [int][Math]::Round(([double]$pausedTimer.Minutes) * 60.0) -eq $expectedSeconds
            }
            if ([bool]$pausedTimer.Paused -and $durationMatches -and
                [string]::Equals([string]$pausedTimer.Mode, $script:activeMode, [StringComparison]::OrdinalIgnoreCase)) {
                $remainingSeconds = [Math]::Max(0, [double]$pausedTimer.RemainingSeconds)
                $script:timerPhase = if ([string]$pausedTimer.Phase -eq 'Break') { 'Break' } elseif ($script:activeMode -eq 'work') { 'Work' } else { 'Game' }
                $script:timerCycle = [Math]::Max(0, [int]$pausedTimer.Cycle)
                $script:timerDeadline = (Get-Date).AddSeconds($remainingSeconds)
                $script:timerAlerted = $false
                Save-GameTimerState
                Write-Log "$($script:timerPhase) timer resumed with $([Math]::Ceiling($remainingSeconds)) seconds left in cycle $script:timerCycle"
                return
            }
            if ([bool]$pausedTimer.Paused -and -not $durationMatches) {
                Write-Log "$script:activeMode timer duration changed; paused countdown reset"
            }
        } catch {
            Write-Log "Paused timer could not be resumed: $($_.Exception.Message)"
        }
    }
    $script:timerPhase = if ($script:activeMode -eq 'work') { 'Work' } else { 'Game' }
    $script:timerCycle = Get-CompletedCycles $script:activeMode
    $script:timerDeadline = (Get-Date).AddSeconds($script:gameTimerSeconds)
    $script:timerAlerted = $false
    Save-GameTimerState
    Write-Log "$($script:timerPhase) timer started for $(Format-InvariantMinutes $script:gameTimerMinutes) minutes"
}

function Update-GameTimer($gameProcesses) {
    if (-not $script:gameTimerEnabled -or $null -eq $script:timerDeadline -or $script:timerAlerted) { return }
    if ((Get-Date) -lt $script:timerDeadline) { return }

    $alertApp = Join-Path $root 'GameManagement.exe'
    $gameMinutesText = Format-InvariantMinutes $script:gameTimerMinutes
    $breakMinutesText = Format-InvariantMinutes $script:breakTimerMinutes
    if ($script:timerPhase -ne 'Break') {
        Write-Log "$($script:timerPhase) timer finished after $gameMinutesText minutes; break timer started for $breakMinutesText minutes"
        $script:timerPhase = 'Break'
        $script:timerDeadline = (Get-Date).AddSeconds($script:breakTimerSeconds)
        $script:timerAlerted = $false
        Save-GameTimerState
        if ($script:activeMode -eq 'game') { Send-GameEscape $gameProcesses 'break start' | Out-Null }
        if (-not $NoInterface -and (Test-Path -LiteralPath $alertApp)) {
            $alertType = if ($script:activeMode -eq 'work') { 'work-finished' } else { 'game-finished' }
            Start-Process -FilePath $alertApp -ArgumentList @('--timer-alert', $alertType, $gameMinutesText, $breakMinutesText)
        } elseif (-not $NoInterface) {
            try { [Console]::Beep(880, 700) } catch {}
        }
    } else {
        $script:timerPhase = if ($script:activeMode -eq 'work') { 'Work' } else { 'Game' }
        Write-Log "Break timer finished after $breakMinutesText minutes; $($script:timerPhase) timer restarted for $gameMinutesText minutes"
        $script:timerCycle++
        Set-CompletedCycles $script:activeMode $script:timerCycle
        $script:timerDeadline = (Get-Date).AddSeconds($script:gameTimerSeconds)
        $script:timerAlerted = $false
        Save-GameTimerState
        if ($script:activeMode -eq 'game') { Send-GameEscape $gameProcesses 'break end' | Out-Null }
        if (-not $NoInterface -and (Test-Path -LiteralPath $alertApp)) {
            $alertType = if ($script:activeMode -eq 'work') { 'work-break-finished' } else { 'break-finished' }
            Start-Process -FilePath $alertApp -ArgumentList @('--timer-alert', $alertType, $breakMinutesText, $gameMinutesText)
        } elseif (-not $NoInterface) {
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

function Finalize-PausedSessionIfInactive {
    $path = Get-SessionStatePath
    if (-not (Test-Path -LiteralPath $path)) { return $false }
    try {
        $saved = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
        if (-not [bool]$saved.Paused -or
            -not [string]::Equals([string]$saved.Mode, $script:activeMode, [StringComparison]::OrdinalIgnoreCase) -or
            [string]::IsNullOrWhiteSpace([string]$saved.SessionId) -or
            [string]::IsNullOrWhiteSpace([string]$saved.StartedAt)) {
            return $false
        }
        $script:sessionStartedAt = [datetime]$saved.StartedAt
        $script:sessionId = [string]$saved.SessionId
        $script:sessionGameNames = @($saved.Applications)
        $script:sessionCycleStart = if ($null -ne $saved.SessionCycleStart) { [Math]::Max(0, [int]$saved.SessionCycleStart) } else { 0 }
        $script:sessionElapsedSeconds = if ($null -ne $saved.ElapsedSeconds) { [Math]::Max(0.0, [double]$saved.ElapsedSeconds) } else { 0.0 }
        $script:sessionRunningSince = $null
        $script:sessionActiveSegments = @($saved.ActiveSegments)
        Write-GameSessionSummary $false
        Write-Log "Paused $script:activeMode session $script:sessionId finalized because no selected application was running"
        Clear-SessionState $script:activeMode
        Clear-GameTimer
        return $true
    } catch {
        Write-Log "Paused $script:activeMode session could not be finalized: $($_.Exception.Message)"
        return $false
    } finally {
        $script:sessionStartedAt = $null
        $script:sessionId = $null
        $script:sessionGameNames = @()
        $script:sessionCycleStart = 0
        $script:sessionElapsedSeconds = 0.0
        $script:sessionRunningSince = $null
        $script:sessionActiveSegments = @()
    }
}

function Start-Management($gameProcesses) {
    $resumedSession = Resume-SavedSession $gameProcesses
    if (-not $resumedSession) {
        $script:sessionStartedAt = Get-Date
        $script:sessionId = [guid]::NewGuid().ToString('N')
        $script:sessionGameNames = @($gameProcesses.ProcessName | Sort-Object -Unique)
        $script:sessionCycleStart = Get-CompletedCycles $script:activeMode
        $script:sessionElapsedSeconds = 0.0
        $script:sessionRunningSince = Get-Date
        $script:sessionActiveSegments = @()
        Remove-Item -LiteralPath $performanceStatePath -Force -ErrorAction SilentlyContinue
    }
    if (-not $NoInterface) { Start-BackgroundInterface }
    $script:originalGuid = Get-ActiveScheme
    if (-not $script:originalGuid) { $script:originalGuid = $balancedGuid }
    Update-IdleBrightness $true
    $script:originalBrightness = $script:lastIdleBrightness
    Save-SessionState $false
    Save-State
    if ($script:activeMode -eq 'work') {
        $script:active = $true
        Start-GameTimer
        Write-Log "Work Management ON; applications: $($gameProcesses.ProcessName -join ', ')"
        return
    }
    if (-not (Set-Scheme $managementPlanGuid)) {
        Write-Log 'Game Management could not activate because the dedicated power plan was unavailable'
        Save-SessionState $true
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

function Stop-Management(
    [bool]$showSessionSummary = $false,
    [bool]$showSummaryDialog = $true,
    [bool]$preserveTimer = $false,
    [bool]$preserveSession = $false
) {
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
    if ($script:active -and -not [string]::IsNullOrWhiteSpace([string]$script:sessionId)) { Save-SessionState $true }
    if ($showSessionSummary -and $script:active) { Write-GameSessionSummary $showSummaryDialog }
    if (-not $preserveSession) { Clear-SessionState $script:activeMode }
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
    $script:sessionElapsedSeconds = 0.0
    $script:sessionRunningSince = $null
    $script:sessionActiveSegments = @()
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
        $staleCheckpoint = (Get-Item -LiteralPath $statePath).LastWriteTime
        $stale = Get-Content -Raw -LiteralPath $statePath | ConvertFrom-Json
        if ($stale.OriginalPowerScheme -and (Set-Scheme $stale.OriginalPowerScheme)) {
            Write-Log "Recovered previous plan $($stale.OriginalPowerScheme) after an interrupted run"
        }
        if ($null -ne $stale.OriginalBrightness -and (Set-DisplayBrightness ([int]$stale.OriginalBrightness))) {
            Write-Log "Recovered previous brightness $($stale.OriginalBrightness)% after an interrupted run"
        }
        Recover-InterruptedSession $stale $staleCheckpoint
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
    $script:gameTimerSeconds = Get-ConfiguredTimerSeconds $config 'gameTimerSeconds' 'gameTimerMinutes' 1800
    $script:breakTimerSeconds = Get-ConfiguredTimerSeconds $config 'breakTimerSeconds' 'breakTimerMinutes' 300
    $script:gameTimerMinutes = [double]$script:gameTimerSeconds / 60.0
    $script:breakTimerMinutes = [double]$script:breakTimerSeconds / 60.0
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
        $script:gameTimerSeconds = Get-ConfiguredTimerSeconds $config 'workTimerSeconds' 'workTimerMinutes' 1800
        $script:breakTimerSeconds = Get-ConfiguredTimerSeconds $config 'workBreakSeconds' 'workBreakMinutes' 300
        $script:gameTimerMinutes = [double]$script:gameTimerSeconds / 60.0
        $script:breakTimerMinutes = [double]$script:breakTimerSeconds / 60.0
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

    $running = @()
    $onAcPower = $false
    $nextProcessScanAt = [datetime]::MinValue
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
                        Stop-Management $false $false $true $true
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

        $now = Get-Date
        if ($now -ge $nextProcessScanAt) {
            $running = if ($script:activeMode -eq 'work') { Find-RunningWorkApps $workApps } else { Find-RunningGames $folders $gameApplications $excluded }
            $onAcPower = Test-AcPower
            $pollMilliseconds = [Math]::Max(250, [int]([double]$config.pollSeconds * 1000.0))
            $nextProcessScanAt = $now.AddMilliseconds($pollMilliseconds)
            if (-not $onAcPower) {
                if ($active) {
                    Write-Log "AC power disconnected; disabling $script:activeMode management"
                    Stop-Management $false $false $false $true
                }
                Update-IdleBrightness
            } elseif ($running.Count -gt 0) {
                if (-not $active) { Start-Management $running }
                elseif ($script:activeMode -eq 'game' -and (Get-ActiveScheme) -ne $managementPlanGuid) { Set-Scheme $managementPlanGuid | Out-Null }
            } else {
                if ($active) { Stop-Management $true }
                else { Finalize-PausedSessionIfInactive | Out-Null }
                Update-IdleBrightness
            }
        }
        if ($active) {
            Update-GameTimer $running
            Save-SessionState $false
            Save-State
        }
        if ($Once) { break }
        $waitMilliseconds = Get-NextWatcherWaitMilliseconds $nextProcessScanAt
        [void]$wakeEvent.WaitOne($waitMilliseconds)
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

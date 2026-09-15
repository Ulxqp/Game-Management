param([switch]$Once, [string]$ConfigPath, [string]$MutexName = 'Local\GameManagementWatcher_v1')

$ErrorActionPreference = 'SilentlyContinue'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $ConfigPath) { $ConfigPath = Join-Path $root 'settings.json' }
$statePath = Join-Path $root 'runtime-state.json'
$timerStatePath = Join-Path $root 'timer-state.json'
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
$timerPhase = 'Game'
$timerDeadline = $null
$timerAlerted = $false
$timerCycle = 1
$sessionStartedAt = $null
$sessionGameNames = @()
$activeMode = 'game'
$mutex = $null

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

function Write-GameSessionSummary {
    $endedAt = Get-Date
    $startedAt = $script:sessionStartedAt
    if ($null -eq $startedAt -and (Test-Path -LiteralPath $statePath)) {
        try { $startedAt = [datetime](Get-Content -Raw -LiteralPath $statePath | ConvertFrom-Json).ManagementStarted } catch {}
    }
    if ($null -eq $startedAt) { $startedAt = $endedAt }
    $duration = $endedAt - $startedAt
    if ($duration.TotalSeconds -le 120) {
        Remove-Item -LiteralPath $lastSessionPath -Force -ErrorAction SilentlyContinue
        Write-Log "Game session not saved because it lasted 2 minutes or less ($(Format-SessionDuration $duration))"
        return
    }
    $gameName = if (@($script:sessionGameNames).Count) { @($script:sessionGameNames) -join ', ' } else { 'Detected game' }
    $cycles = if ($script:gameTimerEnabled) { [Math]::Max(1, [int]$script:timerCycle) } else { 0 }
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
    $summary | ConvertTo-Json | Set-Content -LiteralPath $lastSessionPath -Encoding UTF8
    @(
        '------------------------------------------------------------'
        "Date: $($endedAt.ToString('yyyy-MM-dd'))"
        "Mode: $script:activeMode"
        "Applications: $gameName"
        "Started: $($startedAt.ToString('yyyy-MM-dd HH:mm:ss'))"
        "Finished: $($endedAt.ToString('yyyy-MM-dd HH:mm:ss'))"
        "Total game time: $(Format-SessionDuration $duration) ($([Math]::Round($duration.TotalHours, 3)) hours)"
        "Timer cycles: $cycles"
        "Peak CPU temperature: $peakCpuText"
        "Peak GPU temperature: $peakGpuText"
    ) | Add-Content -LiteralPath $sessionHistoryPath -Encoding UTF8
    Write-Log "$script:activeMode session saved for $gameName; duration $(Format-SessionDuration $duration); cycles $cycles; peak CPU temperature $peakCpuText; peak GPU temperature $peakGpuText"
    $app = Join-Path $root 'GameManagement.exe'
    if (Test-Path -LiteralPath $app) { Start-Process -FilePath $app -ArgumentList '--session-summary' }
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
        OriginalPowerScheme = $script:originalGuid
        OriginalBrightness = $script:originalBrightness
        ManagementStarted = (Get-Date).ToString('o')
        Mode = $script:activeMode
        Applications = @($script:sessionGameNames)
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
    } | ConvertTo-Json | Set-Content -LiteralPath $timerStatePath -Encoding UTF8
}

function Start-GameTimer {
    if (-not $script:gameTimerEnabled -or $script:gameTimerMinutes -lt 1) { return }
    $script:timerPhase = 'Game'
    $script:timerCycle = 1
    $script:timerDeadline = (Get-Date).AddMinutes($script:gameTimerMinutes)
    $script:timerAlerted = $false
    Save-GameTimerState
    Write-Log "Game timer started for $script:gameTimerMinutes minutes"
}

function Update-GameTimer($gameProcesses) {
    if (-not $script:gameTimerEnabled -or $null -eq $script:timerDeadline -or $script:timerAlerted) { return }
    if ((Get-Date) -lt $script:timerDeadline) { return }

    $alertApp = Join-Path $root 'GameManagement.exe'
    if ($script:timerPhase -eq 'Game') {
        Write-Log "Game timer finished after $script:gameTimerMinutes minutes; break timer started for $script:breakTimerMinutes minutes"
        $script:timerPhase = 'Break'
        $script:timerDeadline = (Get-Date).AddMinutes($script:breakTimerMinutes)
        $script:timerAlerted = $false
        Save-GameTimerState
        if ($script:activeMode -eq 'game') { Send-GameEscape $gameProcesses 'break start' | Out-Null }
        if (Test-Path -LiteralPath $alertApp) {
            Start-Process -FilePath $alertApp -ArgumentList @('--timer-alert', 'game-finished', [string]$script:gameTimerMinutes, [string]$script:breakTimerMinutes)
        } else {
            try { [Console]::Beep(880, 700) } catch {}
        }
    } else {
        Write-Log "Break timer finished after $script:breakTimerMinutes minutes; game timer restarted for $script:gameTimerMinutes minutes"
        $script:timerPhase = 'Game'
        $script:timerCycle++
        $script:timerDeadline = (Get-Date).AddMinutes($script:gameTimerMinutes)
        $script:timerAlerted = $false
        Save-GameTimerState
        if ($script:activeMode -eq 'game') { Send-GameEscape $gameProcesses 'break end' | Out-Null }
        if (Test-Path -LiteralPath $alertApp) {
            Start-Process -FilePath $alertApp -ArgumentList @('--timer-alert', 'break-finished', [string]$script:breakTimerMinutes, [string]$script:gameTimerMinutes)
        } else {
            try { [Console]::Beep(1047, 700) } catch {}
        }
    }
}

function Clear-GameTimer {
    Remove-Item -LiteralPath $timerStatePath -Force -ErrorAction SilentlyContinue
    $script:timerDeadline = $null
    $script:timerAlerted = $false
    $script:timerPhase = 'Game'
    $script:timerCycle = 1
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

function Find-RunningGames($folders, $excluded) {
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
                if (-not $excluded.Contains($processName)) {
                    $path = [IO.Path]::GetFullPath([string]$process.Path)
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
    $script:sessionGameNames = @($gameProcesses.ProcessName | Sort-Object -Unique)
    Remove-Item -LiteralPath $performanceStatePath -Force -ErrorAction SilentlyContinue
    Start-BackgroundInterface
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

function Stop-Management([bool]$showSessionSummary = $false) {
    $savedState = $null
    if (Test-Path -LiteralPath $statePath) {
        $savedState = Get-Content -Raw -LiteralPath $statePath | ConvertFrom-Json
    }
    $restoreGuid = $script:originalGuid
    if (-not $restoreGuid -and $savedState) {
        $restoreGuid = $savedState.OriginalPowerScheme
    }
    $restoreBrightness = $script:originalBrightness
    if ($null -eq $restoreBrightness -and $savedState -and $null -ne $savedState.OriginalBrightness) {
        $restoreBrightness = [int]$savedState.OriginalBrightness
    }
    if ($script:activeMode -eq 'work') {
        Write-Log 'Work Management OFF; selected applications closed'
    } else {
        if (-not $restoreGuid) { $restoreGuid = $balancedGuid }
        if (Set-Scheme $restoreGuid) { Write-Log "Game Management OFF; restored plan $restoreGuid" }
        else { Write-Log "Game Management OFF but restoring plan $restoreGuid failed" }
        if ($null -ne $restoreBrightness) {
            if (Set-DisplayBrightness $restoreBrightness) { Write-Log "Brightness restored to $restoreBrightness%" }
            else { Write-Log "Brightness restoration to $restoreBrightness% failed" }
        }
    }
    if ($showSessionSummary) { Write-GameSessionSummary }
    Clear-State
    Clear-GameTimer
    $script:active = $false
    $script:originalGuid = $null
    $script:originalBrightness = $null
    $script:sessionStartedAt = $null
    $script:sessionGameNames = @()
    if ($null -ne $restoreBrightness) { $script:lastIdleBrightness = $restoreBrightness }
}

try {
    $createdNew = $false
    $mutex = [Threading.Mutex]::new($true, $MutexName, [ref]$createdNew)
    if (-not $createdNew) { exit 0 }

    if (Test-Path -LiteralPath $statePath) {
        $stale = Get-Content -Raw -LiteralPath $statePath | ConvertFrom-Json
        if ($stale.OriginalPowerScheme -and (Set-Scheme $stale.OriginalPowerScheme)) {
            Write-Log "Recovered previous plan $($stale.OriginalPowerScheme) after an interrupted run"
        }
        if ($null -ne $stale.OriginalBrightness -and (Set-DisplayBrightness ([int]$stale.OriginalBrightness))) {
            Write-Log "Recovered previous brightness $($stale.OriginalBrightness)% after an interrupted run"
        }
        Clear-State
    }
    Clear-GameTimer

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
    if ([string]$config.activeMode -eq 'work') { $script:activeMode = 'work' }
    Update-IdleBrightness $true
    $folders = Get-GameFolders $config
    $excluded = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($processName in @($config.excludedProcesses) + @('crash_reporter','crashreporter')) {
        if (-not [string]::IsNullOrWhiteSpace([string]$processName)) { [void]$excluded.Add([string]$processName) }
    }
    $cachedBrightnessText = if ($null -eq $script:lastIdleBrightness) { 'unavailable' } else { "$script:lastIdleBrightness%" }
    $workApps = @($config.workApps)
    $monitoringText = if ($script:activeMode -eq 'work') { $workApps -join '; ' } else { $folders -join '; ' }
    Write-Log "Watcher started in AC-only $script:activeMode mode; cached idle brightness: $cachedBrightnessText; monitoring: $monitoringText"

    do {
        $running = if ($script:activeMode -eq 'work') { Find-RunningWorkApps $workApps } else { Find-RunningGames $folders $excluded }
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
        Start-Sleep -Seconds ([Math]::Max(2, [int]$config.pollSeconds))
    } while ($true)
}
finally {
    if ($active) { Stop-Management $false }
    if ($mutex) {
        try { $mutex.ReleaseMutex() } catch {}
        $mutex.Dispose()
    }
}

param([switch]$Once, [string]$ConfigPath, [string]$MutexName = 'Local\GameManagementWatcher_v1')

$ErrorActionPreference = 'SilentlyContinue'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $ConfigPath) { $ConfigPath = Join-Path $root 'settings.json' }
$statePath = Join-Path $root 'runtime-state.json'
$timerStatePath = Join-Path $root 'timer-state.json'
$workTimerStatePath = Join-Path $root 'work-timer-state.json'
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
$timerPhase = 'Game'
$timerDeadline = $null
$timerAlerted = $false
$timerCycle = 1
$sessionStartedAt = $null
$sessionId = $null
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
            [IO.File]::Replace($temporaryHistoryPath, $sessionHistoryPath, $backupHistoryPath)
            Remove-Item -LiteralPath $backupHistoryPath -Force -ErrorAction SilentlyContinue
        } else {
            [IO.File]::Move($temporaryHistoryPath, $sessionHistoryPath)
        }
    }
    $summary | ConvertTo-Json | Set-Content -LiteralPath $lastSessionPath -Encoding UTF8 -ErrorAction Stop
    Write-Log "$script:activeMode session saved for $gameName; duration $(Format-SessionDuration $duration); cycles $cycles; peak CPU temperature $peakCpuText; peak GPU temperature $peakGpuText"
    $app = Join-Path $root 'GameManagement.exe'
    if ($showDialog -and (Test-Path -LiteralPath $app)) { Start-Process -FilePath $app -ArgumentList '--session-summary' }
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
    } | ConvertTo-Json | Set-Content -LiteralPath $(if ($script:activeMode -eq 'work') { $workTimerStatePath } else { $timerStatePath }) -Encoding UTF8
}

function Start-GameTimer {
    if (-not $script:gameTimerEnabled -or $script:gameTimerMinutes -lt 1) { return }
    $script:timerPhase = if ($script:activeMode -eq 'work') { 'Work' } else { 'Game' }
    $script:timerCycle = 1
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

function Stop-Management([bool]$showSessionSummary = $false, [bool]$showSummaryDialog = $true) {
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
    Clear-GameTimer
    $script:active = $false
    $script:originalGuid = $null
    $script:originalBrightness = $null
    $script:sessionStartedAt = $null
    $script:sessionId = $null
    $script:sessionGameNames = @()
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
    Remove-Item -LiteralPath $workTimerStatePath -Force -ErrorAction SilentlyContinue

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
                        Stop-Management $true $false
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

param([switch]$TimerStateOnly)

$ErrorActionPreference = 'SilentlyContinue'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$statePath = Join-Path $root 'runtime-state.json'
$gameSessionStatePath = Join-Path $root 'game-session-state.json'
$workSessionStatePath = Join-Path $root 'work-session-state.json'
$timerStatePath = Join-Path $root 'timer-state.json'
$workTimerStatePath = Join-Path $root 'work-timer-state.json'
$engineEnabledPath = Join-Path $root 'engine-enabled.flag'
$logPath = Join-Path $root 'GameManagement.log'
$balancedGuid = '381b4222-f694-41f0-9685-ff5bb260df2e'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

function Write-AtomicJson([string]$path, $value, [string]$suffix = 'pause') {
    $temporaryPath = "$path.$suffix.tmp"
    $backupPath = "$path.$suffix.bak"
    try {
        [IO.File]::WriteAllText($temporaryPath, ($value | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
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

function Write-BestEffortLog([string]$message) {
    try {
        Add-Content -LiteralPath $logPath -Value "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') $message" -Encoding UTF8 -ErrorAction Stop
    } catch {}
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
    $succeeded = $true
    foreach ($saved in @($savedPriorities)) {
        if ([string]$saved.PriorityClass -notin $allowed) { continue }
        $process = Get-Process -Id ([int]$saved.ProcessId) -ErrorAction SilentlyContinue
        if ($null -eq $process -or $process.ProcessName -ne [string]$saved.ProcessName) { continue }
        try {
            $process.PriorityClass = [string]$saved.PriorityClass
        } catch { $succeeded = $false }
    }
    return $succeeded
}

function Test-OwnedWatcherCommand([string]$commandLine, [string]$watcherPath) {
    if ([string]::IsNullOrWhiteSpace($commandLine) -or [string]::IsNullOrWhiteSpace($watcherPath)) { return $false }
    $escapedPath = [regex]::Escape([IO.Path]::GetFullPath($watcherPath))
    $filePattern = '(?i)(?:^|\s)-File\s+(?:"{0}"|''{0}''|{0})(?=\s|$)' -f $escapedPath
    return [regex]::IsMatch($commandLine, $filePattern)
}

function Stop-OwnedWatchers($watchers) {
    $succeeded = $true
    foreach ($watcher in @($watchers)) {
        $processId = [int]$watcher.ProcessId
        try {
            Stop-Process -Id $processId -Force -ErrorAction Stop
            $stillRunning = $true
            for ($attempt = 0; $attempt -lt 20; $attempt++) {
                if ($null -eq (Get-Process -Id $processId -ErrorAction SilentlyContinue)) {
                    $stillRunning = $false
                    break
                }
                Start-Sleep -Milliseconds 50
            }
            if ($stillRunning) { $succeeded = $false }
        } catch { $succeeded = $false }
    }
    return $succeeded
}

function Read-PauseRuntimeState([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) {
        return [pscustomobject]@{ Exists = $false; Readable = $true; State = $null }
    }
    try {
        $runtimeState = Get-Content -Raw -LiteralPath $path -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
        return [pscustomobject]@{ Exists = $true; Readable = $true; State = $runtimeState }
    } catch {
        Write-BestEffortLog "Runtime state could not be read: $($_.Exception.Message)"
        return [pscustomobject]@{ Exists = $true; Readable = $false; State = $null }
    }
}

function Freeze-RuntimeSession($state) {
    if ($null -eq $state) { return $true }
    if ([string]::IsNullOrWhiteSpace([string]$state.SessionId) -or
        [string]::IsNullOrWhiteSpace([string]$state.ManagementStarted)) {
        return $true
    }
    try {
        $mode = if ([string]$state.Mode -eq 'work') { 'work' } else { 'game' }
        $sessionPath = if ($mode -eq 'work') { $workSessionStatePath } else { $gameSessionStatePath }
        if (Test-Path -LiteralPath $sessionPath) {
            $persisted = Get-Content -Raw -LiteralPath $sessionPath -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
            if ([string]$persisted.SessionId -ne [string]$state.SessionId) {
                throw 'Runtime session conflicts with the persisted session for this mode.'
            }
            if ([bool]$persisted.Paused) {
                Write-BestEffortLog "$mode session $($state.SessionId) was already frozen"
                return $true
            }
        }
        $now = Get-Date
        $elapsedSeconds = if ($null -ne $state.SessionElapsedSeconds) {
            [Math]::Max(0.0, [double]$state.SessionElapsedSeconds)
        } else { 0.0 }
        $activeSegments = @($state.ActiveSegments)
        $runningSinceText = if ($null -ne $state.SessionRunningSince -and
            -not [string]::IsNullOrWhiteSpace([string]$state.SessionRunningSince)) {
            [string]$state.SessionRunningSince
        } else { [string]$state.ManagementStarted }
        if (-not [string]::IsNullOrWhiteSpace($runningSinceText)) {
            $runningSince = [datetime]$runningSinceText
            if ($now -gt $runningSince) {
                $elapsedSeconds += ($now - $runningSince).TotalSeconds
                $activeSegments += [pscustomobject][ordered]@{
                    StartedAt = $runningSince.ToString('o')
                    EndedAt = $now.ToString('o')
                }
            }
        }
        $session = [ordered]@{
            Mode = $mode
            SessionId = [string]$state.SessionId
            StartedAt = ([datetime]$state.ManagementStarted).ToString('o')
            ElapsedSeconds = [Math]::Round($elapsedSeconds, 3)
            RunningSince = $null
            Paused = $true
            SessionCycleStart = if ($null -ne $state.SessionCycleStart) { [Math]::Max(0, [int]$state.SessionCycleStart) } else { 0 }
            Applications = @($state.Applications)
            ActiveSegments = @($activeSegments)
        }
        Write-AtomicJson $sessionPath $session 'session-pause'
    } catch {
        Write-BestEffortLog "Session could not be frozen: $($_.Exception.Message)"
        return $false
    }
    Write-BestEffortLog "$mode session $($state.SessionId) paused at $([Math]::Floor($elapsedSeconds)) elapsed seconds"
    return $true
}

function Freeze-SessionCheckpoint([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return $true }
    try {
        $session = Get-Content -Raw -LiteralPath $path -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
        if ([bool]$session.Paused) { return $true }
        if ([string]::IsNullOrWhiteSpace([string]$session.SessionId) -or
            [string]::IsNullOrWhiteSpace([string]$session.StartedAt) -or
            [string]::IsNullOrWhiteSpace([string]$session.RunningSince)) {
            throw 'Running session checkpoint is incomplete.'
        }
        $now = Get-Date
        $runningSince = [datetime]$session.RunningSince
        $elapsedSeconds = if ($null -ne $session.ElapsedSeconds) { [Math]::Max(0.0, [double]$session.ElapsedSeconds) } else { 0.0 }
        $activeSegments = @($session.ActiveSegments)
        if ($now -gt $runningSince) {
            $elapsedSeconds += ($now - $runningSince).TotalSeconds
            $activeSegments += [pscustomobject][ordered]@{
                StartedAt = $runningSince.ToString('o')
                EndedAt = $now.ToString('o')
            }
        }
        $session | Add-Member -NotePropertyName ElapsedSeconds -NotePropertyValue ([Math]::Round($elapsedSeconds, 3)) -Force
        $session | Add-Member -NotePropertyName RunningSince -NotePropertyValue $null -Force
        $session | Add-Member -NotePropertyName Paused -NotePropertyValue $true -Force
        $session | Add-Member -NotePropertyName ActiveSegments -NotePropertyValue @($activeSegments) -Force
        Write-AtomicJson $path $session 'checkpoint-pause'
    } catch {
        Write-BestEffortLog "Session checkpoint could not be frozen: $($_.Exception.Message)"
        return $false
    }
    Write-BestEffortLog "Session checkpoint $($session.SessionId) frozen without runtime state"
    return $true
}

function Freeze-TimerState([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return $true }
    try {
        $timer = Get-Content -Raw -LiteralPath $path -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
        if ([bool]$timer.Paused -and $null -ne $timer.RemainingSeconds) { return $true }
        $deadline = [datetime]::Parse([string]$timer.Deadline, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind)
        $remainingSeconds = [Math]::Max(0, [Math]::Ceiling(($deadline.ToLocalTime() - (Get-Date)).TotalSeconds))
        $timer | Add-Member -NotePropertyName Paused -NotePropertyValue $true -Force
        $timer | Add-Member -NotePropertyName RemainingSeconds -NotePropertyValue $remainingSeconds -Force
        $timer | Add-Member -NotePropertyName PausedAt -NotePropertyValue (Get-Date).ToString('o') -Force
        Write-AtomicJson $path $timer
    } catch {
        Remove-Item -LiteralPath "$path.pause.tmp" -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath "$path.pause.bak" -Force -ErrorAction SilentlyContinue
        Write-BestEffortLog "Timer could not be frozen: $($_.Exception.Message)"
        return $false
    }
    Write-BestEffortLog "Timer paused with $remainingSeconds seconds left in $($timer.Phase), cycle $($timer.Cycle)"
    return $true
}

if ($TimerStateOnly) {
    $gameTimerFrozen = Freeze-TimerState $timerStatePath
    $workTimerFrozen = Freeze-TimerState $workTimerStatePath
    if ($gameTimerFrozen -and $workTimerFrozen) { exit 0 }
    exit 1
}

$ownedWatcherPath = Join-Path $root 'GameManagement.ps1'
$watchersEnumerated = $true
try {
    $watchers = @(Get-CimInstance Win32_Process -ErrorAction Stop |
        Where-Object {
            $_.Name -in @('powershell.exe','pwsh.exe') -and
            (Test-OwnedWatcherCommand ([string]$_.CommandLine) $ownedWatcherPath) -and
            $_.ProcessId -ne $PID
        })
} catch {
    $watchersEnumerated = $false
    $watchers = @()
    Write-BestEffortLog "Watcher enumeration failed: $($_.Exception.Message)"
}
if (-not $watchersEnumerated) { exit 1 }
$watchersStopped = Stop-OwnedWatchers @($watchers)
if (-not $watchersStopped) {
    Write-BestEffortLog 'Owned watcher termination could not be verified; pause aborted before state mutation'
    exit 1
}

$restoreGuid = $balancedGuid
$runtimeResult = Read-PauseRuntimeState $statePath
$runtimeExists = [bool]$runtimeResult.Exists
$runtimeReadable = [bool]$runtimeResult.Readable
$state = $runtimeResult.State
if ($null -ne $state -and $state.OriginalPowerScheme -match '^[0-9a-fA-F-]{36}$') {
    $restoreGuid = [string]$state.OriginalPowerScheme
}
$sessionFrozen = $false
if ($runtimeReadable -and $null -ne $state) {
    $sessionFrozen = Freeze-RuntimeSession $state
} elseif ($runtimeReadable) {
    $gameSessionFrozen = Freeze-SessionCheckpoint $gameSessionStatePath
    $workSessionFrozen = Freeze-SessionCheckpoint $workSessionStatePath
    $sessionFrozen = $gameSessionFrozen -and $workSessionFrozen
}
$gameTimerFrozen = Freeze-TimerState $timerStatePath
$workTimerFrozen = Freeze-TimerState $workTimerStatePath

$restored = $true
if ($state) {
    try {
        powercfg.exe /setactive $restoreGuid | Out-Null
        if ($LASTEXITCODE -ne 0) { $restored = $false }
    } catch { $restored = $false }
    if ($null -ne $state.OriginalBrightness) {
        if (-not (Set-DisplayBrightness ([int]$state.OriginalBrightness))) { $restored = $false }
    }
    if (-not (Restore-SavedPriorities @($state.OriginalPriorities))) { $restored = $false }
}
$requiredStagesSucceeded = $runtimeReadable -and $sessionFrozen -and $gameTimerFrozen -and $workTimerFrozen -and $restored
$runtimeCleared = -not $runtimeExists
if ($requiredStagesSucceeded -and $runtimeExists) {
    try {
        Remove-Item -LiteralPath $statePath -Force -ErrorAction Stop
        $runtimeCleared = -not (Test-Path -LiteralPath $statePath)
    } catch {
        $runtimeCleared = $false
        Write-BestEffortLog "Frozen runtime state could not be cleared: $($_.Exception.Message)"
    }
}
$pauseSucceeded = $requiredStagesSucceeded -and $runtimeCleared

if ($pauseSucceeded) {
    $engineMarkerExisted = Test-Path -LiteralPath $engineEnabledPath
    $startupEngineValue = $null
    $startupCodexValue = $null
    try {
        if (Test-Path -LiteralPath $runKey) {
            $runValues = Get-ItemProperty -LiteralPath $runKey -ErrorAction Stop
            if ($null -ne $runValues.GameManagementEngine) {
                $startupEngineValue = [string]$runValues.GameManagementEngine
                Remove-ItemProperty -LiteralPath $runKey -Name 'GameManagementEngine' -Force -ErrorAction Stop
            }
            if ($null -ne $runValues.CodexGameManagement) {
                $startupCodexValue = [string]$runValues.CodexGameManagement
                Remove-ItemProperty -LiteralPath $runKey -Name 'CodexGameManagement' -Force -ErrorAction Stop
            }
        }
        if ($engineMarkerExisted) {
            Remove-Item -LiteralPath $engineEnabledPath -Force -ErrorAction Stop
            if (Test-Path -LiteralPath $engineEnabledPath) { throw 'Engine enabled marker remained after removal.' }
        }
    } catch {
        $pauseSucceeded = $false
        try {
            if ($engineMarkerExisted -and -not (Test-Path -LiteralPath $engineEnabledPath)) {
                [IO.File]::WriteAllText($engineEnabledPath, 'enabled', [Text.UTF8Encoding]::new($false))
            }
            if ($null -ne $startupEngineValue) {
                Set-ItemProperty -LiteralPath $runKey -Name 'GameManagementEngine' -Value $startupEngineValue -Force -ErrorAction Stop
            }
            if ($null -ne $startupCodexValue) {
                Set-ItemProperty -LiteralPath $runKey -Name 'CodexGameManagement' -Value $startupCodexValue -Force -ErrorAction Stop
            }
        } catch {
            Write-BestEffortLog "Engine choice rollback also failed: $($_.Exception.Message)"
        }
        Write-BestEffortLog "Engine marker cleanup failed: $($_.Exception.Message)"
    }
}

if ($pauseSucceeded) {
    $pauseLog = if ($state) { "Game Management paused; restored plan $restoreGuid; timer preserved" } else { 'Game Management paused; timer preserved' }
    Write-BestEffortLog $pauseLog
    exit 0
}
Write-BestEffortLog 'Game Management pause did not complete; engine markers were retained where possible'
exit 1

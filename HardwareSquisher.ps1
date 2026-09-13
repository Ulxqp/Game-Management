param([switch]$Once, [string]$ConfigPath)

$ErrorActionPreference = 'SilentlyContinue'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $ConfigPath) { $ConfigPath = Join-Path $root 'settings.json' }
$statePath = Join-Path $root 'runtime-state.json'
$timerStatePath = Join-Path $root 'timer-state.json'
$logPath = Join-Path $root 'HardwareSquisher.log'
$boostGuid = 'c8b1a303-89f5-4b03-ae3f-10b46a186527'
$balancedGuid = '381b4222-f694-41f0-9685-ff5bb260df2e'
$active = $false
$originalGuid = $null
$originalBrightness = $null
$originalPriorities = @{}
$lastIdleBrightness = $null
$gameBrightnessPercent = 75
$gameTimerEnabled = $true
$gameTimerMinutes = 30
$breakTimerMinutes = 5
$timerPhase = 'Game'
$timerDeadline = $null
$timerAlerted = $false
$timerCycle = 1
$mutex = $null

function Write-Log([string]$message) {
    Add-Content -LiteralPath $logPath -Value "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') $message" -Encoding UTF8
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
    $priorities = @(
        foreach ($entry in $script:originalPriorities.Values) {
            @{ ProcessId = $entry.ProcessId; ProcessName = $entry.ProcessName; PriorityClass = $entry.PriorityClass }
        }
    )
    @{
        OriginalPowerScheme = $script:originalGuid
        OriginalBrightness = $script:originalBrightness
        OriginalPriorities = $priorities
        BoostStarted = (Get-Date).ToString('o')
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

function Update-GameTimer {
    if (-not $script:gameTimerEnabled -or $null -eq $script:timerDeadline -or $script:timerAlerted) { return }
    if ((Get-Date) -lt $script:timerDeadline) { return }

    $alertApp = Join-Path $root 'HardwareSquisher.exe'
    if ($script:timerPhase -eq 'Game') {
        Write-Log "Game timer finished after $script:gameTimerMinutes minutes; break timer started for $script:breakTimerMinutes minutes"
        $script:timerPhase = 'Break'
        $script:timerDeadline = (Get-Date).AddMinutes($script:breakTimerMinutes)
        $script:timerAlerted = $false
        Save-GameTimerState
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
    foreach ($process in @(Get-Process)) {
        if ($excluded -contains $process.ProcessName) { continue }
        try {
            $path = [IO.Path]::GetFullPath([string]$process.Path)
            if (-not $path) { continue }
            foreach ($folder in $folders) {
                if ($path.StartsWith([string]$folder, [StringComparison]::OrdinalIgnoreCase)) {
                    $matches.Add($process)
                    break
                }
            }
        } catch {}
    }
    return @($matches)
}

function Ensure-GamePriority($gameProcesses) {
    foreach ($process in $gameProcesses) {
        try {
            $priorityKey = [string]$process.Id
            if (-not $script:originalPriorities.ContainsKey($priorityKey)) {
                $script:originalPriorities[$priorityKey] = [pscustomobject]@{
                    ProcessId = [int]$process.Id
                    ProcessName = [string]$process.ProcessName
                    PriorityClass = [string]$process.PriorityClass
                }
                Save-State
            }
            if ($process.PriorityClass -ne 'High') {
                $process.PriorityClass = 'High'
                $verified = Get-Process -Id $process.Id -ErrorAction Stop
                if ($verified.PriorityClass -ne 'High') { throw 'High priority did not remain applied' }
                Write-Log "High priority applied to $($process.ProcessName) (PID $($process.Id))"
            }
        } catch {
            try {
                $fallback = Get-Process -Id $process.Id -ErrorAction Stop
                if ($fallback.PriorityClass -notin @('High','AboveNormal')) {
                    $fallback.PriorityClass = 'AboveNormal'
                    Write-Log "Above Normal priority fallback applied to $($fallback.ProcessName) (PID $($fallback.Id))"
                }
            } catch {}
        }
    }
}

function Restore-GamePriorities($savedPriorities) {
    $allowed = @('Idle','BelowNormal','Normal','AboveNormal','High')
    foreach ($saved in @($savedPriorities)) {
        try {
            if ([string]$saved.PriorityClass -notin $allowed) { continue }
            $process = Get-Process -Id ([int]$saved.ProcessId) -ErrorAction Stop
            if ($process.ProcessName -ne [string]$saved.ProcessName) { continue }
            $process.PriorityClass = [string]$saved.PriorityClass
            Write-Log "Priority restored to $($saved.PriorityClass) for $($process.ProcessName) (PID $($process.Id))"
        } catch {}
    }
}

function Start-Boost($gameProcesses) {
    $script:originalGuid = Get-ActiveScheme
    if (-not $script:originalGuid) { $script:originalGuid = $balancedGuid }
    $script:originalBrightness = $script:lastIdleBrightness
    if ($null -eq $script:originalBrightness) { $script:originalBrightness = Get-DisplayBrightness }
    $script:originalPriorities = @{}
    Save-State
    if (-not (Set-Scheme $boostGuid)) {
        Write-Log 'Boost could not activate because the dedicated power plan was unavailable'
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
    Ensure-GamePriority $gameProcesses
    $script:active = $true
    Start-GameTimer
    Write-Log "Hardware Squisher ON; previous plan $script:originalGuid; games: $($gameProcesses.ProcessName -join ', ')"
}

function Stop-Boost {
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
    if (-not $restoreGuid) { $restoreGuid = $balancedGuid }
    if (Set-Scheme $restoreGuid) { Write-Log "Hardware Squisher OFF; restored plan $restoreGuid" }
    else { Write-Log "Hardware Squisher OFF but restoring plan $restoreGuid failed" }
    if ($null -ne $restoreBrightness) {
        if (Set-DisplayBrightness $restoreBrightness) { Write-Log "Brightness restored to $restoreBrightness%" }
        else { Write-Log "Brightness restoration to $restoreBrightness% failed" }
    }
    $restorePriorities = @($script:originalPriorities.Values)
    if ($restorePriorities.Count -eq 0 -and $savedState) {
        $restorePriorities = @($savedState.OriginalPriorities)
    }
    Restore-GamePriorities $restorePriorities
    Clear-State
    Clear-GameTimer
    $script:active = $false
    $script:originalGuid = $null
    $script:originalBrightness = $null
    $script:originalPriorities = @{}
    $script:lastIdleBrightness = Get-DisplayBrightness
}

try {
    $createdNew = $false
    $mutex = [Threading.Mutex]::new($true, 'Local\HardwareSquisherWatcher_v1', [ref]$createdNew)
    if (-not $createdNew) { exit 0 }

    if (Test-Path -LiteralPath $statePath) {
        $stale = Get-Content -Raw -LiteralPath $statePath | ConvertFrom-Json
        if ($stale.OriginalPowerScheme -and (Set-Scheme $stale.OriginalPowerScheme)) {
            Write-Log "Recovered previous plan $($stale.OriginalPowerScheme) after an interrupted run"
        }
        if ($null -ne $stale.OriginalBrightness -and (Set-DisplayBrightness ([int]$stale.OriginalBrightness))) {
            Write-Log "Recovered previous brightness $($stale.OriginalBrightness)% after an interrupted run"
        }
        Restore-GamePriorities @($stale.OriginalPriorities)
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
    $script:lastIdleBrightness = Get-DisplayBrightness
    $folders = Get-GameFolders $config
    $excluded = @($config.excludedProcesses)
    $cachedBrightnessText = if ($null -eq $script:lastIdleBrightness) { 'unavailable' } else { "$script:lastIdleBrightness%" }
    Write-Log "Watcher started in AC-only mode; cached idle brightness: $cachedBrightnessText; monitoring: $($folders -join '; ')"

    do {
        $running = Find-RunningGames $folders $excluded
        $onAcPower = Test-AcPower
        if (-not $onAcPower) {
            if ($active) {
                Write-Log 'AC power disconnected; disabling Hardware Squisher'
                Stop-Boost
            }
            $idleBrightness = Get-DisplayBrightness
            if ($null -ne $idleBrightness) { $script:lastIdleBrightness = $idleBrightness }
        } elseif ($running.Count -gt 0) {
            if (-not $active) { Start-Boost $running }
            elseif ((Get-ActiveScheme) -ne $boostGuid) { Set-Scheme $boostGuid | Out-Null }
            if ($active) {
                Ensure-GamePriority $running
                Update-GameTimer
            }
        } else {
            if ($active) { Stop-Boost }
            $idleBrightness = Get-DisplayBrightness
            if ($null -ne $idleBrightness) { $script:lastIdleBrightness = $idleBrightness }
        }
        if ($Once) { break }
        Start-Sleep -Seconds ([Math]::Max(2, [int]$config.pollSeconds))
    } while ($true)
}
finally {
    if ($active) { Stop-Boost }
    if ($mutex) {
        try { $mutex.ReleaseMutex() } catch {}
        $mutex.Dispose()
    }
}

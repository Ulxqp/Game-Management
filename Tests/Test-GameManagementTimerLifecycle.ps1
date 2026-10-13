param(
    [string]$PackagePath = (Split-Path -Parent $MyInvocation.MyCommand.Path),
    [string]$ReportPath = (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'TIMER-LIFECYCLE-TEST-REPORT.md')
)

$ErrorActionPreference = 'Stop'
if ((Split-Path -Leaf $PackagePath) -eq 'Tests') {
    $PackagePath = Split-Path -Parent $PackagePath
    if (-not $PSBoundParameters.ContainsKey('ReportPath')) {
        $ReportPath = Join-Path $PackagePath 'Documentation\Reports\TIMER-LIFECYCLE-TEST-REPORT.md'
        [IO.Directory]::CreateDirectory((Split-Path -Parent $ReportPath)) | Out-Null
    }
}
$results = [Collections.Generic.List[object]]::new()

function Assert-Lifecycle([string]$name, [bool]$condition, [string]$detail) {
    $results.Add([pscustomobject]@{ Name = $name; Passed = $condition; Detail = $detail })
    if (-not $condition) { throw "$name failed: $detail" }
}

function Import-ProductionFunctions([string]$path, [string[]]$names) {
    $tokens = $null
    $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors)
    if ($errors.Count -gt 0) { throw "Could not parse $path`: $($errors[0].Message)" }
    foreach ($name in $names) {
        $definition = $ast.FindAll({
            param($node)
            $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
        }, $true) | Select-Object -First 1
        if ($null -eq $definition) { throw "Production function $name was not found in $path." }
        $bodyText = $definition.Body.Extent.Text
        $bodyContents = $bodyText.Substring(1, $bodyText.Length - 2)
        $parameterBlock = if ($definition.Parameters.Count -gt 0) {
            'param(' + (($definition.Parameters | ForEach-Object { $_.Extent.Text }) -join ',') + ')'
        } else { '' }
        Set-Item -Path ('Function:script:' + $name) -Value ([ScriptBlock]::Create($parameterBlock + $bodyContents))
    }
}

function Remove-LifecycleTemporaryRoot([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return }
    $resolvedPath = [IO.Path]::GetFullPath($path).TrimEnd('\')
    $expectedParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
    $actualParent = [IO.Directory]::GetParent($resolvedPath).FullName.TrimEnd('\')
    $leaf = [IO.Path]::GetFileName($resolvedPath)
    if (-not [string]::Equals($actualParent, $expectedParent, [StringComparison]::OrdinalIgnoreCase) -or
        $leaf -notmatch '^GameManagementTimerLifecycle-[0-9a-f]{32}$') {
        throw "Refusing to remove unexpected lifecycle test path: $resolvedPath"
    }
    Remove-Item -LiteralPath $resolvedPath -Recurse -Force
}

function Reset-SessionHarness([string]$mode, [string]$testRoot) {
    $script:root = $testRoot
    $script:activeMode = $mode
    $script:statePath = Join-Path $testRoot 'runtime-state.json'
    $script:timerStatePath = Join-Path $testRoot 'timer-state.json'
    $script:workTimerStatePath = Join-Path $testRoot 'work-timer-state.json'
    $script:timerCountersPath = Join-Path $testRoot 'timer-counters.json'
    $script:performanceStatePath = Join-Path $testRoot 'performance-state.json'
    $script:lastSessionPath = Join-Path $testRoot 'last-session.json'
    $script:sessionHistoryPath = Join-Path $testRoot 'GameSessionHistory.txt'
    $script:logPath = Join-Path $testRoot 'GameManagement.log'
    $script:sessionStartedAt = $null
    $script:sessionId = $null
    $script:sessionGameNames = @()
    $script:sessionCycleStart = 0
    $script:sessionElapsedSeconds = 0.0
    $script:sessionRunningSince = $null
    $script:sessionActiveSegments = @()
    $script:gameCompletedCycles = 0
    $script:workCompletedCycles = 0
    $script:timerCycle = 0
    $script:timerDeadline = $null
    $script:timerAlerted = $false
    $script:timerPhase = if ($mode -eq 'work') { 'Work' } else { 'Game' }
    $script:gameTimerEnabled = $false
    $script:sessionSummaryEnabled = $false
    $script:active = $false
    $script:NoInterface = $true
}

$PackagePath = (Resolve-Path -LiteralPath $PackagePath).Path
$watcherPath = Join-Path $PackagePath 'GameManagement.ps1'
$pausePath = Join-Path $PackagePath 'Pause-GameManagement.ps1'
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ('GameManagementTimerLifecycle-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null

try {
    Import-ProductionFunctions $pausePath @(
        'Write-AtomicJson', 'Write-BestEffortLog', 'Freeze-RuntimeSession', 'Freeze-SessionCheckpoint',
        'Freeze-TimerState', 'Test-OwnedWatcherCommand', 'Stop-OwnedWatchers', 'Read-PauseRuntimeState'
    )
    $script:gameSessionStatePath = Join-Path $temporaryRoot 'game-session-state.json'
    $script:workSessionStatePath = Join-Path $temporaryRoot 'work-session-state.json'
    $script:logPath = Join-Path $temporaryRoot 'GameManagement.log'
    $ownedWatcher = Join-Path $temporaryRoot 'GameManagement.ps1'
    $otherWatcher = Join-Path (Join-Path $temporaryRoot 'other') 'GameManagement.ps1'
    Assert-Lifecycle 'Pause owns exact watcher path' (
        (Test-OwnedWatcherCommand "powershell.exe -NoProfile -File `"$ownedWatcher`" -Once" $ownedWatcher) -and
        -not (Test-OwnedWatcherCommand "powershell.exe -NoProfile -File `"$otherWatcher`" -Once" $ownedWatcher) -and
        -not (Test-OwnedWatcherCommand "powershell.exe -Command Get-Content `"$ownedWatcher`"" $ownedWatcher)
    ) 'Pause may stop only a PowerShell -File command targeting the watcher under its own root.'
    Assert-Lifecycle 'Watcher termination failure propagates' (
        -not (Stop-OwnedWatchers @([pscustomobject]@{ ProcessId = 2147483646 }))
    ) 'An unverified watcher termination must make the pause stage fail.'

    $invalidRuntimePath = Join-Path $temporaryRoot 'invalid-runtime.json'
    [IO.File]::WriteAllText($invalidRuntimePath, '{invalid json', [Text.UTF8Encoding]::new($false))
    $invalidRuntime = Read-PauseRuntimeState $invalidRuntimePath
    Assert-Lifecycle 'Invalid runtime state fails closed' (
        [bool]$invalidRuntime.Exists -and -not [bool]$invalidRuntime.Readable -and $null -eq $invalidRuntime.State
    ) 'Malformed runtime JSON must be reported as a required pause failure, not treated as no session.'

    $fallbackPath = Join-Path $temporaryRoot 'fallback-session-state.json'
    Write-AtomicJson $fallbackPath ([ordered]@{
        Mode = 'work'; SessionId = 'fallback-session'; StartedAt = (Get-Date).AddMinutes(-2).ToString('o');
        ElapsedSeconds = 5.0; RunningSince = (Get-Date).AddMilliseconds(-250).ToString('o'); Paused = $false;
        SessionCycleStart = 0; Applications = @('MockWork'); ActiveSegments = @()
    })
    $fallbackFrozen = Freeze-SessionCheckpoint $fallbackPath
    $fallback = Get-Content -Raw -LiteralPath $fallbackPath | ConvertFrom-Json
    Assert-Lifecycle 'Missing runtime checkpoint fallback' (
        $fallbackFrozen -and [bool]$fallback.Paused -and $null -eq $fallback.RunningSince -and
        [double]$fallback.ElapsedSeconds -gt 5.0 -and @($fallback.ActiveSegments).Count -eq 1
    ) 'A running session-state checkpoint must freeze cleanly when runtime-state.json was never committed.'
    [IO.File]::WriteAllText($fallbackPath, '{bad checkpoint', [Text.UTF8Encoding]::new($false))
    Assert-Lifecycle 'Invalid checkpoint fails freeze' (-not (Freeze-SessionCheckpoint $fallbackPath)) 'Malformed fallback session state must fail the pause transaction.'

    $invalidTimerPath = Join-Path $temporaryRoot 'invalid-timer.json'
    [IO.File]::WriteAllText($invalidTimerPath, '{bad timer', [Text.UTF8Encoding]::new($false))
    Assert-Lifecycle 'Invalid timer fails freeze' (-not (Freeze-TimerState $invalidTimerPath)) 'Malformed timer state must fail the pause transaction.'

    $script:logPath = $temporaryRoot
    $loggingFailureRuntime = [pscustomobject]@{
        Mode = 'game'; SessionId = 'logging-failure-session'; ManagementStarted = (Get-Date).AddMinutes(-1).ToString('o');
        SessionElapsedSeconds = 8.0; SessionRunningSince = (Get-Date).AddMilliseconds(-200).ToString('o');
        SessionCycleStart = 0; Applications = @('MockGame'); ActiveSegments = @()
    }
    $loggingCommit = Freeze-RuntimeSession $loggingFailureRuntime
    $loggingCommittedState = Get-Content -Raw -LiteralPath $script:gameSessionStatePath | ConvertFrom-Json
    Assert-Lifecycle 'Logging failure does not roll back session commit' (
        $loggingCommit -and [bool]$loggingCommittedState.Paused -and $loggingCommittedState.SessionId -eq 'logging-failure-session'
    ) 'A log write failure after atomic session persistence must still report commit success so stale runtime can be cleared.'
    $script:logPath = Join-Path $temporaryRoot 'GameManagement.log'
    $loggingElapsed = [double]$loggingCommittedState.ElapsedSeconds
    Start-Sleep -Milliseconds 100
    $retryCommit = Freeze-RuntimeSession $loggingFailureRuntime
    $retryState = Get-Content -Raw -LiteralPath $script:gameSessionStatePath | ConvertFrom-Json
    Assert-Lifecycle 'Pause retry honors committed checkpoint' (
        $retryCommit -and [Math]::Abs(([double]$retryState.ElapsedSeconds - $loggingElapsed)) -lt 0.001 -and
        @($retryState.ActiveSegments).Count -eq @($loggingCommittedState.ActiveSegments).Count
    ) 'A retained stale runtime must not add paused downtime when its matching session is already committed as paused.'
    Remove-Item -LiteralPath $script:gameSessionStatePath -Force

    $pausedFixtures = @{}
    foreach ($mode in @('game', 'work')) {
        $sessionId = "pause-$mode-session"
        $runningSince = (Get-Date).AddMilliseconds(-350)
        $runtime = [pscustomobject]@{
            Mode = $mode
            SessionId = $sessionId
            ManagementStarted = (Get-Date).AddMinutes(-5).ToString('o')
            SessionElapsedSeconds = 10.0
            SessionRunningSince = $runningSince.ToString('o')
            SessionCycleStart = 2
            Applications = @("Mock-$mode")
            ActiveSegments = @([pscustomobject]@{
                StartedAt = (Get-Date).AddMinutes(-5).ToString('o')
                EndedAt = (Get-Date).AddMinutes(-4).AddSeconds(-50).ToString('o')
            })
        }
        Assert-Lifecycle "$mode pause writer" (Freeze-RuntimeSession $runtime) 'The production pause writer should freeze the runtime session.'
        $pausedPath = if ($mode -eq 'work') { $script:workSessionStatePath } else { $script:gameSessionStatePath }
        $paused = Get-Content -Raw -LiteralPath $pausedPath | ConvertFrom-Json
        Assert-Lifecycle "$mode paused identity and elapsed" (
            [bool]$paused.Paused -and $paused.SessionId -eq $sessionId -and
            [double]$paused.ElapsedSeconds -gt 10.1 -and @($paused.ActiveSegments).Count -eq 2
        ) 'Pause must keep the session ID, accumulate current runtime, and close one active interval.'
        $pausedFixtures[$mode] = $paused
    }

    Import-ProductionFunctions $watcherPath @(
        'Write-Log', 'Write-AtomicJson', 'Get-SessionStatePath', 'Get-SessionElapsedSeconds',
        'Close-SessionActiveSegment', 'Save-SessionState', 'Clear-SessionState', 'Get-MatchingPausedSession',
        'Resume-SavedSession', 'Recover-InterruptedSession',
        'Get-ConfiguredTimerSeconds', 'Format-InvariantMinutes', 'Get-NextWatcherWaitMilliseconds',
        'Save-CycleCounters', 'Get-CompletedCycles', 'Set-CompletedCycles', 'Format-SessionDuration',
        'Write-GameSessionSummary', 'Save-GameTimerState', 'Start-GameTimer', 'Update-GameTimer',
        'Clear-GameTimer', 'Finalize-PausedSessionIfInactive', 'Update-InactiveTimerState',
        'Process-TimerResetRequest'
    )

    $durationSettings = [pscustomobject]@{
        gameTimerSeconds = 15
        breakTimerSeconds = 65
        workTimerSeconds = 3661
        workBreakSeconds = $null
        workBreakMinutes = 2.5
    }
    Assert-Lifecycle 'Second duration resolver' (
        (Get-ConfiguredTimerSeconds $durationSettings 'gameTimerSeconds' 'gameTimerMinutes' 1800) -eq 15 -and
        (Get-ConfiguredTimerSeconds $durationSettings 'breakTimerSeconds' 'breakTimerMinutes' 300) -eq 65 -and
        (Get-ConfiguredTimerSeconds $durationSettings 'workTimerSeconds' 'workTimerMinutes' 1800) -eq 3661 -and
        (Get-ConfiguredTimerSeconds $durationSettings 'workBreakSeconds' 'workBreakMinutes' 300) -eq 150
    ) 'Nullable seconds must win when present, while an absent seconds value falls back to legacy minutes.'

    foreach ($mode in @('game', 'work')) {
        Reset-SessionHarness $mode $temporaryRoot
        $pausedPath = Get-SessionStatePath
        Write-AtomicJson $pausedPath $pausedFixtures[$mode]
        $expectedId = [string]$pausedFixtures[$mode].SessionId
        $expectedElapsed = [double]$pausedFixtures[$mode].ElapsedSeconds
        $staleRuntime = [pscustomobject]@{
            Mode = $mode; SessionId = $expectedId; ManagementStarted = [string]$pausedFixtures[$mode].StartedAt
        }
        Recover-InterruptedSession $staleRuntime (Get-Date)
        $preservedAfterRecovery = Get-Content -Raw -LiteralPath $pausedPath | ConvertFrom-Json
        Assert-Lifecycle "$mode paused checkpoint precedes stale runtime" (
            [bool]$preservedAfterRecovery.Paused -and $preservedAfterRecovery.SessionId -eq $expectedId
        ) 'Interrupted recovery must return before its clearing finally block when runtime matches a persisted paused session.'
        $resumed = Resume-SavedSession @([pscustomobject]@{ ProcessName = "Mock-$mode" })
        Start-Sleep -Milliseconds 75
        Save-SessionState $true
        $refrozen = Get-Content -Raw -LiteralPath $pausedPath | ConvertFrom-Json
        Assert-Lifecycle "$mode resume continuity" (
            $resumed -and $refrozen.SessionId -eq $expectedId -and
            [double]$refrozen.ElapsedSeconds -ge $expectedElapsed -and
            @($refrozen.ActiveSegments).Count -eq 3
        ) 'Resume must retain identity and elapsed baseline, then append only the new active interval.'
    }

    $handoffRoot = Join-Path $temporaryRoot 'handoff'
    [IO.Directory]::CreateDirectory($handoffRoot) | Out-Null
    Reset-SessionHarness 'game' $handoffRoot
    $script:sessionStartedAt = (Get-Date).AddHours(-2)
    $script:sessionId = 'mode-handoff-session'
    $script:sessionGameNames = @('MockGame')
    $script:sessionElapsedSeconds = 180.0
    $script:sessionRunningSince = $null
    $script:sessionActiveSegments = @([pscustomobject]@{
        StartedAt = (Get-Date).AddHours(-2).ToString('o')
        EndedAt = (Get-Date).AddHours(-2).AddMinutes(3).ToString('o')
    })
    Save-SessionState $true
    Assert-Lifecycle 'Mode handoff does not write history' (-not (Test-Path -LiteralPath $script:sessionHistoryPath)) 'Preserving a handoff should only write paused session state.'
    Assert-Lifecycle 'Paused inactive session finalizes' (Finalize-PausedSessionIfInactive) 'A paused session must finalize when the selected application is absent at next enable.'
    Finalize-PausedSessionIfInactive | Out-Null
    $handoffHistory = Get-Content -Raw -LiteralPath $script:sessionHistoryPath
    $handoffCount = ([regex]::Matches($handoffHistory, 'SessionId: mode-handoff-session')).Count
    Assert-Lifecycle 'Mode handoff history deduplicates' ($handoffCount -eq 1 -and -not (Test-Path -LiteralPath (Get-SessionStatePath))) 'The preserved session must produce at most one history block and then clear.'

    $disabledRoot = Join-Path $temporaryRoot 'disabled'
    [IO.Directory]::CreateDirectory($disabledRoot) | Out-Null
    Reset-SessionHarness 'work' $disabledRoot
    $script:sessionStartedAt = (Get-Date).AddMinutes(-1)
    $script:sessionId = 'disabled-timer-session'
    $script:sessionGameNames = @('MockWork')
    $script:sessionRunningSince = (Get-Date).AddSeconds(-1)
    $beforeDisabled = Get-SessionElapsedSeconds
    Start-Sleep -Milliseconds 150
    $afterDisabled = Get-SessionElapsedSeconds
    Save-SessionState $true
    Assert-Lifecycle 'Disabled countdown still tracks session' (
        -not $script:gameTimerEnabled -and $afterDisabled -gt $beforeDisabled -and
        -not (Test-Path -LiteralPath $script:workTimerStatePath)
    ) 'Session elapsed time must advance independently when the countdown is disabled.'

    $script:active = $true
    $script:activeMode = 'work'
    $script:gameTimerEnabled = $false
    $script:timerDeadline = $null
    $script:gameCompletedCycles = 4
    $script:workCompletedCycles = 7
    $script:sessionCycleStart = 7
    $script:config = [pscustomobject]@{ workTimerSeconds = 15; workBreakSeconds = 2 }
    $script:timerResetRequestPath = Join-Path $disabledRoot 'timer-reset-request.json'
    $script:timerResetAckPath = Join-Path $disabledRoot 'timer-reset-ack.json'
    $requester = [Diagnostics.Process]::GetCurrentProcess()
    Write-AtomicJson $script:timerResetRequestPath ([ordered]@{
        RequestId = 'disabled-reset-cycles'; Mode = 'work'; Action = 'reset-cycles';
        RequesterPid = $requester.Id; RequesterStartTicks = $requester.StartTime.ToUniversalTime().Ticks;
        ExpiresUtc = (Get-Date).ToUniversalTime().AddMinutes(1).ToString('o')
    })
    Process-TimerResetRequest
    $disabledResetAck = Get-Content -Raw -LiteralPath $script:timerResetAckPath | ConvertFrom-Json
    $disabledCounters = Get-Content -Raw -LiteralPath $script:timerCountersPath | ConvertFrom-Json
    Assert-Lifecycle 'Disabled timer reset-cycles is independent' (
        [bool]$disabledResetAck.Succeeded -and [int]$disabledCounters.Work -eq 0 -and
        [int]$disabledCounters.Game -eq 4 -and $null -eq $script:timerDeadline -and
        -not (Test-Path -LiteralPath $script:workTimerStatePath)
    ) 'Reset Cycles must commit counters without dereferencing a disabled timer deadline or creating countdown state.'

    Reset-SessionHarness 'game' (Join-Path $temporaryRoot 'segments')
    [IO.Directory]::CreateDirectory($script:root) | Out-Null
    $segmentStart = [datetimeoffset]'2026-10-11T23:59:50+08:00'
    $script:sessionRunningSince = $segmentStart.LocalDateTime
    Close-SessionActiveSegment $segmentStart.AddSeconds(15).LocalDateTime
    $script:sessionRunningSince = $segmentStart.AddMinutes(10).LocalDateTime
    Close-SessionActiveSegment $segmentStart.AddMinutes(10).AddSeconds(35).LocalDateTime
    Assert-Lifecycle 'Active interval accumulation' (
        [Math]::Abs($script:sessionElapsedSeconds - 50.0) -lt 0.01 -and
        @($script:sessionActiveSegments).Count -eq 2
    ) 'Closed intervals crossing a day boundary must total only active seconds and retain both boundaries.'

    $timerRoot = Join-Path $temporaryRoot 'deadline'
    [IO.Directory]::CreateDirectory($timerRoot) | Out-Null
    Reset-SessionHarness 'work' $timerRoot
    $script:gameTimerEnabled = $true
    $script:active = $true
    $script:gameTimerSeconds = 15
    $script:breakTimerSeconds = 2
    $script:gameTimerMinutes = 15.0 / 60.0
    $script:breakTimerMinutes = 2.0 / 60.0
    $nextProcessScanAt = (Get-Date).AddSeconds(60)
    $scanCount = 1
    Start-GameTimer
    $firstWait = Get-NextWatcherWaitMilliseconds $nextProcessScanAt
    $wallClock = [Diagnostics.Stopwatch]::StartNew()
    while ($script:timerCycle -lt 1 -and $wallClock.Elapsed.TotalSeconds -lt 25) {
        Update-GameTimer @()
        if ((Get-Date) -ge $nextProcessScanAt) {
            $scanCount++
            $nextProcessScanAt = (Get-Date).AddSeconds(60)
        }
        if ($script:timerCycle -lt 1) {
            Start-Sleep -Milliseconds (Get-NextWatcherWaitMilliseconds $nextProcessScanAt)
        }
    }
    $wallClock.Stop()
    $counter = Get-Content -Raw -LiteralPath $script:timerCountersPath | ConvertFrom-Json
    $watcherText = [IO.File]::ReadAllText($watcherPath)
    Assert-Lifecycle 'Deadline wake beats process poll' (
        $firstWait -ge 13000 -and $firstWait -le 16000 -and
        $script:timerCycle -eq 1 -and [int]$counter.Work -eq 1 -and
        $scanCount -eq 1 -and $wallClock.Elapsed.TotalSeconds -lt 22
    ) 'A 15-second focus plus 2-second break must complete one cycle without waiting for or repeating a 60-second process scan.'
    Assert-Lifecycle 'Watcher wake remains interruptible' (
        $watcherText.Contains('$wakeEvent.WaitOne($waitMilliseconds)') -and
        -not $watcherText.Contains('$wakeEvent.WaitOne([Math]::Max(2000')
    ) 'The calculated deadline timeout must still use the wake event so UI/settings signals are not delayed.'
}
finally {
    Remove-LifecycleTemporaryRoot $temporaryRoot
}

$failed = @($results | Where-Object { -not $_.Passed })
$reportLines = @(
    '# Game Management Timer Lifecycle Test Report'
    ''
    "Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')"
    ''
    "Overall result: **$(if ($failed.Count -eq 0) { 'PASS' } else { 'FAIL' })**"
    ''
    '| Test | Result | Detail |'
    '|---|---:|---|'
)
foreach ($result in $results) {
    $reportLines += "| $($result.Name) | $(if ($result.Passed) { 'PASS' } else { 'FAIL' }) | $($result.Detail -replace '\|', '\|') |"
}
$reportLines | Set-Content -LiteralPath $ReportPath -Encoding UTF8
$results | Format-Table -AutoSize
if ($failed.Count -gt 0) { exit 1 }
[pscustomobject]@{ Overall = 'PASS'; Passed = $results.Count; Failed = 0; Report = $ReportPath }

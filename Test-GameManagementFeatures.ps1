param(
    [string]$PackagePath = (Split-Path -Parent $MyInvocation.MyCommand.Path),
    [string]$ReportPath = (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'FEATURE-TEST-REPORT.md')
)

$ErrorActionPreference = 'Stop'
if ((Split-Path -Leaf $PackagePath) -eq 'Tests') {
    $PackagePath = Split-Path -Parent $PackagePath
    if (-not $PSBoundParameters.ContainsKey('ReportPath')) {
        $ReportPath = Join-Path $PackagePath 'Documentation\Reports\FEATURE-TEST-REPORT.md'
        [IO.Directory]::CreateDirectory((Split-Path -Parent $ReportPath)) | Out-Null
    }
}
$results = [Collections.Generic.List[object]]::new()
function Add-Result([string]$Name, [bool]$Passed, [string]$Detail) {
    $results.Add([pscustomobject]@{ Name = $Name; Passed = $Passed; Detail = $Detail })
}
function Assert-Result([string]$Name, [bool]$Condition, [string]$Detail) {
    Add-Result $Name $Condition $Detail
    if (-not $Condition) { throw "$Name failed: $Detail" }
}
function Get-ChildControls($Parent) {
    foreach ($control in $Parent.Controls) {
        $control
        Get-ChildControls $control
    }
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ('GameManagementFeatureTest-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
try {
    $historyPath = Join-Path $temporaryRoot 'GameSessionHistory.txt'
    $history = @'
------------------------------------------------------------
SessionId: test-cross-month-start
Date: 2026-10-01
Mode: game
Applications: ExampleGame
Started: 2026-09-30 23:30:00
Finished: 2026-10-01 00:30:00
Total game time: 1 hr 0 min (1 hours)
Timer cycles: 2
Peak CPU temperature: 70°C
Peak GPU temperature: 72°C
------------------------------------------------------------
SessionId: test-work
Date: 2026-10-02
Mode: work
Applications: Code
Started: 2026-10-02 10:00:00
Finished: 2026-10-02 11:00:00
Total work time: 1 hr 0 min (1 hours)
Timer cycles: 1
Peak CPU temperature: 60°C
Peak GPU temperature: Unavailable
------------------------------------------------------------
SessionId: test-cross-month-end
Date: 2026-11-01
Mode: game
Applications: AnotherGame
Started: 2026-10-31 23:30:00
Finished: 2026-11-01 00:30:00
Total game time: 1 hr 0 min (1 hours)
Timer cycles: 3
Peak CPU temperature: 68°C
Peak GPU temperature: 74°C
'@
    [IO.File]::WriteAllText($historyPath, $history, [Text.UTF8Encoding]::new($false))

    $applicationPath = Join-Path $PackagePath 'GameManagement.exe'
    $assembly = [Reflection.Assembly]::LoadFile($applicationPath)
    $reportType = $assembly.GetType('GameManagement.WeeklyReport', $true)
    $flags = [Reflection.BindingFlags]'Static,NonPublic'
    $loadMethod = $reportType.GetMethod('Load', $flags)
    $weeklyMethod = $reportType.GetMethod('Build', $flags)
    $monthlyMethod = $reportType.GetMethod('BuildMonthly', $flags)
    $sessions = $loadMethod.Invoke($null, [object[]]@([string]$historyPath))
    Assert-Result 'History parser' ($sessions.Count -eq 3) 'Three saved sessions, including cross-boundary sessions, were loaded.'

    $monthly = [string]$monthlyMethod.Invoke($null, [object[]]@($sessions, [datetime]'2026-10-01'))
    Assert-Result 'Monthly boundary totals' (
        $monthly.Contains('Game Mode: 1h 00m (2 sessions)') -and
        $monthly.Contains('Work Mode: 1h 00m (1 session)') -and
        $monthly.Contains('Total tracked time: 2h 00m')
    ) 'October includes only the October portions of sessions that cross into or out of the month.'

    $weekly = [string]$weeklyMethod.Invoke($null, [object[]]@($sessions, [datetime]'2026-10-26'))
    Assert-Result 'Weekly boundary totals' (
        $weekly.Contains('Game Mode: 1h 00m (1 session)') -and
        $weekly.Contains('AnotherGame')
    ) 'The final October session is included correctly in its Monday-to-Sunday week.'

    $detectorType = $assembly.GetType('GameManagement.ForegroundModeDetector', $true)
    $classifyMethod = $detectorType.GetMethod('Classify', [Reflection.BindingFlags]'Static,NonPublic')
    $gamePath = 'C:\Games\Example\Example.exe'
    $workPath = 'C:\Tools\Editor.exe'
    $folderGamePath = 'D:\Library\AnotherGame\Play.exe'
    $gameResult = [string]$classifyMethod.Invoke($null, [object[]]@($gamePath, [string[]]@($gamePath), [string[]]@($workPath), [string[]]@('D:\Library')))
    $workResult = [string]$classifyMethod.Invoke($null, [object[]]@($workPath, [string[]]@($gamePath), [string[]]@($workPath), [string[]]@('D:\Library')))
    $folderResult = [string]$classifyMethod.Invoke($null, [object[]]@($folderGamePath, [string[]]@(), [string[]]@($workPath), [string[]]@('D:\Library')))
    $unassignedResult = $classifyMethod.Invoke($null, [object[]]@('C:\Other\App.exe', [string[]]@($gamePath), [string[]]@($workPath), [string[]]@('D:\Library')))
    $conflictResult = $classifyMethod.Invoke($null, [object[]]@($gamePath, [string[]]@($gamePath), [string[]]@($gamePath), [string[]]@()))
    Assert-Result 'Foreground application classification' (
        $gameResult -eq 'game' -and $workResult -eq 'work' -and $folderResult -eq 'game' -and
        $null -eq $unassignedResult -and $null -eq $conflictResult
    ) 'Exact Game/Work assignments and game-library folders classify correctly; unassigned and conflicting entries do not trigger a switch.'

    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    $formType = $assembly.GetType('GameManagement.WeeklyReportForm', $true)
    $formConstructor = $formType.GetConstructors([Reflection.BindingFlags]'Instance,Public,NonPublic') |
        Where-Object { $_.GetParameters().Count -eq 1 } | Select-Object -First 1
    $form = $formConstructor.Invoke([object[]]@([string]$historyPath))
    try {
        $form.Show()
        [Windows.Forms.Application]::DoEvents()
        $allControls = @(Get-ChildControls $form)
        $tab = @($allControls | Where-Object { $_ -is [Windows.Forms.TabControl] }) | Select-Object -First 1
        $list = @($allControls | Where-Object { $_ -is [Windows.Forms.ListView] }) | Select-Object -First 1
        Assert-Result 'Reports and history interface' ($null -ne $tab -and $tab.TabPages.Count -eq 2 -and $null -ne $list -and $list.Items.Count -eq 3) 'The dialog exposes Reports and Session history and lists all fixture sessions.'
        $previewPath = Join-Path $temporaryRoot 'reports-preview.png'
        $bitmap = [Drawing.Bitmap]::new($form.Width, $form.Height)
        try {
            $form.DrawToBitmap($bitmap, [Drawing.Rectangle]::new(0, 0, $bitmap.Width, $bitmap.Height))
            $bitmap.Save($previewPath, [Drawing.Imaging.ImageFormat]::Png)
        } finally { $bitmap.Dispose() }
        Assert-Result 'Reports dialog render' ((Test-Path -LiteralPath $previewPath) -and (Get-Item -LiteralPath $previewPath).Length -gt 1000) 'The Windows 98-style report dialog renders without an exception.'
    } finally { $form.Dispose() }

    $settingsText = [IO.File]::ReadAllText((Join-Path $PackagePath 'settings.json'))
    $settings = $settingsText | ConvertFrom-Json
    $popupDefault = if ($null -eq $settings.timerPopupEnabled) { $true } else { [bool]$settings.timerPopupEnabled }
    $soundDefault = if ($null -eq $settings.timerSoundEnabled) { $true } else { [bool]$settings.timerSoundEnabled }
    $summaryDefault = if ($null -eq $settings.sessionSummaryEnabled) { $true } else { [bool]$settings.sessionSummaryEnabled }
    $automaticModeDefault = if ($null -eq $settings.automaticModeDetection) { $false } else { [bool]$settings.automaticModeDetection }
    Assert-Result 'Notification defaults' (
        $popupDefault -and $soundDefault -and $summaryDefault
    ) 'Popup, sound, and summary notifications default to enabled, including when an upgraded settings file omits the new fields.'
    Assert-Result 'Automatic mode detection safe default' (-not $automaticModeDefault) 'Automatic mode detection is opt-in so an upgrade does not unexpectedly change modes.'

    $isolatedRoot = Join-Path $temporaryRoot 'isolated-runtime'
    Write-Host 'Feature test: notification behavior'
    [IO.Directory]::CreateDirectory($isolatedRoot) | Out-Null
    [IO.File]::Copy($applicationPath, (Join-Path $isolatedRoot 'GameManagement.exe'), $true)
    [IO.File]::Copy((Join-Path $PackagePath 'GameManagement.ps1'), (Join-Path $isolatedRoot 'GameManagement.ps1'), $true)
    $isolatedSettings = @{
        pollSeconds = 2
        gameBrightnessPercent = 75
        gameTimerEnabled = $false
        gameTimerMinutes = 30
        breakTimerMinutes = 5
        workTimerEnabled = $false
        workTimerMinutes = 30
        workBreakMinutes = 5
        pauseGameWithEscape = $false
        timerPopupEnabled = $false
        timerSoundEnabled = $false
        sessionSummaryEnabled = $false
        activeMode = 'work'
        workApps = @()
        gameApps = @()
        gameFolders = @()
        excludedProcesses = @()
    } | ConvertTo-Json -Depth 4
    [IO.File]::WriteAllText((Join-Path $isolatedRoot 'settings.json'), $isolatedSettings, [Text.UTF8Encoding]::new($false))
    $sourcePath = Join-Path $PackagePath 'GameManagement.cs'
    if (-not (Test-Path -LiteralPath $sourcePath)) { $sourcePath = Join-Path $PackagePath 'Source\GameManagement.cs' }
    $applicationSource = [IO.File]::ReadAllText($sourcePath)
    $isolatedSettingsObject = $isolatedSettings | ConvertFrom-Json
    Assert-Result 'Suppressed timer notification' (
        -not [bool]$isolatedSettingsObject.timerPopupEnabled -and
        -not [bool]$isolatedSettingsObject.timerSoundEnabled -and
        $applicationSource.Contains('if (!showPopup)') -and
        $applicationSource.Contains('if (playSound) TimerAlertForm.PlayOneAlarmSound();')
    ) 'With popup and sound disabled, the alert code returns before opening a dialog or playing a sound.'

    $timerRoot = Join-Path $temporaryRoot 'timer-runtime'
    Write-Host 'Feature test: pause and resume'
    [IO.Directory]::CreateDirectory($timerRoot) | Out-Null
    [IO.File]::Copy((Join-Path $PackagePath 'GameManagement.ps1'), (Join-Path $timerRoot 'GameManagement.ps1'), $true)
    [IO.File]::Copy((Join-Path $PackagePath 'Pause-GameManagement.ps1'), (Join-Path $timerRoot 'Pause-GameManagement.ps1'), $true)
    $powershellPath = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $timerSettings = @{
        pollSeconds = 2
        gameBrightnessPercent = 75
        gameTimerEnabled = $false
        gameTimerMinutes = 30
        breakTimerMinutes = 5
        workTimerEnabled = $true
        workTimerMinutes = 30
        workBreakMinutes = 5
        pauseGameWithEscape = $false
        sessionSummaryEnabled = $false
        activeMode = 'work'
        workApps = @($powershellPath)
        gameApps = @()
        gameFolders = @()
        excludedProcesses = @()
    } | ConvertTo-Json -Depth 4
    $timerSettingsPath = Join-Path $timerRoot 'settings.json'
    [IO.File]::WriteAllText($timerSettingsPath, $timerSettings, [Text.UTF8Encoding]::new($false))
    $workTimerPath = Join-Path $timerRoot 'work-timer-state.json'
    $runningTimer = @{
        Phase = 'Work'
        StartedAt = (Get-Date).AddMinutes(-1).ToString('o')
        Deadline = (Get-Date).AddSeconds(125).ToString('o')
        Minutes = 30
        Alerted = $false
        Cycle = 3
        Mode = 'work'
        Paused = $false
        RemainingSeconds = $null
    } | ConvertTo-Json
    [IO.File]::WriteAllText($workTimerPath, $runningTimer, [Text.UTF8Encoding]::new($false))
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $timerRoot 'Pause-GameManagement.ps1') -TimerStateOnly
    $freezeExitCode = $LASTEXITCODE
    $frozenTimer = [IO.File]::ReadAllText($workTimerPath) | ConvertFrom-Json
    $frozenSeconds = [int]$frozenTimer.RemainingSeconds
    $freezeLog = if (Test-Path -LiteralPath (Join-Path $timerRoot 'GameManagement.log')) { [IO.File]::ReadAllText((Join-Path $timerRoot 'GameManagement.log')).Trim() } else { 'no log' }
    Assert-Result 'Timer pause freezes exact state' (
        $freezeExitCode -eq 0 -and [bool]$frozenTimer.Paused -and
        $frozenTimer.Phase -eq 'Work' -and [int]$frozenTimer.Cycle -eq 3 -and
        $frozenSeconds -ge 115 -and $frozenSeconds -le 125
    ) "The real pause-state writer should preserve Work/cycle 3/about 125 seconds. Observed exit=$freezeExitCode paused=$($frozenTimer.Paused) phase=$($frozenTimer.Phase) cycle=$($frozenTimer.Cycle) seconds=$frozenSeconds log=$freezeLog."

    $timerMutex = 'Local\GameManagementTimerFeatureTest_' + [guid]::NewGuid().ToString('N')
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $timerRoot 'GameManagement.ps1') -Once -NoInterface -ConfigPath $timerSettingsPath -MutexName $timerMutex -WakeEventName ($timerMutex + '_Wake')
    $resumeExitCode = $LASTEXITCODE
    $timerLog = [IO.File]::ReadAllText((Join-Path $timerRoot 'GameManagement.log'))
    Assert-Result 'Timer resumes frozen remainder' (
        $resumeExitCode -eq 0 -and
        $timerLog -match 'Work timer resumed with \d+ seconds left in cycle 3' -and
        $timerLog -notmatch 'Work timer started for 30 minutes'
    ) 'The real watcher resumes the frozen Work timer and does not start a fresh 30-minute countdown.'

    $counterPath = Join-Path $timerRoot 'timer-counters.json'
    Write-Host 'Feature test: completed cycle counting'
    [IO.File]::WriteAllText($counterPath, (@{ Game = 0; Work = 0; UpdatedAt = (Get-Date).ToString('o') } | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    $finishingBreak = @{
        Phase = 'Break'
        StartedAt = (Get-Date).AddMinutes(-6).ToString('o')
        Deadline = (Get-Date).ToString('o')
        Minutes = 5
        Alerted = $false
        Cycle = 0
        Mode = 'work'
        Paused = $true
        RemainingSeconds = 0
        Status = 'Paused'
    } | ConvertTo-Json
    [IO.File]::WriteAllText($workTimerPath, $finishingBreak, [Text.UTF8Encoding]::new($false))
    $cycleMutex = 'Local\GameManagementCycleFeatureTest_' + [guid]::NewGuid().ToString('N')
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $timerRoot 'GameManagement.ps1') -Once -NoInterface -ConfigPath $timerSettingsPath -MutexName $cycleMutex -WakeEventName ($cycleMutex + '_Wake')
    $firstCycleExitCode = $LASTEXITCODE
    $firstCycleCount = [int](([IO.File]::ReadAllText($counterPath) | ConvertFrom-Json).Work)
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $timerRoot 'GameManagement.ps1') -Once -NoInterface -ConfigPath $timerSettingsPath -MutexName ($cycleMutex + '_Second') -WakeEventName ($cycleMutex + '_SecondWake')
    $secondCycleExitCode = $LASTEXITCODE
    $secondCycleCount = [int](([IO.File]::ReadAllText($counterPath) | ConvertFrom-Json).Work)
    Assert-Result 'Completed cycle increments exactly once' (
        $firstCycleExitCode -eq 0 -and $secondCycleExitCode -eq 0 -and
        $firstCycleCount -eq 1 -and $secondCycleCount -eq 1
    ) 'A completed Work break increments Work cycles from 0 to 1, and a later watcher pass cannot duplicate the increment.'

    [IO.File]::WriteAllText($counterPath, (@{ Game = 4; Work = 7; UpdatedAt = (Get-Date).ToString('o') } | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    Write-Host 'Feature test: independent timer reset'
    [IO.File]::WriteAllText($workTimerPath, (@{
        Phase = 'Break'; Deadline = (Get-Date).AddMinutes(2).ToString('o'); Minutes = 5; Cycle = 7;
        Mode = 'work'; Paused = $true; RemainingSeconds = 120; Status = 'Paused'
    } | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    $resetRequestId = [guid]::NewGuid().ToString('N')
    $currentProcess = [Diagnostics.Process]::GetCurrentProcess()
    $resetRequest = @{
        RequestId = $resetRequestId
        Mode = 'work'
        Action = 'reset-all'
        RequesterPid = $currentProcess.Id
        RequesterStartTicks = $currentProcess.StartTime.ToUniversalTime().Ticks
        ExpiresUtc = (Get-Date).ToUniversalTime().AddSeconds(30).ToString('o')
    } | ConvertTo-Json
    [IO.File]::WriteAllText((Join-Path $timerRoot 'timer-reset-request.json'), $resetRequest, [Text.UTF8Encoding]::new($false))
    $resetMutex = 'Local\GameManagementResetFeatureTest_' + [guid]::NewGuid().ToString('N')
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $timerRoot 'GameManagement.ps1') -Once -NoInterface -ConfigPath $timerSettingsPath -MutexName $resetMutex -WakeEventName ($resetMutex + '_Wake')
    $resetExitCode = $LASTEXITCODE
    $resetAck = [IO.File]::ReadAllText((Join-Path $timerRoot 'timer-reset-ack.json')) | ConvertFrom-Json
    $resetCounters = [IO.File]::ReadAllText($counterPath) | ConvertFrom-Json
    Assert-Result 'Independent reset-all command' (
        $resetExitCode -eq 0 -and [bool]$resetAck.Succeeded -and $resetAck.RequestId -eq $resetRequestId -and
        [int]$resetCounters.Work -eq 0 -and [int]$resetCounters.Game -eq 4
    ) 'Reset All clears only the selected Work timer/counter and preserves the Game completed-cycle total.'

    $recoverySessionId = 'feature-test-recovery-session'
    Write-Host 'Feature test: interrupted session recovery'
    $runtimeState = @{
        SessionId = $recoverySessionId
        OriginalPowerScheme = $null
        OriginalBrightness = $null
        ManagementStarted = (Get-Date).AddMinutes(-3).ToString('o')
        Mode = 'work'
        Applications = @('FeatureTestApp')
        TimerEnabled = $false
        TimerCycle = 0
    } | ConvertTo-Json -Depth 4
    $runtimeStatePath = Join-Path $isolatedRoot 'runtime-state.json'
    [IO.File]::WriteAllText($runtimeStatePath, $runtimeState, [Text.UTF8Encoding]::new($false))
    $isolatedWatcherPath = Join-Path $isolatedRoot 'GameManagement.ps1'
    $isolatedSettingsPath = Join-Path $isolatedRoot 'settings.json'
    $isolatedMutexName = 'Local\GameManagementFeatureTest_' + [guid]::NewGuid().ToString('N')
    $isolatedWakeName = $isolatedMutexName + '_Wake'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $isolatedWatcherPath -Once -ConfigPath $isolatedSettingsPath -MutexName $isolatedMutexName -WakeEventName $isolatedWakeName
    $firstRecoveryExitCode = $LASTEXITCODE
    [IO.File]::WriteAllText($runtimeStatePath, $runtimeState, [Text.UTF8Encoding]::new($false))
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $isolatedWatcherPath -Once -ConfigPath $isolatedSettingsPath -MutexName $isolatedMutexName -WakeEventName $isolatedWakeName
    $secondRecoveryExitCode = $LASTEXITCODE
    $recoveryHistoryPath = Join-Path $isolatedRoot 'GameSessionHistory.txt'
    $recoveryLog = if (Test-Path -LiteralPath (Join-Path $isolatedRoot 'GameManagement.log')) { [IO.File]::ReadAllText((Join-Path $isolatedRoot 'GameManagement.log')) } else { 'No recovery log.' }
    if (-not (Test-Path -LiteralPath $recoveryHistoryPath)) { throw "Recovery history was not created. Log: $recoveryLog" }
    $recoveredHistory = [IO.File]::ReadAllText($recoveryHistoryPath)
    $recoveryCount = ([regex]::Matches($recoveredHistory, [regex]::Escape("SessionId: $recoverySessionId"))).Count
    Assert-Result 'Interrupted session runtime recovery' (
        $firstRecoveryExitCode -eq 0 -and
        $secondRecoveryExitCode -eq 0 -and
        $recoveryCount -eq 1 -and
        $recoveredHistory.Contains('Mode: work') -and
        $recoveredHistory.Contains('Applications: FeatureTestApp')
    ) 'An isolated three-minute interrupted Work session is restored to history once, even when the same recovery state is replayed.'

    $watcher = [IO.File]::ReadAllText((Join-Path $PackagePath 'GameManagement.ps1'))
    Assert-Result 'Crash recovery deduplication' (
        $watcher.Contains('Recover-InterruptedSession') -and
        $watcher.Contains('SessionId: $currentSessionId') -and
        $watcher.Contains('$alreadyWritten')
    ) 'Recovered sessions reuse the normal unique-ID history writer.'
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
}

$failed = @($results | Where-Object { -not $_.Passed })
$lines = @(
    '# Game Management feature test report',
    '',
    "Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')",
    '',
    "Overall result: **$(if ($failed.Count -eq 0) { 'PASS' } else { 'FAIL' })**",
    '',
    '| Test | Result | Detail |',
    '|---|---:|---|'
)
foreach ($result in $results) {
    $lines += "| $($result.Name) | $(if ($result.Passed) { 'PASS' } else { 'FAIL' }) | $($result.Detail -replace '\|', '\|') |"
}
$lines | Set-Content -LiteralPath $ReportPath -Encoding UTF8
[pscustomobject]@{ Overall = $(if ($failed.Count -eq 0) { 'PASS' } else { 'FAIL' }); Passed = $results.Count - $failed.Count; Failed = $failed.Count; Report = $ReportPath }


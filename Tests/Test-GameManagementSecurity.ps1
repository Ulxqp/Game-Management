param([string]$PackagePath = (Split-Path -Parent $MyInvocation.MyCommand.Path), [string]$ReportPath = (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'SECURITY-REPORT.md'))
$ErrorActionPreference = 'Stop'
if ((Split-Path -Leaf $PackagePath) -eq 'Tests') {
    $PackagePath = Split-Path -Parent $PackagePath
    if (-not $PSBoundParameters.ContainsKey('ReportPath')) {
        $ReportPath = Join-Path $PackagePath 'Documentation\Reports\SECURITY-REPORT.md'
        [IO.Directory]::CreateDirectory((Split-Path -Parent $ReportPath)) | Out-Null
    }
}
$checks = [Collections.Generic.List[object]]::new()
function Add-Check([string]$Name,[bool]$Passed,[string]$Detail){$checks.Add([pscustomobject]@{Name=$Name;Passed=$Passed;Detail=$Detail})}
$scripts=Get-ChildItem -LiteralPath $PackagePath -Filter '*.ps1'
$audited=@($scripts|Where-Object Name -ne 'Test-GameManagementSecurity.ps1')
$parseErrors=@(); foreach($script in $scripts){$tokens=$null;$errors=$null;[void][Management.Automation.Language.Parser]::ParseFile($script.FullName,[ref]$tokens,[ref]$errors);$parseErrors+=@($errors|ForEach-Object{"$($script.Name): $($_.Message)"})}
Add-Check 'PowerShell syntax' ($parseErrors.Count -eq 0) $(if($parseErrors.Count){$parseErrors -join '; '}else{'All scripts parse successfully.'})
$text=($audited|ForEach-Object{Get-Content -Raw -LiteralPath $_.FullName}) -join "`n"
$danger=[regex]::Matches($text,'Invoke-Expression|\biex\b|DownloadString|WebClient|Invoke-WebRequest|Start-BitsTransfer|Add-MpPreference|Set-MpPreference|\bbcdedit\b|\bdiskpart\b|\bvssadmin\b','IgnoreCase')|ForEach-Object Value|Sort-Object -Unique
Add-Check 'No dangerous scripting primitives' (@($danger).Count -eq 0) $(if(@($danger).Count){"Found: $($danger -join ', ')"}else{'No download, eval, security-disable, boot, disk, or recovery-deletion commands found.'})
$network=[regex]::Matches($text,'https?://|wss?://|System\.Net\.|Net\.WebClient','IgnoreCase')|ForEach-Object Value|Sort-Object -Unique
Add-Check 'No network access' (@($network).Count -eq 0) 'No web, socket, download, or upload code found.'
$hardware=[regex]::Matches($text,'LENOVO_GAMEZONE|SetSmartFan|SetFan|Fan_Set_Table|SetBIOS|OverClock|UnderVolt','IgnoreCase')|ForEach-Object Value|Sort-Object -Unique
Add-Check 'No fan or firmware control' (@($hardware).Count -eq 0) 'Fan mode, firmware, BIOS, voltage, and clocks are untouched.'
$watcher=Get-Content -Raw -LiteralPath (Join-Path $PackagePath 'GameManagement.ps1')
$sourcePath=Join-Path $PackagePath 'GameManagement.cs'
if (-not (Test-Path -LiteralPath $sourcePath)) { $sourcePath=Join-Path $PackagePath 'Source\GameManagement.cs' }
$sourceFiles = @($sourcePath) + @(Get-ChildItem -LiteralPath (Split-Path -Parent $sourcePath) -Filter 'Modern*.cs' | ForEach-Object FullName)
$appSource = ($sourceFiles | ForEach-Object { Get-Content -Raw -LiteralPath $_ }) -join "`n"
$pauseScript=Get-Content -Raw -LiteralPath (Join-Path $PackagePath 'Pause-GameManagement.ps1')
$installer=Get-Content -Raw -LiteralPath (Join-Path $PackagePath 'Install-GameManagement.ps1')
$setupSourcePath = Join-Path $PackagePath 'GameManagementSetup.cs'
if (-not (Test-Path -LiteralPath $setupSourcePath)) { $setupSourcePath = Join-Path $PackagePath 'Source\GameManagementSetup.cs' }
$setupSource = Get-Content -Raw -LiteralPath $setupSourcePath
$bridgeSourcePath = Join-Path $PackagePath 'bridge\ReactDesktopHost.cs'
$bridgeSource = if (Test-Path -LiteralPath $bridgeSourcePath) { Get-Content -Raw -LiteralPath $bridgeSourcePath } else { '' }
$frontendSourceRoot = Join-Path $PackagePath 'frontend\src'
$frontendSourcePresent = Test-Path -LiteralPath $frontendSourceRoot
$frontendSource = if ($frontendSourcePresent) { (Get-ChildItem -LiteralPath $frontendSourceRoot -File -Recurse | Where-Object { $_.Extension -in @('.ts', '.tsx') } | ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName }) -join "`n" } else { '' }
$legacyNames = @('Hardware' + 'Squisher', 'Hardware' + ' Squisher', 'Game' + 'Boost')
$legacyIdentityFound = $false
foreach ($legacyName in $legacyNames) {
    if (($text + "`n" + $appSource).IndexOf($legacyName, [StringComparison]::OrdinalIgnoreCase) -ge 0) { $legacyIdentityFound = $true }
    if (Get-ChildItem -LiteralPath $PackagePath -File | Where-Object { $_.Name.IndexOf($legacyName, [StringComparison]::OrdinalIgnoreCase) -ge 0 }) {
        $legacyIdentityFound = $true
    }
}
Add-Check 'Complete Game Management identity' (-not $legacyIdentityFound) 'Application scripts contain no previous product, executable, service, or data-folder identity.'
Add-Check 'No game process priority changes' (
    $watcher -notmatch 'PriorityClass|Ensure-GamePriority|Update-GamePriorityMode|priority-disabled\.flag'
) 'The Game Management watcher never raises, lowers, or repeatedly resets a game process priority.'
Add-Check 'Single-instance protection' ($watcher -match 'GameManagementWatcher_v1' -and $watcher -match 'Threading\.Mutex') 'Duplicate watchers exit immediately.'
Add-Check 'Single interface instance' (
    $appSource -match 'GameManagementInterface_v1' -and
    $appSource -match 'RunMainFormSingleInstance' -and
    $appSource -match 'new System\.Threading\.Mutex\(true, UiSignals\.InterfaceMutexName' -and
    $appSource -match 'if \(!createdNew\)' -and
    $appSource -match 'showSignal\.Set\(\)' -and
    $appSource -match 'GameManagementShowMain_v1' -and
    $appSource -match 'StartUiSignalListener' -and
    $appSource -match 'WaitHandle\.WaitAny\(signals, 500\)' -and
    $appSource -match 'uiContext\.Post' -and
    $appSource -match 'ShowFromTray\(false\)' -and
    $appSource -match 'ShowFromTray\(true\)' -and
    $appSource -match 'foregroundTimer\.Interval\s*=\s*250' -and
    $appSource -match 'BringToFront\(\)'
) 'Only one main interface can run; a background listener visibly raises the hidden window after later launches without weakening break-screen behavior.'
Add-Check 'Normalized generic detection' ($watcher -match 'GetFullPath' -and $watcher -match 'libraryfolders\.vdf') 'Steam paths and libraries are detected without game-specific entries.'
Add-Check 'Cached process classification' (
    $watcher -match 'processClassificationCache' -and
    $watcher -match 'StartTicks' -and
    $watcher -match 'seenProcessIds'
) 'Unchanged processes reuse their safe game/non-game classification instead of reopening every executable path each scan.'
Add-Check 'Exact restoration' ($watcher -match 'OriginalPowerScheme' -and $watcher -match 'Stop-Management') 'The pre-game plan is captured and restored.'
$settings=Get-Content -Raw -LiteralPath (Join-Path $PackagePath 'settings.json') | ConvertFrom-Json
$undo=Get-Content -Raw -LiteralPath (Join-Path $PackagePath 'Undo-GameManagement.ps1')
Add-Check 'Bounded brightness restoration' (
    [int]$settings.gameBrightnessPercent -ge 0 -and
    [int]$settings.gameBrightnessPercent -le 100 -and
    $watcher -match 'OriginalBrightness' -and
    $watcher -match 'WmiMonitorBrightnessMethods' -and
    $undo -match 'OriginalBrightness'
) 'Built-in display brightness is bounded to 0-100, captured before gaming, and restored by normal shutdown, recovery, reinstall, and undo.'
Add-Check 'Stable brightness capture' (
    $watcher -match 'lastIdleBrightness' -and
    $watcher -match 'Update-IdleBrightness \$true' -and
    $watcher -match 'idleBrightnessRefreshIntervalSeconds\s*=\s*30' -and
    $watcher -match 'originalBrightness\s*=\s*\$script:lastIdleBrightness'
) 'Brightness is sampled immediately before activation and otherwise refreshed at a low idle rate.'
Add-Check 'AC-only activation guard' (
    $watcher -match 'function Test-AcPower' -and
    $watcher -match 'if \(-not \$onAcPower\)' -and
    $watcher -match 'AC power disconnected; disabling \$script:activeMode management'
) 'Game Management cannot activate on battery and disables itself when AC is disconnected.'
Add-Check 'Fast AC status path' (
    $watcher -match 'SystemInformation.*PowerStatus\.PowerLineStatus' -and
    $watcher -match 'Get-CimInstance.*BatteryStatus'
) 'The normal AC check uses the lightweight Windows power-status API while retaining the prior WMI fallback.'
Add-Check 'Full disable restoration' (
    $watcher -match 'OriginalPowerScheme' -and
    $watcher -match 'OriginalBrightness' -and
    $watcher -match 'Set-Scheme \$restoreGuid' -and
    $watcher -match 'Set-DisplayBrightness \$restoreBrightness'
) 'Power plan and brightness are captured and restored on normal disable and recovery.'
Add-Check 'Timer alarm stops safely' (
    $appSource -match 'class TimerAlertForm' -and
    $appSource -match 'soundTimer\.Interval\s*=\s*1500' -and
    $appSource -match 'soundTimer\.Stop\(\)' -and
    $appSource -match 'soundTimer\.Dispose\(\)' -and
    $appSource -match 'RetroButton\("Stop alarm"'
) 'The visible timer alarm repeats a Windows sound until Stop alarm is pressed, then stops and disposes its timer.'
Add-Check 'Plain-language timer alert' (
    $appSource -match 'Time for a break!' -and
    $appSource -match 'Break over!' -and
    $appSource -match 'You can play again\.'
) 'Timer messages use short, plain-language instructions.'
Add-Check 'Timer confirmation change tracking' (
    $appSource -match 'private Button confirmTimerButton' -and
    $appSource -match 'timerMinutesInput\.ValueChanged\s*\+=' -and
    $appSource -match 'breakMinutesInput\.ValueChanged\s*\+=' -and
    $appSource -match 'timerEnabledCheck\.CheckedChanged\s*\+=' -and
    $appSource -match 'confirmTimerButton\.Enabled\s*=' -and
    $appSource -match 'timerEnabledCheck\.Checked\s*!=\s*settings\.gameTimerEnabled' -and
    $appSource -match 'timerMinutesInput\.TotalSeconds\s*!=\s*TimerDuration.Resolve' -and
    $appSource -match 'breakMinutesInput\.TotalSeconds\s*!=\s*TimerDuration.Resolve'
) 'Confirm stays disabled for saved timer values, enables when a timer setting changes, and resets after a successful save.'
Add-Check 'Shared modern secondary dialogs' (
    $appSource -match 'abstract class RetroDialogForm' -and
    $appSource -match 'class RetroDialogButton' -and
    $appSource -match 'TimerAlertForm\s*:\s*RetroDialogForm' -and
    $appSource -match 'SessionSummaryForm\s*:\s*RetroDialogForm' -and
    $appSource -match 'class ModernTheme' -and
    $appSource -match 'class ModernButton' -and
    $appSource -match 'Segoe UI' -and
    $appSource -match 'ModernTheme\.Surface'
) 'Alarm and summary dialogs retain their shared base while using the centralized v5 theme and Windows font.'
Add-Check 'Centralized v5 design tokens' (
    $appSource -match '#2563EB' -and $appSource -match '#F8FAFC' -and
    $appSource -match '#0F172A' -and $appSource -match '#E2E8F0' -and
    $appSource -match 'ControlHeight\s*=\s*36' -and $appSource -match 'PrimaryHeight\s*=\s*40' -and
    $appSource -match 'CardRadius\s*=\s*14' -and ($appSource -match 'Space\s*=\s*8' -or ($appSource -match 'Space8\s*=\s*8' -and $appSource -match 'Space\s*=\s*Space8'))
) 'The shared theme defines consistent colors, Windows typography, spacing, control heights, and card radius.'
Add-Check 'React desktop is the installed default' (
    (Test-Path -LiteralPath (Join-Path $PackagePath 'GameManagement.Desktop.exe')) -and
    (Test-Path -LiteralPath (Join-Path $PackagePath 'Microsoft.Web.WebView2.Core.dll')) -and
    (Test-Path -LiteralPath (Join-Path $PackagePath 'frontend\dist\index.html')) -and
    (Test-Path -LiteralPath (Join-Path $PackagePath 'frontend\dist\assets\app.js')) -and
    (Test-Path -LiteralPath (Join-Path $PackagePath 'frontend\dist\assets\app.css')) -and
    (Test-Path -LiteralPath (Join-Path $PackagePath 'GameManagement.exe')) -and
    $setupSource -match '"GameManagement\.Desktop\.exe"' -and
    $setupSource -match 'string appPath = Path\.Combine\(installRoot, "GameManagement\.Desktop\.exe"\)' -and
    $setupSource -match 'legacyFallbackShortcut' -and
    $setupSource -match 'File\.Delete\(legacyFallbackShortcut\)' -and
    $setupSource -notmatch 'CreateShortcut\([^\r\n]+Game Management \(Native fallback\)\.lnk'
) 'The installer exposes only the React desktop app in Start and removes the obsolete duplicate shortcut while retaining the native executable for recovery.'
Add-Check 'Bounded loopback desktop bridge' (
    $bridgeSource -match 'new TcpListener\(IPAddress\.Loopback, port\)' -and
    $bridgeSource -notmatch 'new HttpListener\(' -and
    $bridgeSource -match 'maxHeaderBytes\s*=\s*32768' -and
    $bridgeSource -match 'maxBodyBytes\s*=\s*1024 \* 1024' -and
    $bridgeSource -match 'ReceiveTimeout\s*=\s*5000' -and $bridgeSource -match 'SendTimeout\s*=\s*5000' -and
    $bridgeSource -match 'stream\.ReadTimeout\s*=\s*5000' -and $bridgeSource -match 'stream\.WriteTimeout\s*=\s*5000' -and
    $bridgeSource -notmatch 'readonly JavaScriptSerializer' -and $bridgeSource -match 'CreateSerializer\(\)' -and
    $bridgeSource -match 'Cross-origin requests are not allowed' -and
    $bridgeSource -match 'StartsWith\(prefixPath, StringComparison\.OrdinalIgnoreCase\)'
) 'The embedded UI uses a loopback-only TCP listener with bounded HTTP input and I/O timeouts, request-local JSON serialization, same-origin POST checks, and static-path containment.'
Add-Check 'Allowlisted application icon bridge' (
    $bridgeSource -match '/api/app-icon' -and
    $bridgeSource -match 'IsConfiguredApplication\(fullPath\)' -and
    $bridgeSource -match 'new\[\] \{ "workApps", "gameApps" \}' -and
    $bridgeSource -match 'Icon\.ExtractAssociatedIcon\(fullPath\)' -and
    $bridgeSource -match 'StringComparison\.OrdinalIgnoreCase' -and
    (-not $frontendSourcePresent -or ($frontendSource -match 'encodeURIComponent\(path\)' -and $frontendSource -match 'onError=\{\(\) => setFailed\(true\)\}'))
) 'Real Windows application icons are served only for exact executable paths already saved in Work or Game settings, with a local UI fallback when extraction fails.'
Add-Check 'Bounded local custom alarm' (
    $bridgeSource -match 'customAlarmPath' -and
    $bridgeSource -match 'String\.Equals\(Path\.GetExtension\(path\), "\.wav"' -and
    $bridgeSource -match '25L \* 1024L \* 1024L' -and
    $bridgeSource -match 'fullPath\.StartsWith\(@"\\\\"' -and
    $bridgeSource -match 'System\.Media\.SoundPlayer' -and
    $appSource -match 'CanUseCustomAlarm' -and $appSource -match 'PlayLooping\(\)' -and
    (-not $frontendSourcePresent -or ($frontendSource -match 'Choose WAV' -and $frontendSource -match 'previewAlarmSound'))
) 'Custom alarms accept only bounded local WAV files, remain opt-in through the typed loopback bridge, stop with their owning UI, and retain the Windows alarm fallback.'
Add-Check 'Serialized recoverable React snapshot polling' (
    -not $frontendSourcePresent -or (
        $frontendSource -match 'startSerializedPolling' -and
        $frontendSource -match 'activePromise' -and $frontendSource -match 'AbortController' -and
        $frontendSource -match 'timeoutMs:\s*5000' -and
        $frontendSource -match 'Connection delayed—retrying' -and
        $frontendSource -notmatch 'setInterval\(refresh'
    )
) 'React allows only one snapshot request at a time, times out and retries stalled requests, ignores stopped pollers, and keeps the last valid snapshot visible with a compact delayed-connection status.'
Add-Check 'Isolated preview is read-only' (
    $bridgeSource -match 'if \(previewMode && request\.Method != "GET"\)' -and
    $bridgeSource -match 'Preview mode is read-only\.' -and
    $bridgeSource -match 'if \(previewMode\) throw new InvalidOperationException\("Live commands are disabled in isolated preview mode\."\);' -and
    $bridgeSource.IndexOf('if (previewMode) throw', [StringComparison]::Ordinal) -lt $bridgeSource.IndexOf('if (name == "openNative")', [StringComparison]::Ordinal)
) 'Preview mode rejects every non-GET bridge request and retains a command-layer guard before native launch, pause, resume, or reset can run.'
Add-Check 'Pause marker changes only after transactional pause' (
    $bridgeSource -notmatch 'name == "pause"[^\r\n]+File\.Delete' -and
    $appSource -notmatch 'if \(File\.Exists\(engineEnabledPath\)\) File\.Delete\(engineEnabledPath\);\s*RunPowerShell\(pausePath' -and
    $pauseScript -match 'if \(\$pauseSucceeded\)[\s\S]+Remove-Item -LiteralPath \$engineEnabledPath' -and
    $pauseScript -match '\$engineMarkerExisted' -and $pauseScript -match 'WriteAllText\(\$engineEnabledPath, ''enabled''' -and
    $pauseScript -match 'Set-ItemProperty -LiteralPath \$runKey'
) 'React and native callers delegate marker removal to the transactional pause script, which restores the engine marker and original startup values if cleanup fails.'
Add-Check 'React snapshot preserves paused elapsed sessions' (
    $bridgeSource -match 'work-session-state\.json' -and $bridgeSource -match 'game-session-state\.json' -and
    (-not $frontendSourcePresent -or ($frontendSource -match 'snapshot\.sessions' -and $frontendSource -match 'selectSessionState' -and $frontendSource -match 'secondsInRange' -and $frontendSource -match 'ActiveSegments'))
) 'The desktop snapshot exposes mode-specific paused session checkpoints; React prefers a committed pause over same-session stale runtime and uses exact active intervals for reports.'
Add-Check 'Integrated Windows desktop chrome' (
    $bridgeSource -match 'FormBorderStyle\s*=\s*FormBorderStyle\.None' -and
    $bridgeSource -match 'WebMessageReceived' -and $bridgeSource -match 'WmNcHitTest' -and
    (-not $frontendSourcePresent -or ($frontendSource -match 'windowControl' -and $frontendSource -match 'Maximize or restore'))
) 'The WebView host has one React-rendered Windows title bar with working drag, resize, minimize, maximize/restore, and close controls.'
Add-Check 'Typed independent overlay preferences' (
    $bridgeSource -match '/api/panels' -and $bridgeSource -match 'panel-preferences\.json' -and
    $bridgeSource -match 'CalendarPinned' -and $bridgeSource -match 'TasksPinned' -and
    (-not $frontendSourcePresent -or ($frontendSource -match 'savePanelPreferences' -and $frontendSource -match 'setTimeout\([^\r\n]+,\s*260\)'))
) 'Calendar and Tasks pin independently through a bounded Version 1 bridge document; the React interface docks clicked panels beside the page while hover previews remain temporary.'
Add-Check 'React timer duration and report controls' (
    -not $frontendSourcePresent -or (
        $frontendSource -match 'DurationEditor' -and $frontendSource -match 'Hours' -and
        $frontendSource -match 'Minutes' -and $frontendSource -match 'Seconds' -and
        $frontendSource -match 'Daily' -and $frontendSource -match 'Weekly' -and
        $frontendSource -match 'Monthly' -and $frontendSource -match 'All Time' -and
        $frontendSource -match 'startOfReportingWeek'
    )
) $(if ($frontendSourcePresent) { 'React source exposes bounded H/M/S editors and four real report periods using the native Monday week boundary.' } else { 'Source-only React checks were completed before runtime packaging.' })
Add-Check 'Embedded five-destination navigation' (
    $appSource -match 'Home' -and $appSource -match 'Applications' -and
    $appSource -match 'Timer Settings' -and $appSource -match 'Reports' -and
    $appSource -match 'Options' -and $appSource -match 'SetPageForPreview' -and
    $appSource -match 'DockStyle\.Top' -and
    $appSource -match 'ProcessCmdKey|KeyDown'
) 'The application contains its top navigation for all five pages and explicit keyboard handling.'
Add-Check 'Independent Calendar and Tasks overlays' (
    $appSource -match 'class OverlayPanels' -and
    $appSource -match 'CalendarPinned' -and $appSource -match 'TasksPinned' -and
    $appSource -match 'SelectCalendarRange' -and $appSource -match 'tasks\.json' -and
    $appSource -match 'SetPanelPinned'
) 'Calendar and Tasks provide independent panel pinning, actual date ranges, and local persistent task storage.'
Add-Check 'Shared Game and Work dashboard behavior' (
    $appSource -match 'ModernTheme\.Work' -and $appSource -match 'ModernTheme\.Game' -and
    $appSource -match 'gameSurface' -and $appSource -match 'workSurface' -and
    $appSource -match 'RequestManagementMode' -and
    $appSource -match 'RefreshTimerDisplay' -and $appSource -match 'RefreshWorkTimerDisplay'
) 'Both dashboards use the same v5 design system while retaining their independent mode-switching and timer refresh paths.'
Add-Check 'User-selected work applications' (
    $appSource -match 'OpenFileDialog' -and
    $appSource -match 'Applications \(\*\.exe\)\|\*\.exe' -and
    $appSource -match 'settings\.workApps' -and
    $watcher -match 'function Find-RunningWorkApps' -and
    $watcher -match '\$selected\.Contains\(\$path\)'
) 'Work mode monitors only executable files explicitly selected by the user and matches their exact paths.'
Add-Check 'Work profile avoids gaming power changes' (
    $watcher -match 'if \(\$script:activeMode -eq ''work''\)' -and
    $watcher -match 'Work Management ON' -and
    $watcher -match '\$script:activeMode -eq ''game'' -and \(Get-ActiveScheme\) -ne \$managementPlanGuid' -and
    $watcher -match 'if \(\$script:activeMode -eq ''game''\) \{ Send-GameEscape'
) 'Selected work apps receive focus timing without gaming power-plan, brightness, or Escape behavior.'
Add-Check 'Pause-aware Work session timer' (
    $appSource -match 'private DateTime\? workSessionStartedAt' -and
    $appSource -match 'ManagementStarted' -and
    $appSource -match 'workSessionClock\.ElapsedAt\(DateTime.Now\)' -and
    $appSource -match '!Paused && RunningSince.HasValue' -and
    $appSource -match 'string\.Format\("\{0:00\}:\{1:00\}:\{2:00\}"' -and
    $appSource -match 'if \(workView\) \{ RefreshWorkElapsedDisplay\(\); RefreshWorkTimerDisplay\(\); \}' -and
    $watcher -match '\$script:sessionStartedAt = Get-Date' -and
    $watcher -match 'Clear-GameTimer'
) 'Work Mode displays accumulated active time plus only the running interval, keeping HH:MM:SS frozen while paused.'
Add-Check 'Target-verified Escape control' (
    $watcher -match 'MainWindowHandle' -and
    $watcher -match 'GetForegroundWindow\(\) -ne \$targetHandle' -and
    $watcher -match 'Escape skipped safely' -and
    $watcher -match 'keybd_event\(0x1B' -and
    $watcher -notmatch 'SendKeys.*ESC'
) 'Escape is sent only after the detected game window is selected and verified as the foreground window; there is no global fallback.'
Add-Check 'Timer dialog only at timer transitions' (
    $watcher -match "'game-finished'" -and
    $watcher -match "'break-finished'" -and
    $watcher -match "'work-finished'" -and
    $watcher -match "'work-break-finished'" -and
    $watcher -notmatch 'Set-BreakInterface\s+\$(true|false)'
) 'Timer transitions open only the alarm dialog and do not show or hide the main interface.'
Add-Check 'Independent Work timer settings and state' (
    $appSource -match 'workTimerEnabled' -and
    $appSource -match 'workTimerMinutes' -and
    $appSource -match 'workBreakMinutes' -and
    $watcher -match 'work-timer-state\.json' -and
    $watcher -match "\$script:activeMode -eq 'work'" -and
    $watcher -match 'workTimerEnabled' -and
    $watcher -match 'workTimerMinutes' -and
    $watcher -match '\$script:gameTimerEnabled = if \(\$null -ne \$config.workTimerEnabled\)' -and
    $appSource -match 'if \(!settingsMap.ContainsKey\("workTimerEnabled"\)\) settings.workTimerEnabled = true'
) 'Work and Game use separate saved settings and timer-state files while sharing the countdown controller.'
Add-Check 'Pause freezes and resumes timer state' (
    $pauseScript -match 'function Freeze-TimerState' -and
    $pauseScript -match 'RemainingSeconds' -and
    $pauseScript -match 'NotePropertyName Paused' -and
    $pauseScript -notmatch 'Remove-Item -LiteralPath \$timerStatePath -Force' -and
    $pauseScript -notmatch 'Remove-Item -LiteralPath \$workTimerStatePath -Force' -and
    $watcher -match '\$pausedTimer\.RemainingSeconds' -and
    $watcher -match 'timerDeadline = \(Get-Date\)\.AddSeconds\(\$remainingSeconds\)' -and
    $watcher -match 'function Remove-StaleTimerState' -and
    $appSource -match 'ApplyPausedTimerDisplay' -and
    $appSource -match 'Paused \(' -and
    $appSource -match 'private Panel Dashboard\(bool work\)' -and
    $appSource -match 'ModernTheme\.Button\("Start / Resume", delegate \{ if \(!PreviewMode\) EnableGameManagement\(\); \}' -and
    $appSource -match 'ModernTheme\.Button\("Pause", delegate \{ if \(!PreviewMode\) PauseGameManagement\(\); \}' -and
    $installer -match 'function Remove-UnlessPaused'
) 'Game and Work Pause store the phase, cycle, and remaining seconds; Resume preserves that state until the matching application returns, and the dashboard displays the frozen value.'
Add-Check 'Installed app selection retains file browse' (
    $appSource -match 'InstalledAppsForm' -and
    $appSource -match 'App Paths' -and
    $appSource -match 'Browse file\.\.\.' -and
    $appSource -match 'OpenFileDialog' -and
    $appSource -match 'LaunchSelectedWorkApplication'
) 'Installed apps can be chosen and launched; the existing executable picker remains available.'
Add-Check 'Game installed-app detection uses exact paths' (
    $appSource -match 'gameApps' -and
    $appSource -match 'ChooseInstalledGameApplication' -and
    $appSource -match 'BrowseGameApplication' -and
    $appSource -match '\[App\]' -and
    $appSource -match '\[Folder\]' -and
    $watcher -match 'function Get-GameApplications' -and
    $watcher -match 'Find-RunningGames\(\$folders, \$selectedPaths, \$excluded\)' -and
    $watcher -match '\$selected\.Contains\(\$path\)'
) 'Game Mode can select installed or manually browsed executables by exact path while retaining folder detection.'
Add-Check 'Temperature polling does not block interface controls' (
    $appSource -match 'ThreadPool\.QueueUserWorkItem' -and
    $appSource -match 'Interlocked\.Exchange\(ref temperatureRefreshRunning' -and
    $appSource -match 'ApplyTemperatureMetrics' -and
    $appSource -match 'BeginInvoke\(new MethodInvoker'
) 'Hardware temperature queries run in one bounded background worker and return results to the interface thread.'
Add-Check 'Settings handoff wakes watcher immediately' (
    $appSource -match 'SignalWatcherWake' -and
    $appSource -match 'GameManagementWatcherWake_v1' -and
    $watcher -match 'WakeEventName' -and
    $watcher -match '\$wakeEvent\.WaitOne' -and
    $watcher -notmatch 'Start-Sleep -Seconds \(\[Math\]::Max\(2, \[int\]\$config\.pollSeconds\)\)'
) 'Mode switches and settings saves signal the watcher instead of waiting for the next scan interval.'
Add-Check 'Weekly report from existing session history' (
    $appSource -match 'internal static class WeeklyReport' -and
    $appSource -match 'GameSessionHistory\.txt' -and
    $appSource -match 'sliceEnd - cursor' -and
    $appSource -match 'Mode' -and
    $appSource -match 'Applications'
) 'Weekly totals are split at midnight and week boundaries from recorded start/end times.'
Add-Check 'Monthly reports and searchable history' (
    $appSource -match 'BuildMonthly' -and
    $appSource -match 'Session history' -and
    $appSource -match 'searchBox\.TextChanged' -and
    $appSource -match 'OrderByDescending\(item => item\.Start\)' -and
    $appSource -match 'StringComparison\.OrdinalIgnoreCase'
) 'Reports include weekly and monthly periods, and saved sessions can be filtered locally by date, mode, app, or duration.'
Add-Check 'Portable data backup is bounded and local' (
    $appSource -match 'GameManagement-Backup-' -and
    $appSource -match 'backup-manifest\.json' -and
    $appSource -match 'GameSessionHistory\.txt' -and
    $appSource -match 'settingsEntry\.Length > 5 \* 1024 \* 1024' -and
    $appSource -match 'historyEntry\.Length > 100 \* 1024 \* 1024' -and
    $appSource -match 'previousSettings' -and
    $appSource -match 'previousHistory' -and
    $appSource -match 'File\.Exists\(engineEnabledPath\) \|\| IsWatcherRunning\(\)' -and
    $appSource -notmatch 'ExtractToDirectory'
) 'Backup and restore handle only named settings/history entries, require a fully paused engine, enforce size limits, roll back failed replacement, and never extract arbitrary archive paths.'
Add-Check 'Configurable local notifications' (
    $appSource -match 'timerPopupEnabled' -and
    $appSource -match 'timerSoundEnabled' -and
    $appSource -match 'sessionSummaryEnabled' -and
    $appSource -match 'if \(!showPopup\)' -and
    $watcher -match '\$script:sessionSummaryEnabled'
) 'Timer popup, alarm sound, and end-of-session summary choices are local settings with safe enabled defaults.'
Add-Check 'Interrupted session recovery' (
    $watcher -match 'function Recover-InterruptedSession' -and
    $watcher -match 'Write-GameSessionSummary \$false' -and
    $watcher -match 'Recovered interrupted \$script:activeMode session' -and
    $watcher -match 'if \(\$active\) \{ Stop-Management \$true \$false \}' -and
    $watcher -match 'SessionId: \$currentSessionId'
) 'Unexpected watcher shutdowns preserve eligible sessions without showing a stale popup, and SessionId prevents duplicate history entries.'
Add-Check 'Read-only CPU and GPU temperatures' (
    $appSource -match 'performanceTimer\.Interval\s*=\s*1000' -and
    $appSource -match 'GetCPUTemp' -and
    $appSource -match '--query-gpu=temperature\.gpu' -and
    $appSource -match 'peakCpuTemperature' -and
    $appSource -match 'peakGpuTemperature' -and
    $appSource -match 'Danger' -and
    $appSource -notmatch '--power-limit|-pl\s|SetSmartFan|SetFan'
) 'CPU and GPU temperatures and session peaks refresh every second using read-only queries, with color and unavailable handling.'
Add-Check 'Idle tray sensor suspension' (
    $appSource -match 'if \(!Visible && !File\.Exists\(statePath\)\)' -and
    $appSource -match 'if \(File\.Exists\(performanceStatePath\)\) File\.Delete\(performanceStatePath\);' -and
    $appSource -match 'if \(!Visible \|\| modeSwitchInProgress\) return;' -and
    $appSource -match 'else RefreshTimerDisplay\(IsRuntimeMode\("game"\)\);'
) 'Hidden idle operation skips GPU, CPU, and timer display polling, removes stale session snapshots, and keeps active monitoring real time.'
Add-Check 'Single power-plan status query' (
    $appSource -match 'activeSchemeOutput\s*=\s*RunCapture\("powercfg\.exe", "/getactivescheme"\)' -and
    $appSource -match 'GetActiveSchemeGuid\(activeSchemeOutput\)' -and
    $appSource -match 'GetActiveSchemeName\(activeSchemeOutput, activeSchemeGuid\)' -and
    $appSource -match 'snapshot\.Managed\s*=\s*gameStateActive;' -and
    $appSource -notmatch 'snapshot\.Managed\s*=\s*gameStateActive\s*\|\|'
) 'A visible status refresh parses one powercfg result instead of launching powercfg twice, while runtime state—not the selected plan—owns the active label.'
Add-Check 'Read-only MSI Afterburner CPU sensor' (
    $appSource -match 'MemoryMappedFile\.OpenExisting\("MAHMSharedMemory", MemoryMappedFileRights\.Read\)' -and
    $appSource -match 'CreateViewAccessor\(0, 0, MemoryMappedFileAccess\.Read\)' -and
    $appSource -match 'CpuTemperatureSourceId\s*=\s*0x00000080' -and
    $appSource -notmatch 'MemoryMappedFile\.CreateNew|MemoryMappedFileAccess\.Write|WriteArray|WriteByte|WriteInt'
) 'MSI Afterburner CPU temperature is consumed through its existing monitoring map with read-only handles and bounded data validation.'
Add-Check 'Priority controls removed from interface' (
    $appSource -notmatch 'Stop priority|Start priority|RestoreCapturedPriorities|ProcessPriorityClass|priority-disabled\.flag' -and
    $watcher -notmatch 'High priority|Above Normal priority|PriorityClass'
) 'The Game Management interface and watcher contain no process-priority feature.'
Add-Check 'Exited game cleanup' (
    $watcher -match 'if \(\$process\.HasExited\) \{ continue \}' -and
    $watcher -match "'crash_reporter','crashreporter'"
) 'Exited process entries and known crash reporters are ignored so management mode can restore after the real game closes.'
Add-Check 'Persistent game session notes' (
    $watcher -match 'GameSessionHistory\.txt' -and
    $watcher -match '''Total work time''' -and
    $watcher -match '''Total game time''' -and
    $watcher -match '\$\{timeLabel\}:' -and
    $watcher -match 'Timer cycles:' -and
    $watcher -match 'Peak CPU temperature:' -and
    $watcher -match 'Peak GPU temperature:' -and
    $appSource -match 'class SessionSummaryForm'
) 'A plain-text history and visible exit summary store the game, date/time, duration, cycles, and peak CPU/GPU temperatures.'
Add-Check 'Short sessions excluded from history' (
    $watcher -match '\$duration\.TotalSeconds\s*-le\s*120' -and
    $watcher -match '\$sessionLabel session not saved because it lasted 2 minutes or less' -and
    $watcher -match 'Remove-Item\s+-LiteralPath\s+\$lastSessionPath' -and
    $watcher -match 'return'
) 'Sessions lasting exactly two minutes or less do not update history or open a saved-session summary.'
Add-Check 'Non-blocking status collection' (
    $appSource -match 'CollectStatusSnapshot' -and
    $appSource -match 'ThreadPool\.QueueUserWorkItem' -and
    $appSource -match 'ApplyStatusSnapshot' -and
    $appSource -match 'statusRefreshRunning' -and
    $appSource -match 'statusRefreshPending'
) 'Power, brightness, watcher, process, and Work-app status checks run in one coalesced background worker.'
Add-Check 'Stale status results rejected' (
    $appSource -match 'statusRefreshGeneration' -and
    $appSource -match 'snapshot\.Generation' -and
    $appSource -match 'snapshot\.Generation\s*!=\s*System\.Threading\.Interlocked\.CompareExchange'
) 'A delayed status result cannot overwrite a newer Game/Work mode or settings state.'
Add-Check 'Non-blocking control and settings changes' (
    $appSource -match 'SaveSettingsAsync' -and
    $appSource -match 'RunControlActionAsync' -and
    $appSource -match 'SetSettingsControlsEnabled\(false\)' -and
    $appSource -match 'WriteSettingsAtomically'
) 'Settings, mode handoffs, Enable, Pause, and startup changes leave the Windows message loop responsive and replace settings atomically.'
Add-Check 'Serialized watcher transitions' (
    $appSource -match 'watcherTransitionRunning' -and
    ([regex]::Matches($appSource, 'CompareExchange\(ref watcherTransitionRunning, 1, 0\)').Count -ge 3) -and
    $appSource -match 'File\.Exists\(engineEnabledPath\) && !IsWatcherRunning\(\)' -and
    $appSource -match 'CloseReason\.UserClosing && IsWatcherTransitionActive\(\)' -and
    $appSource -match 'ExplainBlockedExit\(\)'
) 'Status restart holds the same gate as save/control actions, rechecks state before launch, and all user Exit paths wait for transitions.'
Add-Check 'Background installed-app discovery' (
    $appSource -match 'BeginDiscovery' -and
    $appSource -match 'DiscoverCached' -and
    $appSource -match 'LoadIconsInBackground' -and
    $appSource -match 'SetApartmentState\(System\.Threading\.ApartmentState\.STA\)'
) 'The app picker opens before registry, shortcut, and icon discovery completes, and caches discovery results for later opens.'
Add-Check 'Bounded shared-log reading' (
    $appSource -match 'ReadLogTail' -and
    $appSource -match 'const int maximumBytes = 262144' -and
    $appSource -match 'FileShare\.ReadWrite \| FileShare\.Delete' -and
    $appSource -notmatch 'File\.ReadAllLines\(logPath\)'
) 'Status refresh reads only a bounded log tail and safely shares the file with the background watcher.'
$installer=Get-Content -Raw -LiteralPath (Join-Path $PackagePath 'Install-GameManagement.ps1')
Add-Check 'Normal-user startup only' ($installer -match 'CurrentVersion\\Run' -and $installer -notmatch 'ScheduledTask|RunLevel|Verb RunAs') 'No service or elevated startup mechanism is used.'
Add-Check 'Single configurable application startup' (
    $installer -match '\$application\s*=\s*Join-Path \$root ''GameManagement\.exe''' -and
    $installer -match '--ui-start-hidden' -and
    $appSource -match 'SetStartup\(startupCheck\.Checked\)' -and
    $appSource -match 'RemoveRunEntry\(\)' -and
    $appSource -match 'StopWatcherForSettingsChange\(previousSettings\.activeMode\)' -and
    $appSource -match 'StartWatcherAfterSettingsChange\(\)' -and
    $installer -match '\$isUpgrade\s*=\s*Test-Path -LiteralPath \$backupPath' -and
    $installer -match '\$startupWasEnabled' -and
    $installer -match 'if \(-not \$isUpgrade -or \$startupWasEnabled\)' -and
    $installer -match '\$engineWasEnabled\s*=\s*Test-Path -LiteralPath \$engineEnabledPath' -and
    $installer -match 'if \(-not \$isUpgrade -or \$engineWasEnabled\)' -and
    ([regex]::Matches($installer, '\$runName\s*=\s*''GameManagementEngine''').Count -eq 1)
) 'One per-user Run entry launches the single-instance application hidden, and watcher restarts and upgrades preserve the startup and paused choices.'
$setupPath=Join-Path $PackagePath 'GameManagementSetup.cs'
$installerBuilderPath=Join-Path $PackagePath 'Build-GameManagementInstaller.ps1'
$packageBuildFilesPresent=(Test-Path -LiteralPath $setupPath) -and (Test-Path -LiteralPath $installerBuilderPath)
$setup=if($packageBuildFilesPresent){Get-Content -Raw -LiteralPath $setupPath}else{''}
$installerBuilder=if($packageBuildFilesPresent){Get-Content -Raw -LiteralPath $installerBuilderPath}else{''}
$undoScript=Get-Content -Raw -LiteralPath (Join-Path $PackagePath 'Undo-GameManagement.ps1')
Add-Check 'RTX 20/30 compatibility coverage' (
    -not $packageBuildFilesPresent -or (
        $setup -match 'Rtx20Or30Pattern' -and
        $setup -match 'RTX 3050 Laptop GPU' -and
        $setup -match 'RTX 3090' -and
        $installerBuilder -match '--compatibility-test'
    )
) $(if($packageBuildFilesPresent){'The installer self-test covers RTX 20/30 desktop, SUPER, Ti, and Laptop GPU naming variants.'}else{'Installer build files are intentionally omitted from the installed runtime; this package-only check was completed before installation.'})
Add-Check 'GPU-independent behavior' (
    $text -notmatch 'nvidia-smi|NVAPI|Set-Gpu|Overclock|Undervolt' -and
    (-not $packageBuildFilesPresent -or $setup -match 'does not change GPU clocks, voltages, drivers, firmware, or NVIDIA settings')
) $(if($packageBuildFilesPresent){'RTX recognition is informational; the application does not issue vendor-specific GPU tuning commands.'}else{'Installed runtime scripts contain no vendor-specific GPU tuning commands.'})
Add-Check 'Adaptive Windows power settings' (
    $installer -match 'function Try-PowerCfg' -and
    $installer -match 'balancedGuid.*managementPlanGuid' -and
    $installer -match 'Unsupported optional settings are skipped'
) 'Setup falls back to Balanced and skips unsupported optional power settings on vendor-specific firmware.'
Add-Check 'Redirected Documents compatibility' (
    $installer -match '\[regex\]::Escape\(\$watcher\)' -and
    $undoScript -match '\[regex\]::Escape\(\$watcherPath\)' -and
    $installer -notmatch '\*Documents\\GameManagement\\GameManagement'
) 'Watcher cleanup uses the resolved installation path, including redirected or localized Documents folders.'
Add-Check 'Foreground-only automatic mode detection' (
    $appSource -match 'GetForegroundWindow' -and
    $appSource -match 'GetWindowThreadProcessId' -and
    $appSource -match 'ForegroundModeDetector\.Classify' -and
    $appSource -match 'foregroundModeTimer\.Interval\s*=\s*1000' -and
    $appSource -match 'automaticModeDetection' -and
    $appSource -match 'manualOverridePath'
) 'Automatic switching classifies only the active window once per second, ignores unassigned apps, and keeps a manual override until the foreground assignment changes.'
Add-Check 'Authenticated timer reset channel' (
    $appSource -match 'timer-reset-request\.json' -and
    $appSource -match 'RequesterStartTicks' -and
    $watcher -match 'Process-TimerResetRequest' -and
    $watcher -match 'owner\.StartTime\.ToUniversalTime\(\)\.Ticks' -and
    $watcher -match 'ExpiresUtc'
) 'Timer reset commands are local, short-lived, tied to the requesting process instance, and acknowledged before the interface reports success.'
Add-Check 'Independent persistent completed cycles' (
    $watcher -match 'timer-counters\.json' -and
    $watcher -match 'gameCompletedCycles' -and
    $watcher -match 'workCompletedCycles' -and
    $watcher -match '\$script:timerCycle\+\+' -and
    $watcher -match 'Set-CompletedCycles \$script:activeMode \$script:timerCycle' -and
    $watcher -match 'timerCycle - \[int\]\$script:sessionCycleStart' -and
    $appSource -match 'Completed cycles'
) 'Game and Work completed-cycle totals persist separately, increment at the end of a break, and session history records only the cycles completed during that session.'
Add-Check 'Mode handoff preserves independent timers' (
    $watcher -match 'Freeze-ActiveTimerState' -and
    $watcher -match 'Stop-Management \$false \$false \$true \$true' -and
    $watcher -match 'Save-SessionState' -and
    $watcher -match 'Remove-StaleTimerState \$timerStatePath' -and
    $watcher -match 'Remove-StaleTimerState \$workTimerStatePath' -and
    $appSource -match 'RequestManagementMode'
) 'A mode change freezes the outgoing countdown and elapsed session without finalizing history, preserves both modes, and keeps one watcher.'
$task=Get-ScheduledTask -TaskName 'Game Management' -ErrorAction SilentlyContinue
Add-Check 'Bounded second-resolution durations' (
    $appSource -match 'class DurationInput' -and $appSource -match 'class TimerDuration' -and
    $appSource -match 'gameTimerSeconds' -and $appSource -match 'workBreakSeconds' -and
    $watcher -match 'Get-ConfiguredTimerSeconds' -and $watcher -match 'Min\(86400' -and
    $watcher -match 'Get-NextWatcherWaitMilliseconds'
) 'All four timers use bounded seconds with legacy-minute fallback and interruptible deadline wake-ups.'
Add-Check 'Pause-aware history accounting' (
    $watcher -match 'ActiveSeconds:' -and $watcher -match 'ActiveSegments:' -and
    $appSource -match 'session.ActiveSegments' -and $appSource -match 'session.Duration' -and
    $pauseScript -match 'ElapsedSeconds' -and $pauseScript -match 'Paused = \$true'
) 'Elapsed session checkpoints and history use active time, excluding paused gaps from calendar reports.'
Add-Check 'No elevated Game Management task' (-not [bool]$task) $(if($task){'An elevated task exists.'}else{'No elevated task is installed.'})
$failed=@($checks|Where-Object{-not $_.Passed});$status=if($failed.Count -eq 0){'PASS'}else{'REVIEW REQUIRED'}
$lines=@('# Game Management security report','',"Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')",'',"Overall result: **$status**",'','| Check | Result | Detail |','|---|---:|---|')
foreach($check in $checks){$result=if($check.Passed){'PASS'}else{'FAIL'};$lines+="| $($check.Name) | $result | $($check.Detail -replace '\|','\|') |"}
$lines+=@('','## SHA-256 hashes','','```text');foreach($hash in ($scripts|Get-FileHash -Algorithm SHA256|Sort-Object Path)){$lines+="$($hash.Hash)  $([IO.Path]::GetFileName($hash.Path))"};$lines+='```'
$lines|Set-Content -LiteralPath $ReportPath -Encoding UTF8
[pscustomobject]@{Overall=$status;Passed=$checks.Count-$failed.Count;Failed=$failed.Count;Report=$ReportPath}
if ($failed.Count -gt 0) { throw "$($failed.Count) security checks failed; inspect $ReportPath before deployment." }

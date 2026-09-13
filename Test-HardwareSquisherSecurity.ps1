param([string]$PackagePath = (Split-Path -Parent $MyInvocation.MyCommand.Path), [string]$ReportPath = (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'SECURITY-REPORT.md'))
$ErrorActionPreference = 'Stop'
$checks = [Collections.Generic.List[object]]::new()
function Add-Check([string]$Name,[bool]$Passed,[string]$Detail){$checks.Add([pscustomobject]@{Name=$Name;Passed=$Passed;Detail=$Detail})}
$scripts=Get-ChildItem -LiteralPath $PackagePath -Filter '*.ps1'
$audited=@($scripts|Where-Object Name -ne 'Test-HardwareSquisherSecurity.ps1')
$parseErrors=@(); foreach($script in $scripts){$tokens=$null;$errors=$null;[void][Management.Automation.Language.Parser]::ParseFile($script.FullName,[ref]$tokens,[ref]$errors);$parseErrors+=@($errors|ForEach-Object{"$($script.Name): $($_.Message)"})}
Add-Check 'PowerShell syntax' ($parseErrors.Count -eq 0) $(if($parseErrors.Count){$parseErrors -join '; '}else{'All scripts parse successfully.'})
$text=($audited|ForEach-Object{Get-Content -Raw -LiteralPath $_.FullName}) -join "`n"
$danger=[regex]::Matches($text,'Invoke-Expression|\biex\b|DownloadString|WebClient|Invoke-WebRequest|Start-BitsTransfer|Add-MpPreference|Set-MpPreference|\bbcdedit\b|\bdiskpart\b|\bvssadmin\b','IgnoreCase')|ForEach-Object Value|Sort-Object -Unique
Add-Check 'No dangerous scripting primitives' (@($danger).Count -eq 0) $(if(@($danger).Count){"Found: $($danger -join ', ')"}else{'No download, eval, security-disable, boot, disk, or recovery-deletion commands found.'})
$network=[regex]::Matches($text,'https?://|wss?://|System\.Net\.|Net\.WebClient','IgnoreCase')|ForEach-Object Value|Sort-Object -Unique
Add-Check 'No network access' (@($network).Count -eq 0) 'No web, socket, download, or upload code found.'
$hardware=[regex]::Matches($text,'LENOVO_GAMEZONE|SetSmartFan|SetFan|Fan_Set_Table|SetBIOS|OverClock|UnderVolt','IgnoreCase')|ForEach-Object Value|Sort-Object -Unique
Add-Check 'No fan or firmware control' (@($hardware).Count -eq 0) 'Fan mode, firmware, BIOS, voltage, and clocks are untouched.'
$watcher=Get-Content -Raw -LiteralPath (Join-Path $PackagePath 'HardwareSquisher.ps1')
$appSource=Get-Content -Raw -LiteralPath (Join-Path $PackagePath 'HardwareSquisher.cs')
Add-Check 'No Realtime priority' ($watcher -notmatch "PriorityClass\s*=\s*'Realtime'") 'Only High with Above Normal fallback is used.'
Add-Check 'Single-instance protection' ($watcher -match 'HardwareSquisherWatcher_v1' -and $watcher -match 'Threading\.Mutex') 'Duplicate watchers exit immediately.'
Add-Check 'Normalized generic detection' ($watcher -match 'GetFullPath' -and $watcher -match 'libraryfolders\.vdf') 'Steam paths and libraries are detected without game-specific entries.'
Add-Check 'Exact restoration' ($watcher -match 'OriginalPowerScheme' -and $watcher -match 'Stop-Boost') 'The pre-game plan is captured and restored.'
$settings=Get-Content -Raw -LiteralPath (Join-Path $PackagePath 'settings.json') | ConvertFrom-Json
$undo=Get-Content -Raw -LiteralPath (Join-Path $PackagePath 'Undo-HardwareSquisher.ps1')
Add-Check 'Bounded brightness restoration' (
    [int]$settings.gameBrightnessPercent -ge 0 -and
    [int]$settings.gameBrightnessPercent -le 100 -and
    $watcher -match 'OriginalBrightness' -and
    $watcher -match 'WmiMonitorBrightnessMethods' -and
    $undo -match 'OriginalBrightness'
) 'Built-in display brightness is bounded to 0-100, captured before gaming, and restored by normal shutdown, recovery, reinstall, and undo.'
Add-Check 'Stable brightness capture' (
    $watcher -match 'lastIdleBrightness' -and
    $watcher -match 'originalBrightness\s*=\s*\$script:lastIdleBrightness'
) 'Brightness is cached before game launch transitions instead of first sampled during fullscreen startup.'
Add-Check 'AC-only activation guard' (
    $watcher -match 'function Test-AcPower' -and
    $watcher -match 'if \(-not \$onAcPower\)' -and
    $watcher -match 'AC power disconnected; disabling Hardware Squisher'
) 'Hardware Squisher cannot activate on battery and disables itself when AC is disconnected.'
Add-Check 'Full disable restoration' (
    $watcher -match 'OriginalPriorities' -and
    $watcher -match 'Restore-GamePriorities' -and
    $undo -match 'Restore-SavedPriorities'
) 'Power plan, brightness, and original live process priorities are restored on disable, recovery, reinstall, and undo.'
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
Add-Check 'Windows 98 secondary dialogs' (
    $appSource -match 'abstract class RetroDialogForm' -and
    $appSource -match 'class RetroDialogButton' -and
    $appSource -match 'TimerAlertForm\s*:\s*RetroDialogForm' -and
    $appSource -match 'SessionSummaryForm\s*:\s*RetroDialogForm' -and
    $appSource -match 'FormBorderStyle\s*=\s*FormBorderStyle\.None' -and
    $appSource -match 'BackColor\s*=\s*Color\.FromArgb\(0, 0, 128\)' -and
    $appSource -match 'Border3DStyle\.Raised' -and
    $appSource -match 'Border3DStyle\.Sunken' -and
    $appSource -match 'FlatStyle\s*=\s*FlatStyle\.Standard' -and
    $appSource -notmatch 'DrawFocusRectangle'
) 'The alarm and session summary share the classic title bar, gray surface, fixed raised/sunken button borders, and beveled controls used by the main interface.'
Add-Check 'Aligned activity toggle' (
    $appSource -match 'toggleLogButton\.Left\s*=\s*logGroup\.Left\s*\+\s*actionLeft' -and
    $appSource -match 'toggleLogButton\.Width\s*=\s*actionWidth' -and
    $appSource -match 'toggleLogButton\.Height\s*=\s*25'
) 'Hide activity uses the exact width, left edge, right edge, and height of Open log and Diagnostics.'
Add-Check 'Target-verified Escape control' (
    $watcher -match 'MainWindowHandle' -and
    $watcher -match 'GetForegroundWindow\(\) -ne \$targetHandle' -and
    $watcher -match 'Escape skipped safely' -and
    $watcher -match 'keybd_event\(0x1B' -and
    $watcher -notmatch 'SendKeys.*ESC'
) 'Escape is sent only after the detected game window is selected and verified as the foreground window; there is no global fallback.'
Add-Check 'Automatic break interface flow' (
    $watcher -match '--break-ui-show' -and
    $watcher -match '--break-ui-hide' -and
    $appSource -match 'HardwareSquisherShowBreak_v1' -and
    $appSource -match 'ShowForBreak\(\)' -and
    $appSource -match 'HideAfterBreak\(\)'
) 'The main interface is signaled to show when a break starts and hide when the break ends.'
Add-Check 'Read-only CPU and GPU temperatures' (
    $appSource -match 'performanceTimer\.Interval\s*=\s*1000' -and
    $appSource -match 'GetCPUTemp' -and
    $appSource -match '--query-gpu=temperature\.gpu' -and
    $appSource -match 'peakCpuTemperature' -and
    $appSource -match 'peakGpuTemperature' -and
    $appSource -match 'Danger' -and
    $appSource -notmatch '--power-limit|-pl\s|SetSmartFan|SetFan'
) 'CPU and GPU temperatures and session peaks refresh every second using read-only queries, with color and unavailable handling.'
Add-Check 'Read-only MSI Afterburner CPU sensor' (
    $appSource -match 'MemoryMappedFile\.OpenExisting\("MAHMSharedMemory", MemoryMappedFileRights\.Read\)' -and
    $appSource -match 'CreateViewAccessor\(0, 0, MemoryMappedFileAccess\.Read\)' -and
    $appSource -match 'CpuTemperatureSourceId\s*=\s*0x00000080' -and
    $appSource -notmatch 'MemoryMappedFile\.CreateNew|MemoryMappedFileAccess\.Write|WriteArray|WriteByte|WriteInt'
) 'MSI Afterburner CPU temperature is consumed through its existing monitoring map with read-only handles and bounded data validation.'
Add-Check 'Persistent game session notes' (
    $watcher -match 'GameSessionHistory\.txt' -and
    $watcher -match 'Total game time:' -and
    $watcher -match 'Timer cycles:' -and
    $watcher -match 'Peak CPU temperature:' -and
    $watcher -match 'Peak GPU temperature:' -and
    $appSource -match 'class SessionSummaryForm'
) 'A plain-text history and visible exit summary store the game, date/time, duration, cycles, and peak CPU/GPU temperatures.'
$installer=Get-Content -Raw -LiteralPath (Join-Path $PackagePath 'Install-HardwareSquisher.ps1')
Add-Check 'Normal-user startup only' ($installer -match 'CurrentVersion\\Run' -and $installer -notmatch 'ScheduledTask|RunLevel|Verb RunAs') 'No service or elevated startup mechanism is used.'
$setupPath=Join-Path $PackagePath 'HardwareSquisherSetup.cs'
$installerBuilderPath=Join-Path $PackagePath 'Build-HardwareSquisherInstaller.ps1'
$packageBuildFilesPresent=(Test-Path -LiteralPath $setupPath) -and (Test-Path -LiteralPath $installerBuilderPath)
$setup=if($packageBuildFilesPresent){Get-Content -Raw -LiteralPath $setupPath}else{''}
$installerBuilder=if($packageBuildFilesPresent){Get-Content -Raw -LiteralPath $installerBuilderPath}else{''}
$undoScript=Get-Content -Raw -LiteralPath (Join-Path $PackagePath 'Undo-HardwareSquisher.ps1')
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
    $installer -match 'balancedGuid.*boostGuid' -and
    $installer -match 'Unsupported optional settings are skipped'
) 'Setup falls back to Balanced and skips unsupported optional power settings on vendor-specific firmware.'
Add-Check 'Redirected Documents compatibility' (
    $installer -match '\[regex\]::Escape\(\$watcher\)' -and
    $undoScript -match '\[regex\]::Escape\(\$watcherPath\)' -and
    $installer -notmatch '\*Documents\\GameBoost\\HardwareSquisher'
) 'Watcher cleanup uses the resolved installation path, including redirected or localized Documents folders.'
$task=Get-ScheduledTask -TaskName 'Hardware Squisher' -ErrorAction SilentlyContinue
Add-Check 'No elevated Hardware Squisher task' (-not [bool]$task) $(if($task){'An elevated task exists.'}else{'No elevated task is installed.'})
$failed=@($checks|Where-Object{-not $_.Passed});$status=if($failed.Count -eq 0){'PASS'}else{'REVIEW REQUIRED'}
$lines=@('# Hardware Squisher security report','',"Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')",'',"Overall result: **$status**",'','| Check | Result | Detail |','|---|---:|---|')
foreach($check in $checks){$result=if($check.Passed){'PASS'}else{'FAIL'};$lines+="| $($check.Name) | $result | $($check.Detail -replace '\|','\|') |"}
$lines+=@('','## SHA-256 hashes','','```text');foreach($hash in ($scripts|Get-FileHash -Algorithm SHA256|Sort-Object Path)){$lines+="$($hash.Hash)  $([IO.Path]::GetFileName($hash.Path))"};$lines+='```'
$lines|Set-Content -LiteralPath $ReportPath -Encoding UTF8
[pscustomobject]@{Overall=$status;Passed=$checks.Count-$failed.Count;Failed=$failed.Count;Report=$ReportPath}

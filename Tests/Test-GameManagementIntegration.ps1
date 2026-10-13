param(
    [string]$PackagePath = (Split-Path -Parent $MyInvocation.MyCommand.Path),
    [string]$ReportPath = (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'INTEGRATION-TEST-REPORT.md')
)
$ErrorActionPreference = 'Stop'
if ((Split-Path -Leaf $PackagePath) -eq 'Tests') {
    $PackagePath=Split-Path -Parent $PackagePath
    if (-not $PSBoundParameters.ContainsKey('ReportPath')) { $ReportPath=Join-Path $PackagePath 'Documentation\Reports\INTEGRATION-TEST-REPORT.md' }
}
$PackagePath=[IO.Path]::GetFullPath($PackagePath)
$ReportPath=[IO.Path]::GetFullPath($ReportPath)
$invocationContext = if ([string]::IsNullOrEmpty($MyInvocation.ScriptName)) { 'Standalone script' } else { 'Nested script invocation (build compatible)' }
if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne [Threading.ApartmentState]::STA) { throw 'Run integration verification with Windows PowerShell -STA.' }
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$instanceFlags = [Reflection.BindingFlags]'Instance,Public,NonPublic'
$staticFlags = [Reflection.BindingFlags]'Static,Public,NonPublic'
$results = [Collections.Generic.List[object]]::new()
$constructedForms = [Collections.Generic.List[object]]::new()
$fixtureWrites = [Collections.Generic.List[string]]::new()
$fixtureMarker = 'GameManagementIntegrationFixture-' + [guid]::NewGuid().ToString('N')
$externalStateWarnings = [Collections.Generic.List[string]]::new()
$installedDiagnostics = [Collections.Generic.List[object]]::new()
$scratch = Join-Path (Join-Path $PackagePath 'test-output') ('IntegrationTest-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($scratch) | Out-Null
$installedRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments)) 'GameManagement'
$installedBefore = @{}
function Installed-Snapshot([string]$name) {
    $path=Join-Path $installedRoot $name
    if (-not (Test-Path -LiteralPath $path)) { return [pscustomobject]@{Hash=$null;Marker=$false;Error=$null} }
    $stream=$null; $sha=$null; $reader=$null
    try {
        $stream=[IO.FileStream]::new($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
        $sha=[Security.Cryptography.SHA256]::Create()
        $hash=([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-','').ToLowerInvariant()
        $stream.Position=0
        $reader=[IO.StreamReader]::new($stream,[Text.Encoding]::UTF8,$true,4096,$true)
        $tail=''; $hasMarker=$false; $buffer=[char[]]::new(4096)
        while (($count=$reader.Read($buffer,0,$buffer.Length)) -gt 0) {
            $text=$tail+[string]::new($buffer,0,$count)
            if ($text.Contains($fixtureMarker)) { $hasMarker=$true; break }
            $tail=$text.Substring([Math]::Max(0,$text.Length-$fixtureMarker.Length))
        }
        [pscustomobject]@{Hash=$hash;Marker=$hasMarker;Error=$null}
    } catch { [pscustomobject]@{Hash=$null;Marker=$false;Error=$_.Exception.GetType().Name} }
    finally { if ($reader) { $reader.Dispose() }; if ($sha) { $sha.Dispose() }; if ($stream) { $stream.Dispose() } }
}
foreach ($name in @('settings.json','GameSessionHistory.txt','tasks.json','panel-preferences.json')) {
    $installedBefore[$name] = Installed-Snapshot $name
}
function Assert-Integration([bool]$condition, [string]$detail) { if (-not $condition) { throw $detail } }
function Check([string]$name, [scriptblock]$body) {
    try { & $body; $results.Add([pscustomobject]@{Name=$name;Passed=$true;Detail='Verified on isolated fixture data.'}); Write-Host "PASS: $name" }
    catch { $detail = $_.Exception.Message; if ($_.Exception.InnerException) { $detail += ' ' + $_.Exception.InnerException.Message }; $results.Add([pscustomobject]@{Name=$name;Passed=$false;Detail=$detail}); Write-Host "FAIL: $name -- $detail" }
}
function Clean-Arguments([object[]]$values) { $clean = [object[]]::new($values.Count); for ($i=0; $i -lt $values.Count; $i++) { $clean[$i] = if ($null -eq $values[$i]) { $null } else { $values[$i].PSObject.BaseObject } }; ,$clean }
function New-Internal($type, [object[]]$arguments = @()) { [Activator]::CreateInstance($type, $instanceFlags, $null, (Clean-Arguments $arguments), $null) }
function Field($instance, [string]$name) { ,($instance.GetType().GetField($name,$instanceFlags).GetValue($instance)) }
function Assert-ScratchPath([string]$path,[string]$description) {
    $absolute=[IO.Path]::GetFullPath($path).TrimEnd('\')
    $expected=[IO.Path]::GetFullPath($scratch).TrimEnd('\')
    Assert-Integration ([string]::Equals($absolute,$expected,[StringComparison]::OrdinalIgnoreCase) -or $absolute.StartsWith($expected+'\',[StringComparison]::OrdinalIgnoreCase)) ($description+' resolves outside the assigned scratch root.')
}
function Write-FixtureText([string]$path,[string]$text) {
    Assert-ScratchPath $path 'Fixture write'
    [IO.File]::WriteAllText($path,$text,[Text.UTF8Encoding]::new($false))
    $fixtureWrites.Add([IO.Path]::GetFullPath($path))
}
function Assert-PreviewTargets($targetForm) {
    $resolved=[IO.Path]::GetFullPath((Field $targetForm 'rootPath')).TrimEnd('\')
    Assert-Integration ([string]::Equals($resolved,[IO.Path]::GetFullPath($scratch).TrimEnd('\'),[StringComparison]::OrdinalIgnoreCase)) 'A tested form resolved a data root other than its assigned scratch root.'
    foreach($fieldInfo in $targetForm.GetType().GetFields($instanceFlags)) {
        if ($fieldInfo.FieldType -eq [string] -and $fieldInfo.Name -match '^(settings|log|state|timerState|workTimerState|timerCounters|timerResetRequest|timerResetAck|history|performanceState|engineEnabled|watcher|installer|pause)Path$') {
            Assert-ScratchPath ([string]$fieldInfo.GetValue($targetForm)) ('Form '+$fieldInfo.Name)
        }
    }
    $targetOverlays=Field $targetForm 'overlays'
    foreach($name in @('taskStore','panelPreferences')) { $store=Field $targetOverlays $name; Assert-ScratchPath (Field $store 'root') $name; Assert-ScratchPath (Field $store 'path') $name }
    Assert-ScratchPath (Field (Field $targetForm 'reportsPage') 'historyPath') 'Reports history'
    foreach($overview in (Field $targetForm 'sessionOverviews').Values) { Assert-ScratchPath (Field $overview 'historyPath') 'Home history' }
}
function Set-Field($instance, [string]$name, $value) { $instance.GetType().GetField($name,$instanceFlags).SetValue($instance,$value) }
function Property($instance, [string]$name) { ,($instance.GetType().GetProperty($name,$instanceFlags).GetValue($instance,$null)) }
function Invoke-Internal($instance, [string]$name, [object[]]$arguments = @()) {
    $methods = @($instance.GetType().GetMethods($instanceFlags) | Where-Object { $_.Name -eq $name -and $_.GetParameters().Count -eq $arguments.Count })
    if ($methods.Count -ne 1) { throw "Expected one $name method with $($arguments.Count) arguments." }
    $methods[0].Invoke($instance,(Clean-Arguments $arguments))
}
function Descendants($parent) { foreach ($control in $parent.Controls) { $control; Descendants $control } }
function Pump($control) { $control.PerformLayout(); [Windows.Forms.Application]::DoEvents(); $control.PerformLayout(); [Windows.Forms.Application]::DoEvents() }
function Pump-For([int]$milliseconds) { $until = [DateTime]::UtcNow.AddMilliseconds($milliseconds); do { [Windows.Forms.Application]::DoEvents(); [Threading.Thread]::Sleep(8) } while ([DateTime]::UtcNow -lt $until) }
function Metric($overview, [string]$name) { Field (Field $overview $name) 'Value' }
function Bounds-In($control, $ancestor) { $ancestor.RectangleToClient($control.RectangleToScreen($control.ClientRectangle)) }
function Save-Preview($control, [string]$name) { $bitmap = [Drawing.Bitmap]::new($control.Width,$control.Height); try { $control.DrawToBitmap($bitmap,[Drawing.Rectangle]::new(0,0,$bitmap.Width,$bitmap.Height)); $bitmap.Save((Join-Path $PackagePath ('test-output\' + $name))) } finally { $bitmap.Dispose() } }
function New-PreviewForm {
    $newForm = New-Internal $mainType @($false,$false,$false)
    $constructedForms.Add($newForm)
    Assert-PreviewTargets $newForm
    $newForm.ShowInTaskbar = $false
    $newForm.StartPosition = [Windows.Forms.FormStartPosition]::Manual
    $newForm.Location = [Drawing.Point]::new(-4000,-4000)
    $newForm.Show(); Pump $newForm
    $newForm
}
function Verify-Dock($targetForm) {
    $nav = @((Field $targetForm 'navigation').Values)
    Assert-Integration ($nav.Count -eq 5) 'The top navigation must contain exactly five destinations.'
    $titleBar = $nav[0].Parent.Parent
    Assert-Integration ($titleBar.Dock -eq [Windows.Forms.DockStyle]::Top) 'Navigation must be embedded in the title bar.'
    $captions = @($titleBar.Controls | Where-Object { $_ -is [Windows.Forms.Button] -and $_.AccessibleName -in @('Close window','Maximize or restore window','Minimize window') })
    Assert-Integration ($captions.Count -eq 3) 'All Windows caption actions must remain available.'
    $title = @($titleBar.Controls | Where-Object { $_ -is [Windows.Forms.Label] -and $_.Text -eq 'Game Management' })[0]
    foreach ($button in $nav) {
        $bounds = Bounds-In $button $targetForm
        Assert-Integration ($targetForm.ClientRectangle.Contains($bounds)) 'A top navigation destination is clipped.'
        Assert-Integration ($button.TabStop -and $button.Visible) 'Navigation must remain keyboard reachable.'
        foreach ($caption in $captions) { Assert-Integration (-not $bounds.IntersectsWith((Bounds-In $caption $targetForm))) 'Navigation collides with a Windows caption action.' }
        Assert-Integration (-not $bounds.IntersectsWith((Bounds-In $title $targetForm))) 'Navigation collides with the application title.'
    }
    for ($i=0; $i -lt $nav.Count; $i++) { for ($j=$i+1; $j -lt $nav.Count; $j++) { Assert-Integration (-not (Bounds-In $nav[$i] $targetForm).IntersectsWith((Bounds-In $nav[$j] $targetForm))) 'Navigation destinations overlap.' } }
}
$form = $null
$previewMode = $null
$previewRoot = $null
try {
    $fixture = Get-Content -LiteralPath (Join-Path $PackagePath 'settings.json') -Raw | ConvertFrom-Json
    $fixture | Add-Member -NotePropertyName integrationFixtureMarker -NotePropertyValue $fixtureMarker -Force
    $fixture.gameTimerSeconds = 65; $fixture.breakTimerSeconds = 10; $fixture.workTimerSeconds = 3661; $fixture.workBreakSeconds = 5
    $fixture.gameApps = @(); $fixture.workApps = @(); $fixture.gameFolders = @(); $fixture.activeMode = 'game'; $fixture.automaticModeDetection = $false
    $settingsPath = Join-Path $scratch 'settings.json'
    Write-FixtureText $settingsPath ($fixture | ConvertTo-Json -Depth 8)
    $settingsHash = (Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash
    $historyPath = Join-Path $scratch 'GameSessionHistory.txt'
    $historyFixture=@'
Mode: work
Applications: ExampleEditor
Started: 2026-10-10 23:30:00
Finished: 2026-10-11 01:30:00
ActiveSeconds: 5400
ActiveSegments: [{"StartedAt":"2026-10-10T23:30:00","EndedAt":"2026-10-11T00:15:00"},{"StartedAt":"2026-10-11T00:45:00","EndedAt":"2026-10-11T01:30:00"}]
Timer cycles: 2
----
Mode: game
Applications: ExampleGame
Started: 2026-10-11 09:00:00
Finished: 2026-10-11 10:00:00
ActiveSeconds: 1800
Timer cycles: 1
----
Mode: work
Applications: EarlierEditor
Started: 2026-10-10 23:00:00
Finished: 2026-10-11 01:00:00
ActiveSeconds: 3540
ActiveSegments: [{"StartedAt":"2026-10-10T23:00:00","EndedAt":"2026-10-10T23:59:00"}]
----
'@
    Write-FixtureText $historyPath ('SessionId: '+$fixtureMarker+[Environment]::NewLine+$historyFixture)
    $assembly = [Reflection.Assembly]::LoadFile((Join-Path $PackagePath 'GameManagement.exe'))
    $mainType = $assembly.GetType('GameManagement.MainForm',$true)
    $previewMode = $mainType.GetField('PreviewMode',$staticFlags); $previewRoot = $mainType.GetField('PreviewRoot',$staticFlags)
    $previewMode.SetValue($null,$true); $previewRoot.SetValue($null,$scratch)
    $form = New-PreviewForm
    $overlays = Field $form 'overlays'
    $overviews = Field $form 'sessionOverviews'
    $reports = Field $form 'reportsPage'
    Check 'Preview isolates every live timer, watcher and data root' {
        Assert-Integration ((Field $form 'rootPath') -eq $scratch) 'The main form did not use its assigned preview root.'
        foreach ($name in @('refreshTimer','countdownTimer','performanceTimer','foregroundModeTimer')) { Assert-Integration (-not (Field $form $name).Enabled) 'A preview runtime worker was enabled.' }
        Assert-Integration (-not [bool](Property $form 'ModernRefreshEnabledForTest')) 'The cosmetic UI refresh timer was enabled in preview mode.'
        Assert-Integration ($null -eq (Field $form 'uiSignalThread')) 'Preview must not register the live watcher listener.'
    }
    Check 'Modern dashboard refresh survives detached controls and form teardown' {
        $lifecycleForm=New-PreviewForm
        try {
            foreach($page in @('Home','Applications','Home')) {
                Invoke-Internal $lifecycleForm 'SetPageForPreview' @($page) | Out-Null
                foreach($work in @($false,$true)) {
                    Invoke-Internal $lifecycleForm 'SetWorkViewForPreview' @($work) | Out-Null
                    1..4 | ForEach-Object { Invoke-Internal $lifecycleForm 'RunModernRefreshForTest' | Out-Null; Pump $lifecycleForm }
                }
            }
            $gpuLabel=(Field $lifecycleForm 'gpuDisplays')[$false]
            $gpuCard=$gpuLabel.Parent
            $gpuCard.Controls.Remove($gpuLabel)
            Invoke-Internal $lifecycleForm 'SetPageForPreview' @('Home') | Out-Null
            Invoke-Internal $lifecycleForm 'RunModernRefreshForTest' | Out-Null
            Assert-Integration ((Field $lifecycleForm 'footerLabel').Text -like '*Dashboard refresh paused:*') 'A detached dashboard binding was not detected safely.'
            $gpuCard.Controls.Add($gpuLabel)
            Invoke-Internal $lifecycleForm 'RunModernRefreshForTest' | Out-Null
        } finally { $lifecycleForm.Dispose() }
        Invoke-Internal $lifecycleForm 'RunModernRefreshForTest' | Out-Null
        Assert-Integration (-not [bool](Property $lifecycleForm 'ModernRefreshEnabledForTest')) 'The cosmetic UI refresh timer remained active after disposal.'
    }
    Check 'Normal status snapshots refresh Game and Work UI without enabling live workers' {
        $snapshotType = $assembly.GetType('GameManagement.ManagementStatusSnapshot',$true)
        foreach ($work in @($false,$true)) {
            Invoke-Internal $form 'SetWorkViewForPreview' @($work) | Out-Null
            foreach ($managed in @($false,$true)) {
                $snapshot = New-Internal $snapshotType
                Set-Field $snapshot 'Generation' (Field $form 'statusRefreshGeneration')
                Set-Field $snapshot 'Full' $true
                Set-Field $snapshot 'Watcher' $true
                Set-Field $snapshot 'StateActive' $managed
                Set-Field $snapshot 'Managed' $managed
                Set-Field $snapshot 'Startup' (Field $form 'startupCheck').Checked
                Set-Field $snapshot 'Game' 'ExampleGame'
                Set-Field $snapshot 'ActivePlan' 'Fixture plan'
                Set-Field $snapshot 'WorkApplications' ([string[]]@('ExampleEditor'))
                if ($managed) { Set-Field $snapshot 'WorkSessionStartedAt' ([datetime]::Now.AddMinutes(-1)) }
                Invoke-Internal $form 'ApplyStatusSnapshot' @($snapshot) | Out-Null
                Pump $form
                Assert-Integration ((Field $form 'watcherValue').Text -eq 'Running') 'The normal full snapshot did not update watcher status.'
                Assert-Integration ((Field $form 'statusValue').Text -eq $(if ($managed) { 'GAME ACTIVE' } else { 'Ready / monitoring' })) 'The normal snapshot did not update managed status.'
                if ($work) { Assert-Integration ((Field $form 'workAppsValue').Text -eq 'ExampleEditor') 'Work status did not update the bound application label.' }
                foreach ($mode in @($false,$true)) {
                    Assert-Integration (-not (Field $form 'dashboardResumeButtons')[$mode].Enabled -and (Field $form 'dashboardPauseButtons')[$mode].Enabled) 'Both dashboard action pairs must follow a running watcher snapshot.'
                }
            }
        }
        Invoke-Internal $form 'ApplyTemperatureMetrics' @($null,$null,$null) | Out-Null
        Assert-Integration ((Field $form 'gpuDisplays')[$false].Text -eq 'Unavailable') 'The normal missing-temperature refresh did not reach the visible GPU card.'
        foreach ($name in @('refreshTimer','countdownTimer','performanceTimer','foregroundModeTimer')) { Assert-Integration (-not (Field $form $name).Enabled) 'UI-only status verification enabled a live worker.' }
        Invoke-Internal $form 'SetWorkViewForPreview' @($false) | Out-Null
    }
    Check 'Stopped watcher disables Pause and enables Resume on both dashboards' {
        $snapshotType = $assembly.GetType('GameManagement.ManagementStatusSnapshot',$true)
        $snapshot = New-Internal $snapshotType
        Set-Field $snapshot 'Generation' (Field $form 'statusRefreshGeneration')
        Set-Field $snapshot 'Full' $true
        Set-Field $snapshot 'Watcher' $false
        Set-Field $snapshot 'StateActive' $false
        Set-Field $snapshot 'Managed' $false
        Set-Field $snapshot 'Startup' (Field $form 'startupCheck').Checked
        Invoke-Internal $form 'ApplyStatusSnapshot' @($snapshot) | Out-Null
        foreach ($mode in @($false,$true)) {
            Assert-Integration ((Field $form 'dashboardResumeButtons')[$mode].Enabled -and -not (Field $form 'dashboardPauseButtons')[$mode].Enabled) 'Both dashboard action pairs must follow a stopped watcher snapshot.'
        }
    }
    Check 'Five destinations share one title bar at 800, 960 and 1280 widths' {
        foreach ($size in @([Drawing.Size]::new(800,600),[Drawing.Size]::new(960,720),[Drawing.Size]::new(1280,880))) { $form.ClientSize = $size; Pump $form; Verify-Dock $form }
    }
    Check 'Both Home dashboards fit compact timer, applications and lower cards at 1280 by 880' {
        $form.ClientSize = [Drawing.Size]::new(1280,880)
        Invoke-Internal $form 'SetPageForPreview' @('Home') | Out-Null
        foreach ($work in @($false,$true)) {
            Invoke-Internal $form 'SetWorkViewForPreview' @($work) | Out-Null; Pump $form
            $surface = Field $form $(if ($work) { 'workSurface' } else { 'gameSurface' })
            $content = @($surface.Controls | Where-Object { $_ -is [Windows.Forms.Panel] })[0]
            Assert-Integration ($surface.ClientRectangle.Contains($content.Bounds)) ('The full compact dashboard requires scrolling at its default size. ' + $surface.ClientRectangle + ' versus ' + $content.Bounds)
            Assert-Integration ($overviews[$work].Visible -and (Field $form 'dashboardSettings')[$work].Visible) 'The overview and timer summary must be visible on both dashboards.'
            $cards = @($content.Controls | Where-Object { $_.Top -eq 56 })
            Assert-Integration ($cards.Count -eq 3) 'Home must have three summary cards.'
            foreach ($card in $cards) { Assert-Integration ($content.ClientRectangle.Contains($card.Bounds)) 'A metric card is clipped.' }
            $grid = Field $form $(if ($work) { 'workDashboardGrid' } else { 'gameDashboardGrid' })
            Assert-Integration ($grid.Columns.Count -eq 7 -and $grid.Parent.Parent -eq $content) 'Selected applications must retain executable paths, statuses and three actions.'
            $countdown = Field $form $(if ($work) { 'workMinutesLeftValue' } else { 'minutesLeftValue' })
            $progress = Field $form $(if ($work) { 'workProgress' } else { 'gameProgress' })
            Assert-Integration ($countdown.Bottom -lt $progress.Top -and $countdown.Font.Height -le $countdown.Height) 'The countdown label must fit its slot and leave the progress track visible.'
            $buttons = @(Descendants $surface | Where-Object { $_ -is [Windows.Forms.Button] -and ($_.Text -in @('Start / Resume','Pause','Reset','Configure') -or $_.Text.StartsWith('Add Application')) })
            Assert-Integration ($buttons.Count -eq 5 -and @($buttons | Where-Object { -not $_.Visible -or -not $_.TabStop }).Count -eq 0) ('Dashboard actions must remain visible and keyboard reachable. Found ' + $buttons.Count + ': ' + (($buttons | ForEach-Object { $_.Text + '=' + $_.Visible + '/' + $_.TabStop }) -join ', '))
        }
        Invoke-Internal $form 'SetWorkViewForPreview' @($false) | Out-Null
        Save-Preview $form 'integration-home-100.png'
    }
    Check 'Unavailable telemetry is truthful on both modes' {
        foreach ($name in @('gpuTemperatureValue','cpuTemperatureValue','workGpuValue','workCpuValue')) { Assert-Integration ((Field $form $name).Text -eq 'Unavailable') 'Preview must not fabricate hardware temperatures.' }
        foreach ($label in (Field $form 'gpuDisplays').Values) { Assert-Integration ($label.Text -eq 'Unavailable') 'The visible GPU card must report missing telemetry.' }
        foreach ($badge in (Field $form 'thermalBadges').Values) { Assert-Integration ($badge.Text -eq 'Unavailable') 'Thermal status must not claim Safe when telemetry is unavailable.' }
    }
    Check 'A small Home window stacks lower cards and offers scrolling' {
        $form.ClientSize = [Drawing.Size]::new(800,600); Pump $form
        $surface = Field $form 'gameSurface'; $overview = $overviews[$false]; $summary = (Field $form 'dashboardSettings')[$false].Parent
        Assert-Integration ($overview.Top -gt $summary.Bottom -and $overview.Left -eq 0) ('Small Home must stack the statistics below timer settings. Overview ' + $overview.Bounds + ', settings ' + $summary.Bounds)
        Assert-Integration ($surface.AutoScrollMinSize.Height -gt $surface.ClientSize.Height) 'Small Home must allow vertical scrolling to the lower cards.'
        $form.ClientSize = [Drawing.Size]::new(1280,880); Pump $form
    }
    Check 'Calendar custom range updates Reports and both real Home overviews' {
        Invoke-Internal $overlays 'SelectCalendarRange' @('Today') | Out-Null
        Invoke-Internal $overlays 'SelectCalendarRange' @('Custom') | Out-Null
        (Field $overlays 'rangeFrom').Value = [datetime]'2026-10-11'
        (Field $overlays 'rangeTo').Value = [datetime]'2026-10-11'
        foreach ($overview in $overviews.Values) {
            Invoke-Internal $overview 'RefreshData' @($true) | Out-Null
            Assert-Integration ((Property $overview 'RangeStart') -eq [datetime]'2026-10-11' -and (Property $overview 'RangeEnd') -eq [datetime]'2026-10-11') 'A Home overview did not follow Calendar.'
            Assert-Integration ((Metric $overview 'workCount') -eq '1' -and (Metric $overview 'workTime') -eq '01:00:00') 'Cross-midnight active segments or paused gaps were counted incorrectly.'
            Assert-Integration ((Metric $overview 'gameCount') -eq '1' -and (Metric $overview 'gameTime') -eq '00:30:00') 'Legacy checkpoint active duration was counted incorrectly.'
            Assert-Integration ((Field $overview 'history').Rows.Count -eq 2) 'A session with zero active time in range must not appear in Home history.'
        }
        Assert-Integration ((Field $reports 'from').Value.Date -eq [datetime]'2026-10-11' -and (Field $reports 'to').Value.Date -eq [datetime]'2026-10-11') 'Reports did not follow Calendar.'
        Assert-Integration ((Field $reports 'history').Rows.Count -eq 2) 'Reports and Home must agree on matching saved sessions.'
    }
    Check 'Calendar and overview View reports actions carry their selected dates' {
        Invoke-Internal $overlays 'ShowForPreview' @('calendar') | Out-Null; Pump $form
        (Field $overlays 'viewReports').PerformClick()
        Assert-Integration ((Field $form 'currentPage') -eq 'Reports') 'Calendar View reports did not navigate.'
        Invoke-Internal $form 'SetPageForPreview' @('Home') | Out-Null
        Invoke-Internal $form 'SetWorkViewForPreview' @($false) | Out-Null
        Invoke-Internal $overviews[$false] 'SetRange' @([datetime]'2026-10-10',[datetime]'2026-10-11') | Out-Null
        (Field $overviews[$false] 'reports').PerformClick()
        Assert-Integration ((Field $form 'currentPage') -eq 'Reports' -and (Field $reports 'from').Value.Date -eq [datetime]'2026-10-10') 'Overview View reports must carry an independently selected range.'
        Invoke-Internal $form 'SetPageForPreview' @('Home') | Out-Null
    }
    Check 'Calendar week uses current culture and Today resets linked ranges' {
        Invoke-Internal $overlays 'SelectCalendarRange' @('This Week') | Out-Null
        $firstDay = [int][Globalization.CultureInfo]::CurrentCulture.DateTimeFormat.FirstDayOfWeek
        $expected = [datetime]::Today.AddDays(-(([int][datetime]::Today.DayOfWeek - $firstDay + 7) % 7))
        Assert-Integration ((Property $overlays 'RangeStart') -eq $expected -and (Property $overlays 'RangeEnd') -eq [datetime]::Today) 'Calendar week-to-date must use the current culture first day and disable future dates.'
        Invoke-Internal $overlays 'SelectCalendarRange' @('Today') | Out-Null
        foreach ($overview in $overviews.Values) { Assert-Integration ((Property $overview 'RangeStart') -eq [datetime]::Today) 'Today did not update every overview.' }
    }
    Check 'Expanded Calendar and Tasks stack as overlays without shifting Home' {
        $surface = Field $form 'gameSurface'; $before = $surface.Size
        Invoke-Internal $overlays 'ShowForPreview' @('both') | Out-Null; Pump-For 230
        $calendar = Field $overlays 'calendarPanel'; $tasks = Field $overlays 'tasksPanel'; $overlayContainer = Field $form 'homePage'
        Assert-Integration ($calendar.Left -eq $tasks.Left -and $calendar.Bottom -lt $tasks.Top) 'Expanded panels must be a vertical stack.'
        Assert-Integration ($overlayContainer.ClientRectangle.Contains($calendar.Bounds) -and $overlayContainer.ClientRectangle.Contains($tasks.Bounds)) 'Expanded overlay cards are clipped.'
        Assert-Integration ($surface.Size -eq $before) 'Overlay opening must not resize the dashboard.'
    }
    Check 'Focused Tasks remains open until focus leaves then collapses safely' {
        Invoke-Internal $overlays 'SetPanelPinned' @('tasks',$false) | Out-Null
        Invoke-Internal $overlays 'OpenPanel' @($true) | Out-Null; Pump-For 230
        (Field $overlays 'taskTitle').Focus() | Out-Null
        Set-Field $overlays 'tasksCloseAt' ([datetime]::UtcNow.AddSeconds(-2))
        Invoke-Internal $overlays 'CloseTimerTick' @($null,[EventArgs]::Empty) | Out-Null
        Assert-Integration ((Property $overlays 'TasksVisible')) 'A panel must retain its focused input.'
        (Field $form 'navigation')['Home'].Focus() | Out-Null
        Set-Field $overlays 'tasksCloseAt' ([datetime]::UtcNow.AddSeconds(-2))
        Invoke-Internal $overlays 'CloseTimerTick' @($null,[EventArgs]::Empty) | Out-Null; Pump-For 230
        Assert-Integration (-not (Property $overlays 'TasksVisible')) 'An unfocused unpinned panel did not collapse.'
        Assert-Integration ((Field $form 'navigation')['Home'].Focused) 'Panel collapse must preserve the current keyboard focus.'
    }
    Check 'Preview pins do not write preferences; persisted pins restore independently' {
        Assert-Integration (-not (Test-Path -LiteralPath (Join-Path $scratch 'panel-preferences.json'))) 'Preview pinning unexpectedly wrote preferences.'
        $pinRoot = Join-Path $scratch 'pin-fixture'; [IO.Directory]::CreateDirectory($pinRoot) | Out-Null
        Assert-ScratchPath $pinRoot 'Pin persistence fixture'
        $pinContainer = [Windows.Forms.Panel]::new(); $pinContainer.Size = [Drawing.Size]::new(960,800)
        $overlayType = $assembly.GetType('GameManagement.OverlayPanels',$true)
        $first = New-Internal $overlayType @($pinContainer,$pinRoot,$true)
        try { Invoke-Internal $first 'SetPanelPinned' @('calendar',$true) | Out-Null; Invoke-Internal $first 'SetPanelPinned' @('tasks',$false) | Out-Null } finally { $first.Dispose() }
        $second = New-Internal $overlayType @($pinContainer,$pinRoot,$true)
        try { Assert-Integration ((Property $second 'CalendarPinned') -and -not (Property $second 'TasksPinned')) 'Persisted pins did not restore independently.'; Assert-Integration ((Property $second 'CalendarVisible')) 'A restored pinned calendar must reopen.' } finally { $second.Dispose(); $pinContainer.Dispose() }
    }
    Check 'Restore defaults remains pending until Save and Discard reloads exact seconds' {
        Invoke-Internal $form 'SetPageForPreview' @('Timer Settings') | Out-Null
        $restore = @(Descendants $form | Where-Object { $_ -is [Windows.Forms.Button] -and $_.Text -eq 'Restore defaults' })[0]
        $restore.PerformClick()
        Assert-Integration ((Field $form 'timerMinutesInput').TotalSeconds -eq 1800 -and (Field $form 'workTimerMinutesInput').TotalSeconds -eq 1800) 'Default work/game durations were not loaded.'
        Assert-Integration ((Field $form 'breakMinutesInput').TotalSeconds -eq 300 -and (Field $form 'workBreakMinutesInput').TotalSeconds -eq 300) 'Default break durations were not loaded.'
        Assert-Integration ((Field $form 'confirmTimerButton').Enabled -and [bool](Invoke-Internal $form 'HasUnsavedSettings')) 'Restore defaults must create pending edits.'
        Assert-Integration ((Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash -eq $settingsHash) 'Restore defaults changed stored settings before Save.'
        @(Descendants $form | Where-Object { $_ -is [Windows.Forms.Button] -and $_.Text -eq 'Discard edits' })[0].PerformClick()
        Assert-Integration ((Field $form 'timerMinutesInput').TotalSeconds -eq 65 -and (Field $form 'workTimerMinutesInput').TotalSeconds -eq 3661 -and -not [bool](Invoke-Internal $form 'HasUnsavedSettings')) 'Discard must restore exact-second values and clear pending edits.'
        Invoke-Internal $form 'SetPageForPreview' @('Home') | Out-Null
    }
    Check 'Timer sound and time-up options synchronize and enable Save only for pending changes' {
        Invoke-Internal $form 'SetPageForPreview' @('Timer Settings') | Out-Null
        foreach ($pair in @(@('timerPopupOption','popupOption'),@('timerSoundOption','soundOption'))) {
            $timerOption = Field $form $pair[0]; $option = Field $form $pair[1]
            $savedValue = $timerOption.Checked
            $timerOption.Checked = -not $savedValue
            Assert-Integration ($option.Checked -eq $timerOption.Checked -and (Field $form 'confirmTimerButton').Enabled -and [bool](Invoke-Internal $form 'HasUnsavedSettings')) 'Editing a Timer Settings behavior option must synchronize and enable Save.'
            Assert-Integration ((Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash -eq $settingsHash) 'A behavior option was saved before Save timers.'
            $option.Checked = $savedValue
            Assert-Integration ($timerOption.Checked -eq $savedValue -and -not (Field $form 'confirmTimerButton').Enabled -and -not [bool](Invoke-Internal $form 'HasUnsavedSettings')) 'Restoring the saved Options value must clear its pending edit in Timer Settings.'
        }
        Invoke-Internal $form 'SetPageForPreview' @('Home') | Out-Null
    }
    Check 'Native alerts expose Save Discard Cancel and Remove Keep with keyboard bindings' {
        $alertType = $assembly.GetType('GameManagement.ModernAlertForm',$true)
        foreach ($configuration in @(
            @{Title='Unsaved changes';Buttons=[Windows.Forms.MessageBoxButtons]::YesNoCancel;Yes='Save';No='Discard';Cancel='Cancel';Expected=[Windows.Forms.DialogResult]::Cancel},
            @{Title='Remove application';Buttons=[Windows.Forms.MessageBoxButtons]::YesNo;Yes='Remove';No='Keep';Cancel=$null;Expected=[Windows.Forms.DialogResult]::No}
        )) {
            $alert = New-Internal $alertType @($configuration.Title,'Fixture-only confirmation.',$configuration.Buttons,[Windows.Forms.MessageBoxIcon]::Question,$configuration.Yes,$configuration.No,$configuration.Cancel)
            try {
                $alert.Location = [Drawing.Point]::new(-4000,-4000); $alert.StartPosition = [Windows.Forms.FormStartPosition]::Manual; $alert.Show(); Pump $alert
                $buttons = @(Descendants $alert | Where-Object { $_ -is [Windows.Forms.Button] -and $_.DialogResult -ne [Windows.Forms.DialogResult]::None })
                Assert-Integration (@($buttons | Where-Object Text -eq $configuration.Yes).Count -eq 1 -and @($buttons | Where-Object Text -eq $configuration.No).Count -eq 1) 'Alert action labels must describe their effects.'
                Assert-Integration ($alert.AcceptButton.Text -eq $configuration.Yes -and $alert.CancelButton.Text -eq $(if ($configuration.Cancel) { $configuration.Cancel } else { $configuration.No })) 'Enter and Escape bindings must match the alert actions.'
                Invoke-Internal $alert 'ProcessDialogKey' @([Windows.Forms.Keys]::Escape) | Out-Null
                Assert-Integration ($alert.DialogResult -eq $configuration.Expected) 'Escape must choose the non-destructive alert result.'
            } finally { $alert.Dispose() }
        }
        $alert = New-Internal $alertType @('Save changes','Fixture-only save action.',[Windows.Forms.MessageBoxButtons]::YesNoCancel,[Windows.Forms.MessageBoxIcon]::Question,'Save','Discard','Cancel')
        try { $alert.Show(); Pump $alert; $alert.AcceptButton.Focus() | Out-Null; Invoke-Internal $alert 'ProcessDialogKey' @([Windows.Forms.Keys]::Enter) | Out-Null; Assert-Integration ($alert.DialogResult -eq [Windows.Forms.DialogResult]::Yes) 'Enter on the focused Save action must select Save.' } finally { $alert.Dispose() }
    }
    Check 'Application removal uses the actual Remove Keep alert and only changes selection' {
        $selectedPath = Join-Path $scratch 'ExampleApplication.exe'
        Write-FixtureText $selectedPath ('fixture only; never executed; '+$fixtureMarker)
        (Field $form 'workAppsList').Items.Add($selectedPath) | Out-Null
        Set-Field $form 'applicationWorkTab' $true
        Invoke-Internal $form 'SetPageForPreview' @('Applications') | Out-Null
        Invoke-Internal $form 'RefreshApplicationPage' | Out-Null; Pump $form
        $grid = Field $form 'managementGrid'
        foreach ($choice in @('Keep','Remove')) {
            $modalState = @{Verified=$false;Error=$null;Deadline=[datetime]::UtcNow.AddSeconds(5)}
            $modalTimer = [Windows.Forms.Timer]::new(); $modalTimer.Interval = 30
            $modalTimer.add_Tick({
                $dialog = @([Windows.Forms.Application]::OpenForms | Where-Object { $_.GetType().Name -eq 'ModernAlertForm' }) | Select-Object -First 1
                if (-not $dialog) { return }
                $modalTimer.Stop()
                try {
                    # A GetNewClosure callback runs in its own dynamic module.
                    # Walk the native controls here so nested build invocation does
                    # not depend on lookup of helpers in the caller's script scope.
                    $pendingControls = [Collections.Generic.Queue[Windows.Forms.Control]]::new()
                    $actions = [Collections.Generic.List[Windows.Forms.Button]]::new()
                    $pendingControls.Enqueue($dialog)
                    while ($pendingControls.Count) {
                        $control = $pendingControls.Dequeue()
                        foreach ($child in $control.Controls) { $pendingControls.Enqueue($child) }
                        if ($control -is [Windows.Forms.Button] -and $control.Text -in @('Remove','Keep')) { $actions.Add($control) }
                    }
                    if ($actions.Count -ne 2) { throw 'Application removal must show explicit Remove and Keep choices.' }
                    $selectedAction = $null
                    foreach ($action in $actions) { if ($action.Text -eq $choice) { $selectedAction = $action; break } }
                    if ($null -eq $selectedAction) { throw 'The requested application removal action is missing.' }
                    $selectedAction.PerformClick()
                    $modalState.Verified = $true
                } catch { $modalState.Error = $_.Exception.Message; $dialog.Close() }
            }.GetNewClosure())
            try { $modalTimer.Start(); Invoke-Internal $form 'ApplicationAction' @($grid,[Windows.Forms.DataGridViewCellEventArgs]::new(6,0),$true) | Out-Null }
            finally { $modalTimer.Stop(); $modalTimer.Dispose() }
            Assert-Integration ($modalState.Verified -and -not $modalState.Error) ('The actual application alert could not be verified. ' + $modalState.Error)
            $expectedCount = if ($choice -eq 'Keep') { 1 } else { 0 }
            Assert-Integration ((Field $form 'workAppsList').Items.Count -eq $expectedCount) 'The selection did not follow its confirmation choice.'
            Assert-Integration (Test-Path -LiteralPath $selectedPath) 'Removing a selection must preserve its executable.'
        }
        Invoke-Internal $form 'LoadSettings' | Out-Null
        Invoke-Internal $form 'SetPageForPreview' @('Home') | Out-Null
    }
    Check 'Hover transitions settle and honor the current reduced-motion preference' {
        $button = @(Descendants $form | Where-Object { $_.GetType().Name -eq 'ModernButton' -and $_.Visible })[0]
        $theme = $assembly.GetType('GameManagement.ModernTheme',$true)
        $reduced = $theme.GetProperty('ReducedMotion',$staticFlags).GetValue($null,$null)
        Assert-Integration ($reduced -eq (-not [Windows.Forms.SystemInformation]::IsMenuAnimationEnabled)) 'Reduced motion must follow the Windows animation preference.'
        Invoke-Internal $button 'OnMouseEnter' @([EventArgs]::Empty) | Out-Null
        if ($reduced) { Assert-Integration ((Field $button 'hoverAmount') -eq 1 -and -not (Field $button 'hoverTimer').Enabled) 'Reduced-motion hover must settle immediately.' } else { Pump-For 250; Assert-Integration ((Field $button 'hoverAmount') -eq 1 -and -not (Field $button 'hoverTimer').Enabled) 'Animated hover did not settle.' }
        Invoke-Internal $button 'OnMouseLeave' @([EventArgs]::Empty) | Out-Null; Pump-For 250
        Assert-Integration ((Field $button 'hoverAmount') -eq 0) 'Hover leave did not return to its resting state.'
    }
    foreach ($factor in @(1.0,1.25,1.5)) {
        $percent = [int]($factor * 100)
        Check "WinForms scaling simulation at $percent percent keeps navigation and timer controls reachable" {
            $scaled = New-PreviewForm
            try {
                $scaled.AutoScaleMode = [Windows.Forms.AutoScaleMode]::None
                $scaled.SuspendLayout()
                $fontSnapshot = @(@($scaled) + @(Descendants $scaled) | ForEach-Object { [pscustomobject]@{Control=$_;Font=$_.Font} })
                $scaled.Scale([Drawing.SizeF]::new($factor,$factor))
                # Enlarge explicit fonts as well as control geometry; do not change Windows DPI.
                if ($factor -gt 1) { foreach ($entry in $fontSnapshot) { $font = $entry.Font; $entry.Control.Font = [Drawing.Font]::new($font.FontFamily,[single]($font.Size * $factor),$font.Style,$font.Unit) } }
                # A visible WinForms window cannot exceed the native tracking limit.
                # Request a reachable viewport up front so Windows cannot clamp only
                # the native window after child controls laid out at a larger size.
                $trackingLimit = [Windows.Forms.SystemInformation]::MaxWindowTrackSize
                $scaled.ClientSize = [Drawing.Size]::new([Math]::Min([int](1280*$factor),$trackingLimit.Width),[Math]::Min([int](880*$factor),$trackingLimit.Height))
                $scaled.ResumeLayout($true); Pump $scaled
                Write-Host ("Scaling {0}: form {1}, client {2}, title bar {3}" -f $percent,$scaled.Size,$scaled.ClientSize,(Field $scaled 'navigation')['Home'].Parent.Parent.Size)
                Save-Preview $scaled "integration-home-$percent.png"
                Verify-Dock $scaled
                foreach ($button in (Field $scaled 'navigation').Values) {
                    $textSize = [Windows.Forms.TextRenderer]::MeasureText($button.Text,$button.Font,[Drawing.Size]::new(400,100),[Windows.Forms.TextFormatFlags]::NoPadding)
                    Assert-Integration ($textSize.Width -le $button.ClientSize.Width - 8) ('A scaled navigation label is clipped: ' + $button.Text)
                }
                foreach ($name in @('gameElapsedValue','cycleValue','minutesLeftValue')) {
                    $label = Field $scaled $name
                    $textSize = [Windows.Forms.TextRenderer]::MeasureText($label.Text,$label.Font,[Drawing.Size]::new(2000,300),[Windows.Forms.TextFormatFlags]::NoPadding)
                    Assert-Integration ($textSize.Height -le $label.ClientSize.Height) ('A scaled metric or countdown is vertically clipped: ' + $name)
                }
                $visibleGpu = (Field $scaled 'gpuDisplays')[$false]
                $gpuText = [Windows.Forms.TextRenderer]::MeasureText($visibleGpu.Text,$visibleGpu.Font,[Drawing.Size]::new(1000,200),[Windows.Forms.TextFormatFlags]::NoPadding)
                Assert-Integration ($gpuText.Height -le $visibleGpu.Height) 'The scaled visible GPU card text is clipped.'
                Invoke-Internal $scaled 'SetPageForPreview' @('Timer Settings') | Out-Null; Pump $scaled
                foreach ($name in @('timerMinutesInput','workTimerMinutesInput')) {
                    $input = Field $scaled $name
                    foreach ($number in @(Descendants $input | Where-Object { $_ -is [Windows.Forms.NumericUpDown] })) { Assert-Integration ($number.Width -ge 30 -and $input.ClientRectangle.Contains((Bounds-In $number $input))) ('A scaled duration field is clipped: ' + $input.ClientRectangle + ' versus ' + (Bounds-In $number $input)) }
                }
                Invoke-Internal $scaled 'SetPageForPreview' @('Home') | Out-Null; Pump $scaled
                $overview = (Field $scaled 'sessionOverviews')[$false]
                Invoke-Internal $overview 'RefreshData' @($true) | Out-Null
                Assert-Integration ($overview.Width -ge 300 -and (Field $overview 'period').TabStop) 'Scaled session statistics must remain usable.'
                $scaledSurface = Field $scaled 'gameSurface'
                $scaledSurface.ScrollControlIntoView($overview); Pump $scaledSurface
                Assert-Integration ($scaledSurface.ClientRectangle.Contains((Bounds-In $overview $scaledSurface))) 'The full scaled Home statistics card cannot be reached by scrolling.'
                $overviewHistory = Field $overview 'history'
                $overviewCell = [Windows.Forms.TextRenderer]::MeasureText('00:30:00',$overviewHistory.DefaultCellStyle.Font,[Drawing.Size]::new(500,150),[Windows.Forms.TextFormatFlags]::NoPadding)
                Assert-Integration ($overviewCell.Height -lt $overviewHistory.RowTemplate.Height) 'Scaled Home history rows clip their text.'
                Save-Preview $scaled "integration-home-$percent-lower.png"
                $overview.ScrollControlIntoView((Field $overview 'reports')); Pump $overview
                Assert-Integration ($overview.ClientRectangle.Contains((Bounds-In $overviewHistory $overview)) -and $overview.ClientRectangle.Contains((Bounds-In (Field $overview 'reports') $overview))) 'The Home statistics history and View reports action cannot be reached inside the compact card.'
                Save-Preview $scaled "integration-home-$percent-history.png"
                $overview.AutoScrollPosition = [Drawing.Point]::Empty; Pump $overview
                $scaledSurface.AutoScrollPosition = [Drawing.Point]::Empty; Pump $scaledSurface
                Invoke-Internal $scaled 'SetPageForPreview' @('Reports') | Out-Null; Pump $scaled
                $scaledReports = Field $scaled 'reportsPage'
                foreach ($name in @('workValue','gameValue','totalValue','cyclesValue')) {
                    $value = Field $scaledReports $name
                    Assert-Integration ($value.Parent.ClientRectangle.Contains($value.Bounds)) ('A scaled Reports metric is clipped: ' + $name)
                }
                $reportHeading = @(Descendants $scaledReports | Where-Object { $_ -is [Windows.Forms.Label] -and $_.Text -eq 'Reports' })[0]
                $reportSubtitle = @(Descendants $scaledReports | Where-Object { $_ -is [Windows.Forms.Label] -and $_.Text -eq 'Understand how you spend your time.' })[0]
                Save-Preview $scaled "integration-reports-$percent.png"
                Assert-Integration (-not $reportHeading.Bounds.IntersectsWith($reportSubtitle.Bounds)) ('The Reports title overlaps its subtitle. Title ' + $reportHeading.Bounds + ', subtitle ' + $reportSubtitle.Bounds)
                $reportHistory = Field $scaledReports 'history'
                $cellText = [Windows.Forms.TextRenderer]::MeasureText('00:30:00',$reportHistory.DefaultCellStyle.Font,[Drawing.Size]::new(500,150),[Windows.Forms.TextFormatFlags]::NoPadding)
                Assert-Integration ($cellText.Height -lt $reportHistory.RowTemplate.Height) 'Scaled report history rows clip their text.'
                Save-Preview $scaled "integration-reports-$percent.png"
                $scaledReports.ScrollControlIntoView($reportHistory.Parent); Pump $scaledReports
                Assert-Integration ($scaledReports.ClientRectangle.Contains((Bounds-In $reportHistory.Parent $scaledReports))) 'The scaled Reports history cannot be reached by scrolling.'
                Save-Preview $scaled "integration-reports-$percent-history.png"
                Invoke-Internal $scaled 'SetPageForPreview' @('Home') | Out-Null; Pump $scaled
                Save-Preview $scaled "integration-home-$percent.png"
            } finally { $scaled.Dispose() }
        }
    }
} catch { $results.Add([pscustomobject]@{Name='Integration harness initialization';Passed=$false;Detail=$_.Exception.Message}) }
finally {
    if ($form) { $form.Dispose() }
    if ($previewMode) { $previewMode.SetValue($null,$false) }
    if ($previewRoot) { $previewRoot.SetValue($null,$null) }
    Check 'Every tested form and fixture write stays in scratch without leaking fixture markers to installed data' {
        Assert-Integration ($constructedForms.Count -gt 0) 'No constructed preview forms were recorded for isolation verification.'
        foreach($testedForm in $constructedForms) { Assert-PreviewTargets $testedForm }
        foreach($writePath in $fixtureWrites) { Assert-ScratchPath $writePath 'Recorded fixture write' }
        foreach ($name in @('settings.json','GameSessionHistory.txt','tasks.json','panel-preferences.json')) {
            $before=$installedBefore[$name]
            $after=Installed-Snapshot $name
            Assert-Integration (-not $before.Marker -and -not $after.Marker) ('The unique integration fixture marker appeared in installed '+$name+'.')
            $state='Same observed hash'
            if ($before.Error -or $after.Error) {
                $state='Diagnostic read unavailable'
                $externalStateWarnings.Add($name+': installed-data hash or marker diagnostics could not be read concurrently ('+$before.Error+' / '+$after.Error+').')
            } elseif ($before.Hash -ne $after.Hash) {
                $state='Changed during verification'
                $externalStateWarnings.Add($name+': installed data changed during the suite while all tested form/write targets remained in scratch; the harness cannot attribute that external change.')
            }
            $installedDiagnostics.Add([pscustomobject]@{Name=$name;Before=$before.Hash;After=$after.Hash;State=$state})
        }
    }
    if ($results.Count -and $results[$results.Count-1].Passed) { $results[$results.Count-1].Detail='Verified '+$constructedForms.Count+' form roots and '+$fixtureWrites.Count+' fixture write paths. Unique fixture markers were absent from readable installed data snapshots. Installed hashes are diagnostic observations.' }
    $absoluteScratch = [IO.Path]::GetFullPath($scratch).TrimEnd('\')
    $expectedParent = [IO.Path]::GetFullPath((Join-Path $PackagePath 'test-output')).TrimEnd('\')
    if ((Test-Path -LiteralPath $absoluteScratch) -and [string]::Equals([IO.Directory]::GetParent($absoluteScratch).FullName.TrimEnd('\'),$expectedParent,[StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($absoluteScratch) -match '^IntegrationTest-[0-9a-f]{32}$') { Remove-Item -LiteralPath $absoluteScratch -Recurse -Force }
}
$failures = @($results | Where-Object { -not $_.Passed })
$outcome = if ($failures.Count) { 'FAIL' } else { 'PASS' }
$verifiedHash = (Get-FileHash -LiteralPath (Join-Path $PackagePath 'GameManagement.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
$lines = @('# Version 5 UI integration verification','',"Result: **$outcome**. $($results.Count-$failures.Count) passed, $($failures.Count) failed.",'',"Invocation: $invocationContext.",'',"Verified executable SHA-256: $verifiedHash",'',
    'The harness creates a temporary preview root containing anonymous test settings and recorded-session fixtures. Every constructed MainForm and its settings, history, task and panel-preference write targets must resolve to that scratch root. Every direct fixture write is checked before writing; unique fixture markers are checked against readable installed data snapshots. It does not enable or pause the live watcher, change startup behavior, or change Windows display settings.','',
    'Installed settings, history, tasks and panel-preference hashes are recorded before and after as diagnostics. The user may run the installed application during the suite. A concurrent installed-file change is reported as external state, without claiming that the file remained unchanged or attributing its writer. A tested form resolving outside scratch, or a unique fixture marker appearing in installed data, fails isolation.','',
    'The 100%, 125% and 150% checks simulate WinForms control scaling and larger fonts. The viewport is limited to the current native maximum window size; larger content must remain reachable by scrolling. They are not a substitute for testing per-monitor DPI transitions on another display. Reduced motion is checked against the current Windows animation preference; that preference is never changed.','',
    '| Check | Result | Evidence |','|---|---|---|')
foreach ($result in $results) { $detail = ($result.Detail -replace '[\r\n]+',' ') -replace '\|','\|'; $label = if ($result.Passed) { 'PASS' } else { 'FAIL' }; $lines += "| $($result.Name) | $label | $detail |" }
$lines+=@('','Installed data hash diagnostics:','','| File | Before SHA-256 | After SHA-256 | Observation |','|---|---|---|---|')
foreach($diagnostic in $installedDiagnostics) { $beforeHash=if($diagnostic.Before) {$diagnostic.Before} else {'Missing or unreadable'}; $afterHash=if($diagnostic.After) {$diagnostic.After} else {'Missing or unreadable'}; $lines+="| $($diagnostic.Name) | $beforeHash | $afterHash | $($diagnostic.State) |" }
if ($externalStateWarnings.Count) {
    $lines+=@('','External-state warnings:','')
    foreach($warning in $externalStateWarnings) { $lines+='- '+$warning; Write-Warning $warning }
} else { $lines+=@('','No installed-file hash change was observed between the diagnostic snapshots.') }
$lines += @('','Timer engine, exact-second checkpoints, mode detection, saved data compatibility and watcher handoff are covered by the existing feature and lifecycle suites. This suite adds Home, range-linking, overlay, pending-default, alert keyboard and layout integration coverage.','',
    'Preview artifacts: `test-output/integration-home-100.png`, `test-output/integration-home-125.png`, `test-output/integration-home-150.png`, and matching `integration-reports-100.png`, `integration-reports-125.png`, `integration-reports-150.png` in `test-output`. Each scale also has `-lower` and `-history` Home previews and a `-history` Reports preview showing content reached by scrolling. They contain test fixtures only.')
[IO.Directory]::CreateDirectory((Split-Path -Parent ([IO.Path]::GetFullPath($ReportPath)))) | Out-Null
[IO.File]::WriteAllLines($ReportPath,$lines,[Text.UTF8Encoding]::new($false))
[pscustomobject]@{Overall=$outcome;Passed=$results.Count-$failures.Count;Failed=$failures.Count;Report=$ReportPath}
if ($failures.Count) { throw "$($failures.Count) integration checks failed; inspect the integration report." }

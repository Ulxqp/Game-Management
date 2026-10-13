param(
    [string]$PackagePath = (Split-Path -Parent $MyInvocation.MyCommand.Path),
    [string]$ReportPath = (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'V5-TEST-REPORT.md')
)

$ErrorActionPreference = 'Stop'
if ((Split-Path -Leaf $PackagePath) -eq 'Tests') {
    $PackagePath = Split-Path -Parent $PackagePath
    if (-not $PSBoundParameters.ContainsKey('ReportPath')) {
        $ReportPath = Join-Path $PackagePath 'Documentation\Reports\V5-TEST-REPORT.md'
        [IO.Directory]::CreateDirectory((Split-Path -Parent $ReportPath)) | Out-Null
    }
}
$PackagePath = [IO.Path]::GetFullPath($PackagePath)
$ReportPath = [IO.Path]::GetFullPath($ReportPath)
if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne [Threading.ApartmentState]::STA) {
    throw 'Run this UI verification with Windows PowerShell -STA.'
}
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$results = [Collections.Generic.List[object]]::new()
$allInstance = [Reflection.BindingFlags]'Instance,Public,NonPublic'
$allStatic = [Reflection.BindingFlags]'Static,Public,NonPublic'
function Assert-V5([bool]$condition, [string]$detail) {
    if (-not $condition) { throw $detail }
}
function Test-Case([string]$name, [ScriptBlock]$body) {
    try {
        & $body
        $results.Add([pscustomobject]@{ Name=$name; Passed=$true; Detail='Verified.' })
        Write-Host "PASS: $name"
    } catch {
        $detail = $_.Exception.Message
        if ($_.Exception.InnerException) { $detail += ' ' + $_.Exception.InnerException.Message }
        $results.Add([pscustomobject]@{ Name=$name; Passed=$false; Detail=$detail })
        Write-Host "FAIL: $name -- $detail"
    }
}
function Get-Field($instance, [string]$name) {
    $field = $instance.GetType().GetField($name, $allInstance)
    if (-not $field) { throw "Required field $name was not found." }
    ,($field.GetValue($instance))
}
function Set-Field($instance, [string]$name, $value) {
    $field = $instance.GetType().GetField($name, $allInstance)
    if (-not $field) { throw "Required field $name was not found." }
    $field.SetValue($instance, $value)
}
function Get-Property($instance, [string]$name) {
    $property = $instance.GetType().GetProperty($name, $allInstance)
    if ($property) { ,($property.GetValue($instance, $null)) }
    else { Get-Field $instance $name }
}
function Invoke-Method($instance, [string]$name, [object[]]$arguments = @()) {
    $methods = @($instance.GetType().GetMethods($allInstance) | Where-Object { $_.Name -eq $name -and $_.GetParameters().Count -eq $arguments.Count })
    if ($methods.Count -ne 1) { throw "Expected one $name method with $($arguments.Count) arguments; found $($methods.Count)." }
    $methods[0].Invoke($instance, (Unwrap-Arguments $arguments))
}
function Unwrap-Arguments([object[]]$arguments) {
    $cleanArguments = [object[]]::new($arguments.Count)
    for ($i=0; $i -lt $arguments.Count; $i++) {
        $cleanArguments[$i] = if ($null -eq $arguments[$i]) { $null } else { $arguments[$i].PSObject.BaseObject }
    }
    ,$cleanArguments
}
function New-Internal($type, [object[]]$arguments = @()) {
    [Activator]::CreateInstance($type, $allInstance, $null, (Unwrap-Arguments $arguments), $null)
}
function Get-Controls($parent) {
    foreach ($control in $parent.Controls) {
        $control
        Get-Controls $control
    }
}
function Pump-Layout($form) {
    $form.PerformLayout()
    [Windows.Forms.Application]::DoEvents()
    $form.PerformLayout()
    [Windows.Forms.Application]::DoEvents()
}
function Remove-V5Scratch([string]$path) {
    $absolute = [IO.Path]::GetFullPath($path).TrimEnd('\')
    $parent = [IO.Directory]::GetParent($absolute).FullName.TrimEnd('\')
    $expected = [IO.Path]::GetFullPath((Join-Path $PackagePath 'test-output')).TrimEnd('\')
    if (-not [string]::Equals($parent, $expected, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($absolute) -notmatch '^GameManagementV5Test-[0-9a-f]{32}$') {
        throw "Refusing to remove unexpected test path: $absolute"
    }
    Remove-Item -LiteralPath $absolute -Recurse -Force
}

$scratch = Join-Path (Join-Path $PackagePath 'test-output') ('GameManagementV5Test-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($scratch) | Out-Null
$form = $null
$reports = $null
$overlay = $null
$overlayHost = $null
$mainType = $null
$previewMode = $null
$previewRoot = $null
$installedSettings = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments)) 'GameManagement\settings.json'
$installedHashBefore = if (Test-Path -LiteralPath $installedSettings) { (Get-FileHash -LiteralPath $installedSettings -Algorithm SHA256).Hash } else { $null }
try {
    $fixture = Get-Content -Raw -LiteralPath (Join-Path $PackagePath 'settings.json') | ConvertFrom-Json
    $fixture.gameTimerSeconds = 65
    $fixture.breakTimerSeconds = 10
    $fixture.workTimerSeconds = 3661
    $fixture.workBreakSeconds = 5
    $fixture.activeMode = 'game'
    $fixture.automaticModeDetection = $false
    [IO.File]::WriteAllText((Join-Path $scratch 'settings.json'), ($fixture | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    $historyFixture = @'
------------------------------------------------------------
SessionId: v5-work-cross-boundary
Mode: work
Applications: Editor, Notes
Started: 2026-10-10 23:30:00
Finished: 2026-10-11 02:00:00
ActiveSeconds: 5400
ActiveSegments: [{"StartedAt":"2026-10-10T23:30:00","EndedAt":"2026-10-11T00:00:00"},{"StartedAt":"2026-10-11T01:00:00","EndedAt":"2026-10-11T02:00:00"}]
Timer cycles: 2
------------------------------------------------------------
SessionId: v5-game
Mode: game
Applications: ExampleGame
Started: 2026-10-11 10:00:00
Finished: 2026-10-11 10:30:00
ActiveSeconds: 1800
Timer cycles: 1
'@
    $historyPath = Join-Path $scratch 'GameSessionHistory.txt'
    [IO.File]::WriteAllText($historyPath, $historyFixture, [Text.UTF8Encoding]::new($false))
    $assemblyPath = Join-Path $PackagePath 'GameManagement.exe'
    $assembly = [Reflection.Assembly]::LoadFile($assemblyPath)
    Test-Case 'Version 5 assembly' {
        Assert-V5 ($assembly.GetName().Version.Major -eq 5) 'The assembly must identify the new release as version 5.'
    }
    $mainType = $assembly.GetType('GameManagement.MainForm', $true)
    $previewMode = $mainType.GetField('PreviewMode', $allStatic)
    $previewRoot = $mainType.GetField('PreviewRoot', $allStatic)
    if (-not $previewMode -or -not $previewRoot) { throw 'MainForm must expose isolated preview mode and preview root before UI tests can run safely.' }
    $previewMode.SetValue($null, $true)
    $previewRoot.SetValue($null, $scratch)
    $form = New-Internal $mainType ([object[]]@($false, $false, $false))
    $form.ShowInTaskbar = $false
    $form.StartPosition = [Windows.Forms.FormStartPosition]::Manual
    $form.Location = [Drawing.Point]::new(-4000,-4000)
    $form.Show()
    Pump-Layout $form

    Test-Case 'Isolated preview has no live monitoring workers' {
        Assert-V5 ([string](Get-Field $form 'rootPath') -eq $scratch) 'Preview must read only its explicitly assigned scratch root.'
        foreach ($name in @('refreshTimer','countdownTimer','performanceTimer','foregroundModeTimer')) {
            Assert-V5 (-not (Get-Field $form $name).Enabled) "Preview must suppress the $name runtime worker."
        }
        Assert-V5 ($null -eq (Get-Field $form 'uiSignalThread')) 'Preview must not register the live application signal listener.'
    }
    Test-Case 'Saved settings load into independent timer editors' {
        Assert-V5 ((Get-Field $form 'timerMinutesInput').TotalSeconds -eq 65) 'Game seconds did not load.'
        Assert-V5 ((Get-Field $form 'breakMinutesInput').TotalSeconds -eq 10) 'Game break seconds did not load.'
        Assert-V5 ((Get-Field $form 'workTimerMinutesInput').TotalSeconds -eq 3661) 'Work hours, minutes, and seconds did not load.'
        Assert-V5 ((Get-Field $form 'workBreakMinutesInput').TotalSeconds -eq 5) 'Work break seconds did not load.'
    }
    Test-Case 'Work timer edits enable Save timers and preserve Game duration' {
        $work = Get-Field $form 'workTimerMinutesInput'
        $confirm = Get-Field $form 'confirmTimerButton'
        Assert-V5 (-not $confirm.Enabled) 'Save timers must start disabled when all timer values match saved settings.'
        $work.TotalSeconds = 3662
        Assert-V5 ($confirm.Enabled -and [bool](Invoke-Method $form 'HasUnsavedSettings')) 'Changing Work duration must enable Save timers and mark pending edits.'
        Assert-V5 ((Get-Field $form 'timerMinutesInput').TotalSeconds -eq 65) 'Work duration edits must not change Game duration.'
        $work.TotalSeconds = 3661
        Assert-V5 (-not $confirm.Enabled) 'Returning to saved Work duration must clear timer change tracking.'
        $enabled = Get-Field $form 'workTimerEnabledCheck'
        $enabled.Checked = $false
        Assert-V5 ($confirm.Enabled -and -not $work.Enabled) 'The Work enable switch must update Save timers and disable its editors.'
        $enabled.Checked = $true
        Assert-V5 (-not $confirm.Enabled -and $work.Enabled) 'Restoring the saved Work enable choice must restore its editors and saved state.'
    }
    Test-Case 'Discarding settings reloads notification options and clears pending flags' {
        foreach ($name in @('popupOption','soundOption','summaryOption')) { (Get-Field $form $name).Checked = $false }
        (Get-Field $form 'pollInput').Value = 4
        (Get-Field $form 'brightnessInput').Value = 76
        Set-Field $form 'selectionsDirty' $true
        Assert-V5 ([bool](Invoke-Method $form 'HasUnsavedSettings')) 'Options and selection changes must be recognized as pending edits.'
        Invoke-Method $form 'LoadSettings'
        foreach ($name in @('popupOption','soundOption','summaryOption')) {
            Assert-V5 ((Get-Field $form $name).Checked) "Reload must synchronize the saved $name choice."
        }
        Assert-V5 ((Get-Field $form 'pollInput').Value -eq 3 -and (Get-Field $form 'brightnessInput').Value -eq 75) 'Reload must restore saved general values.'
        Assert-V5 (-not [bool](Get-Field $form 'selectionsDirty') -and -not [bool](Invoke-Method $form 'HasUnsavedSettings')) 'Discard must clear pending settings and application-selection flags.'
    }
    Test-Case 'Automatic mode switching stops before discovery when settings are pending' {
        $sourcePath = Join-Path $PackagePath 'GameManagement.cs'
        if (-not (Test-Path -LiteralPath $sourcePath)) { $sourcePath = Join-Path $PackagePath 'Source\GameManagement.cs' }
        $source = [IO.File]::ReadAllText($sourcePath)
        $method = [regex]::Match($source,'private void CheckForegroundMode\(\)[\s\S]*?\r?\n        \}').Value
        $guardAt = $method.IndexOf('HasUnsavedSettings()',[StringComparison]::Ordinal)
        $discoveryAt = $method.IndexOf('GetForegroundExecutablePath()',[StringComparison]::Ordinal)
        Assert-V5 ($guardAt -ge 0 -and $discoveryAt -gt $guardAt) 'The automatic path must reject unsaved edits before discovering or switching the foreground application.'
    }
    Test-Case 'Game and Work preview switching preserves timers and stored mode' {
        Invoke-Method $form 'SetWorkViewForPreview' ([object[]]@($true))
        Assert-V5 ([bool](Get-Field $form 'workView')) 'Work dashboard did not become the active preview.'
        Assert-V5 ((Get-Field $form 'workTimerMinutesInput').TotalSeconds -eq 3661) 'Work switch altered saved duration.'
        Invoke-Method $form 'SetWorkViewForPreview' ([object[]]@($false))
        Assert-V5 (-not [bool](Get-Field $form 'workView')) 'Game dashboard did not become the active preview.'
        Assert-V5 ((Get-Content -Raw -LiteralPath (Join-Path $scratch 'settings.json') | ConvertFrom-Json).activeMode -eq 'game') 'Preview navigation must not save or change monitoring mode.'
    }
    Test-Case 'All five top navigation destinations are reachable' {
        foreach ($page in @('Home','Applications','Timer Settings','Reports','Options')) {
            Invoke-Method $form 'SetPageForPreview' ([object[]]@($page))
            Pump-Layout $form
            $buttons = @(Get-Controls $form | Where-Object { $_ -is [Windows.Forms.Button] -and ($_.Text -eq $page -or $_.AccessibleName -eq $page) })
            Assert-V5 ($buttons.Count -ge 1) "Navigation destination $page is missing."
            Assert-V5 (@($buttons | Where-Object { $_.Visible -and $_.TabStop }).Count -ge 1) "$page navigation must be visible and keyboard reachable."
        }
    }
    Test-Case 'Navigation keyboard shortcuts activate their destinations' {
        $pages = @('Home','Applications','Timer Settings','Reports','Options')
        for ($i=0; $i -lt $pages.Count; $i++) {
            $key = [Windows.Forms.Keys]([int][Windows.Forms.Keys]::Control -bor ([int][Windows.Forms.Keys]::D1+$i))
            $event = [Windows.Forms.KeyEventArgs]::new($key)
            Invoke-Method $form 'OnKeyDown' ([object[]]@($event))
            Assert-V5 ((Get-Field $form 'currentPage') -eq $pages[$i] -and $event.Handled) "Control+$($i+1) must activate $($pages[$i])."
        }
    }
    Test-Case 'Top navigation fits at minimum and large window sizes' {
        foreach ($size in @([Drawing.Size]::new(800,600), [Drawing.Size]::new(960,720), [Drawing.Size]::new(1440,960))) {
            $form.ClientSize = $size
            Invoke-Method $form 'SetPageForPreview' ([object[]]@('Home'))
            Pump-Layout $form
            $nav = @((Get-Field $form 'navigation').Values)
            Assert-V5 ($nav.Count -eq 5) 'Expected exactly five visible navigation buttons.'
            foreach ($button in $nav) {
                $bounds = $form.RectangleToClient($button.RectangleToScreen($button.ClientRectangle))
                Assert-V5 ($form.ClientRectangle.Contains($bounds)) "Navigation $($button.Text) is clipped at $size."
            }
            for ($i=0; $i -lt $nav.Count; $i++) {
                for ($j=$i+1; $j -lt $nav.Count; $j++) {
                    Assert-V5 (-not $nav[$i].RectangleToScreen($nav[$i].ClientRectangle).IntersectsWith($nav[$j].RectangleToScreen($nav[$j].ClientRectangle))) 'Navigation controls overlap.'
                }
            }
        }
    }

    $overlay = Get-Field $form 'overlays'
    $overlayHost = Get-Field $form 'homePage'
    Test-Case 'Calendar and Tasks independently expand and pin without shifting dashboard' {
        $originalSize = $overlayHost.Size
        Invoke-Method $overlay 'ShowForPreview' ([object[]]@('both'))
        Assert-V5 ([bool](Get-Property $overlay 'CalendarVisible') -and [bool](Get-Property $overlay 'TasksVisible')) 'Both overlays must expand independently.'
        Invoke-Method $overlay 'SetPanelPinned' ([object[]]@('calendar', $true))
        Invoke-Method $overlay 'SetPanelPinned' ([object[]]@('tasks', $false))
        Assert-V5 ([bool](Get-Property $overlay 'CalendarPinned') -and -not [bool](Get-Property $overlay 'TasksPinned')) 'Panel pins must be independent.'
        Invoke-Method $overlay 'SetPanelPinned' ([object[]]@('tasks', $true))
        Assert-V5 ([bool](Get-Property $overlay 'TasksPinned')) 'Tasks panel pin did not apply.'
        Assert-V5 ($overlayHost.Size -eq $originalSize) 'Overlay expansion must not resize the dashboard host.'
    }
    Test-Case 'Pinned overlays stay ahead of both dashboards after mode switches' {
        Invoke-Method $form 'SetPageForPreview' ([object[]]@('Home'))
        Invoke-Method $form 'SetOverlayForPreview' ([object[]]@('both'))
        foreach ($showWork in @($true,$false,$true)) {
            Invoke-Method $form 'SetWorkViewForPreview' ([object[]]@($showWork))
            Pump-Layout $form
            $surfaceName = if ($showWork) { 'workSurface' } else { 'gameSurface' }
            $surface = Get-Field $form $surfaceName
            $surfaceIndex = $overlayHost.Controls.GetChildIndex($surface)
            Assert-V5 ($surface.Visible) 'The selected dashboard must remain visible.'
            foreach ($name in @('rail','calendarPanel','tasksPanel')) {
                $panel = Get-Field $overlay $name
                Assert-V5 ($panel.Visible -and $panel.Parent -eq $overlayHost -and $overlayHost.Controls.GetChildIndex($panel) -lt $surfaceIndex) "$name must stay above the $surfaceName dashboard after a mode switch."
            }
            Assert-V5 ([bool](Get-Property $overlay 'CalendarPinned') -and [bool](Get-Property $overlay 'TasksPinned')) 'Switching dashboards must preserve both panel pins.'
        }
        Invoke-Method $form 'SetWorkViewForPreview' ([object[]]@($false))
    }
    Test-Case 'Calendar presets use the actual calendar' {
        Invoke-Method $overlay 'SelectCalendarRange' ([object[]]@('Today'))
        Assert-V5 ((Get-Property $overlay 'RangeStart').Date -eq [DateTime]::Today -and (Get-Property $overlay 'RangeEnd').Date -eq [DateTime]::Today) 'Today preset must use the actual local date.'
        Invoke-Method $overlay 'SelectCalendarRange' ([object[]]@('This Month'))
        $first = [DateTime]::new([DateTime]::Today.Year,[DateTime]::Today.Month,1)
        Assert-V5 ((Get-Property $overlay 'RangeStart').Date -eq $first -and (Get-Property $overlay 'RangeEnd').Date -eq [DateTime]::Today) 'Month-to-date preset range is incorrect.'
        Assert-V5 (-not (Get-Field $overlay 'nextMonth').Enabled) 'The source-informed calendar must not navigate beyond the current month.'
        $futureDay = @((Get-Field $overlay 'calendarDays') | Where-Object { $_.Date.Date -eq [DateTime]::Today.AddDays(1) })[0]
        Assert-V5 ($futureDay -and -not $futureDay.Enabled) 'A visible future day must be disabled.'
        $beforeStart = (Get-Property $overlay 'RangeStart').Date
        $beforeEnd = (Get-Property $overlay 'RangeEnd').Date
        Invoke-Method $overlay 'SelectCalendarDay' ([object[]]@([DateTime]::Today.AddDays(1)))
        Assert-V5 ((Get-Property $overlay 'RangeStart').Date -eq $beforeStart -and (Get-Property $overlay 'RangeEnd').Date -eq $beforeEnd) 'Future dates must stay disabled.'
        Invoke-Method $overlay 'NavigateCalendar' ([object[]]@(-1))
        Invoke-Method $overlay 'NavigateCalendar' ([object[]]@(1))
    }
    Test-Case 'Overlay hover, keyboard, focus retention, and delayed collapse' {
        Invoke-Method $overlay 'SetPanelPinned' ([object[]]@('calendar',$true))
        Invoke-Method $overlay 'SetPanelPinned' ([object[]]@('tasks',$false))
        (Get-Field $overlay 'taskTitle').Focus() | Out-Null
        Set-Field $overlay 'tasksCloseAt' ([DateTime]::UtcNow.AddSeconds(-2))
        Invoke-Method $overlay 'CloseTimerTick' ([object[]]@($null,[EventArgs]::Empty))
        Assert-V5 ([bool](Get-Property $overlay 'TasksVisible')) 'An unpinned task panel must remain open while a child has keyboard focus.'
        (Get-Field $form 'navigation')['Home'].Focus() | Out-Null
        Set-Field $overlay 'tasksCloseAt' ([DateTime]::UtcNow.AddSeconds(-2))
        Invoke-Method $overlay 'CloseTimerTick' ([object[]]@($null,[EventArgs]::Empty))
        Assert-V5 (-not [bool](Get-Property $overlay 'TasksVisible') -and [bool](Get-Property $overlay 'CalendarVisible')) 'Unpinned Tasks must collapse after its delay while pinned Calendar stays open.'
        $railButton = Get-Field $overlay 'tasksRail'
        Invoke-Method $railButton 'OnMouseEnter' ([object[]]@([EventArgs]::Empty))
        Assert-V5 ([bool](Get-Property $overlay 'TasksVisible')) 'Hover must reveal Tasks.'
        Assert-V5 ($railButton.TabStop -and -not [string]::IsNullOrEmpty($railButton.AccessibilityObject.Name)) 'The Tasks rail must also be keyboard accessible.'
        Invoke-Method $overlay 'SetPanelPinned' ([object[]]@('tasks',$true))
    }
    Test-Case 'Task records persist completion, category, and text' {
        $storeType = $assembly.GetType('GameManagement.OverlayTaskStore', $true)
        $itemType = $assembly.GetType('GameManagement.OverlayTaskItem', $true)
        $store = New-Internal $storeType ([object[]]@($scratch))
        $item = New-Internal $itemType
        $item.Id = [Guid]::NewGuid().ToString('N')
        $item.Title = 'Review score, then play'
        $item.Category = 'Personal'
        $item.Completed = $true
        $item.CreatedUtc = [DateTime]::UtcNow
        $items = Get-Property $store 'Items'
        $items.Add($item)
        Assert-V5 ([bool](Invoke-Method $store 'Save')) ('Task storage reported that saving failed: ' + (Get-Property $store 'LastError'))
        $reloaded = New-Internal $storeType ([object[]]@($scratch))
        Assert-V5 ([bool](Invoke-Method $reloaded 'Load')) 'Task storage reported that loading failed.'
        $saved = @((Get-Property $reloaded 'Items') | Where-Object Id -eq $item.Id)
        Assert-V5 ($saved.Count -eq 1 -and $saved[0].Title -eq $item.Title -and $saved[0].Category -eq 'Personal' -and $saved[0].Completed) 'Task storage must preserve category, title, identity, and completion across reload.'
        Assert-V5 (Test-Path -LiteralPath (Join-Path $scratch 'tasks.json')) 'Task file was not saved under the assigned data root.'
    }
    Test-Case 'Task UI adds, edits, completes, removes, restores, and clears persisted tasks' {
        $store = Get-Field $overlay 'taskStore'
        Assert-V5 ([bool](Invoke-Method $store 'Load')) 'The task panel must be able to reload saved task data.'
        Invoke-Method $overlay 'RenderTasks'
        (Get-Field $overlay 'taskTitle').Text = 'Practice the introduction'
        (Get-Field $overlay 'taskCategory').SelectedItem = 'Work'
        (Get-Field $overlay 'taskAdd').PerformClick()
        $added = @((Get-Property $store 'Items') | Where-Object Title -eq 'Practice the introduction')
        Assert-V5 ($added.Count -eq 1 -and $added[0].Category -eq 'Work') 'The Add control must create one task in the selected category.'
        $editState = @{ Completed=$false; Error=$null }
        $editTimer = [Windows.Forms.Timer]::new()
        $editTimer.Interval = 40
        $editTimer.add_Tick({
            $dialog = @([Windows.Forms.Application]::OpenForms | Where-Object Text -eq 'Edit task') | Select-Object -First 1
            if (-not $dialog) { return }
            $editTimer.Stop()
            try {
                @($dialog.Controls | Where-Object { $_ -is [Windows.Forms.TextBox] })[0].Text = 'Practice both hymn stanzas'
                @($dialog.Controls | Where-Object { $_ -is [Windows.Forms.ComboBox] })[0].SelectedItem = 'Game'
                @($dialog.Controls | Where-Object { $_ -is [Windows.Forms.Button] -and $_.Text -eq 'Save changes' })[0].PerformClick()
                $editState.Completed = $true
            } catch {
                $editState.Error = $_.Exception.Message
                $dialog.Close()
            }
        }.GetNewClosure())
        try {
            $editTimer.Start()
            Invoke-Method $overlay 'EditTask' ([object[]]@($added[0]))
        } finally { $editTimer.Stop(); $editTimer.Dispose() }
        Assert-V5 ($editState.Completed -and -not $editState.Error -and $added[0].Title -eq 'Practice both hymn stanzas' -and $added[0].Category -eq 'Game') ('Edit must save title and category through its actual dialog. ' + $editState.Error)
        $row = @((Get-Field $overlay 'taskList').Controls | Where-Object Tag -eq $added[0].Id)[0]
        $check = @($row.Controls | Where-Object { $_ -is [Windows.Forms.CheckBox] })[0]
        $check.Checked = $true
        Assert-V5 ($added[0].Completed -and (Get-Field $overlay 'taskProgressLabel').Text.StartsWith('2 of 2 completed')) 'Completion must update the task and visible progress.'
        $row = @((Get-Field $overlay 'taskList').Controls | Where-Object Tag -eq $added[0].Id)[0]
        @($row.ContextMenuStrip.Items | Where-Object Text -eq 'Remove')[0].PerformClick()
        Assert-V5 (@((Get-Property $store 'Items') | Where-Object Id -eq $added[0].Id).Count -eq 0) 'Remove must update task storage.'
        (Get-Field $overlay 'undoRemove').PerformClick()
        Assert-V5 (@((Get-Property $store 'Items') | Where-Object Id -eq $added[0].Id).Count -eq 1) 'Undo must restore the same task identity.'
        $verifyStore = New-Internal $assembly.GetType('GameManagement.OverlayTaskStore', $true) ([object[]]@($scratch))
        Assert-V5 ([bool](Invoke-Method $verifyStore 'Load')) 'Task UI changes must be reloadable.'
        $saved = @((Get-Property $verifyStore 'Items') | Where-Object Id -eq $added[0].Id)
        Assert-V5 ($saved.Count -eq 1 -and $saved[0].Completed -and $saved[0].Category -eq 'Game' -and $saved[0].Title -eq 'Practice both hymn stanzas') 'Task UI changes must persist across a fresh store instance.'
        (Get-Field $overlay 'taskTitle').Text = 'Keep this open'
        (Get-Field $overlay 'taskCategory').SelectedItem = 'Work'
        (Get-Field $overlay 'taskAdd').PerformClick()
        $clearState = @{ Completed=$false; Error=$null }
        $clearTimer = [Windows.Forms.Timer]::new()
        $clearTimer.Interval = 40
        $clearTimer.add_Tick({
            $dialog = @([Windows.Forms.Application]::OpenForms | Where-Object Text -eq 'Clear completed tasks') | Select-Object -First 1
            if (-not $dialog) { return }
            $clearTimer.Stop()
            try {
                $dialog.AcceptButton.PerformClick()
                $clearState.Completed = $true
            } catch {
                $clearState.Error = $_.Exception.Message
                $dialog.Close()
            }
        }.GetNewClosure())
        try {
            $clearTimer.Start()
            (Get-Field $overlay 'clearCompleted').PerformClick()
        } finally { $clearTimer.Stop(); $clearTimer.Dispose() }
        $remaining = @((Get-Property $store 'Items'))
        Assert-V5 ($clearState.Completed -and -not $clearState.Error -and $remaining.Count -eq 1 -and -not $remaining[0].Completed -and $remaining[0].Title -eq 'Keep this open') ('Clear completed must remove completed tasks and preserve open tasks. ' + $clearState.Error)
        $afterClear = New-Internal $assembly.GetType('GameManagement.OverlayTaskStore', $true) ([object[]]@($scratch))
        Assert-V5 ([bool](Invoke-Method $afterClear 'Load')) 'Cleared task data must remain reloadable.'
        Assert-V5 (@((Get-Property $afterClear 'Items')).Count -eq 1 -and -not @((Get-Property $afterClear 'Items'))[0].Completed) 'Clear completed must persist without deleting an open task.'
    }
    Test-Case 'Malformed task data remains recoverable and is not overwritten' {
        $badRoot = Join-Path $scratch 'malformed-task-data'
        [IO.Directory]::CreateDirectory($badRoot) | Out-Null
        $badPath = Join-Path $badRoot 'tasks.json'
        [IO.File]::WriteAllText($badPath,'{invalid task document',[Text.UTF8Encoding]::new($false))
        $before = (Get-FileHash -LiteralPath $badPath -Algorithm SHA256).Hash
        $badStore = New-Internal $assembly.GetType('GameManagement.OverlayTaskStore', $true) ([object[]]@($badRoot))
        Assert-V5 (-not [bool](Invoke-Method $badStore 'Load')) 'Malformed task data must report a load failure.'
        Assert-V5 (-not [bool](Invoke-Method $badStore 'Save')) 'Saving must not overwrite a task file that could not be loaded.'
        Assert-V5 ((Get-FileHash -LiteralPath $badPath -Algorithm SHA256).Hash -eq $before) 'The original malformed document must remain available for recovery.'
    }

    $reportsType = $assembly.GetType('GameManagement.ModernReportsView', $true)
    $reports = New-Internal $reportsType ([object[]]@($historyPath))
    Test-Case 'Modern reports clip active segments and exclude paused gaps' {
        $reportType = $assembly.GetType('GameManagement.WeeklyReport', $true)
        $load = $reportType.GetMethod('Load', $allStatic)
        $sessions = $load.Invoke($null, (Unwrap-Arguments ([object[]]@($historyPath))))
        Assert-V5 ($sessions.Count -eq 2) 'The existing history format must remain readable.'
        $activeInRange = $reportsType.GetMethod('ActiveInRange', $allStatic)
        $work = @($sessions | Where-Object Mode -eq 'work')[0]
        $time = [TimeSpan]$activeInRange.Invoke($null, (Unwrap-Arguments ([object[]]@($work, [DateTime]'2026-10-11', [DateTime]'2026-10-12'))))
        Assert-V5 ($time.TotalSeconds -eq 3600) 'Reports must include 1 hour of active work on October 11 and exclude the paused midnight-to-1am gap.'
    }
    Test-Case 'Report period and search controls expose real fixture data' {
        $period = Get-Field $reports 'period'
        Assert-V5 ($period.Items.Count -eq 5) 'Reports must offer daily, weekly, monthly, all-time, and custom ranges.'
        $period.SelectedIndex = 3
        $grid = Get-Field $reports 'history'
        Assert-V5 ($grid.Rows.Count -eq 2) 'All-time history must list both saved fixture sessions.'
        (Get-Field $reports 'search').Text = 'ExampleGame'
        Assert-V5 ($grid.Rows.Count -eq 1 -and [string]$grid.Rows[0].Cells[2].Value -eq 'ExampleGame') 'Searching history must filter actual saved application data.'
        (Get-Field $reports 'search').Text = ''
        Assert-V5 ($grid.Rows.Count -eq 2) 'Clearing search must restore history rows.'
        Assert-V5 ([string](Get-Field $reports 'cyclesValue').Text -eq '3') 'Reports must total the fixture completed cycles.'
    }
    Test-Case 'CSV export preserves actual values and quoted application names' {
        $csv = [string](Invoke-Method $reports 'BuildCsv')
        $rows = @($csv | ConvertFrom-Csv)
        Assert-V5 ($rows.Count -eq 2) 'All-time export must include both saved sessions.'
        $work = @($rows | Where-Object Mode -eq 'work')[0]
        Assert-V5 ($work.Application -eq 'Editor, Notes' -and [double]$work.'Active seconds in range' -eq 5400 -and [int]$work.'Completed cycles' -eq 2) 'CSV must preserve quoted commas, active time, and completed cycles.'
        (Get-Field $reports 'search').Text = 'ExampleGame'
        $filtered = @(([string](Invoke-Method $reports 'BuildCsv')) | ConvertFrom-Csv)
        Assert-V5 ($filtered.Count -eq 1 -and $filtered[0].Application -eq 'ExampleGame') 'CSV export must respect the selected history search.'
        (Get-Field $reports 'search').Text = ''
    }
    Test-Case 'Timer notification preview renders with a keyboard accessible stop control' {
        $alertType = $assembly.GetType('GameManagement.TimerAlertForm', $true)
        $alert = New-Internal $alertType ([object[]]@('work-finished','30','5',$true,$false))
        try {
            $alert.Show()
            Pump-Layout $alert
            $stop = @(Get-Controls $alert | Where-Object { $_ -is [Windows.Forms.Button] -and $_.Text -eq 'Stop alarm' })
            Assert-V5 ($stop.Count -eq 1 -and $stop[0].TabStop -and $alert.CancelButton -eq $stop[0] -and -not [string]::IsNullOrEmpty($stop[0].AccessibilityObject.Name)) 'Alarm must expose exactly one keyboard accessible Stop alarm action and bind it to Escape.'
            $bitmap = [Drawing.Bitmap]::new($alert.Width,$alert.Height)
            try { $alert.DrawToBitmap($bitmap,[Drawing.Rectangle]::new(0,0,$bitmap.Width,$bitmap.Height)) }
            finally { $bitmap.Dispose() }
            $stop[0].PerformClick()
        } finally { $alert.Dispose() }
    }
} catch {
    $results.Add([pscustomobject]@{Name='UI harness initialization';Passed=$false;Detail=$_.Exception.ToString()})
} finally {
    if ($reports) { $reports.Dispose() }
    if ($overlay) { $overlay.Dispose() }
    if ($overlayHost) { $overlayHost.Dispose() }
    if ($form) { $form.Dispose() }
    if ($previewMode) { $previewMode.SetValue($null, $false) }
    if ($previewRoot) { $previewRoot.SetValue($null, $null) }
    Test-Case 'Preview leaves installed settings unchanged' {
        $after = if (Test-Path -LiteralPath $installedSettings) { (Get-FileHash -LiteralPath $installedSettings -Algorithm SHA256).Hash } else { $null }
        Assert-V5 ($after -eq $installedHashBefore) 'UI verification altered installed settings.'
    }
    if (Test-Path -LiteralPath $scratch) { Remove-V5Scratch $scratch }
}

$failures = @($results | Where-Object { -not $_.Passed })
$status = if ($failures.Count) { 'FAIL' } else { 'PASS' }
$verifiedHash = (Get-FileHash -LiteralPath (Join-Path $PackagePath 'GameManagement.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
$lines = @('# Game Management v5 verification', '', "Result: **$status**. $($results.Count-$failures.Count) passed, $($failures.Count) failed.", '',
    "Verified executable SHA-256: $verifiedHash", '',
    'The UI harness uses a temporary preview data root. It does not enable or pause monitoring, change startup, or operate the live installed application.', '',
    '| Check | Result | Evidence |', '|---|---|---|')
foreach ($result in $results) {
    $label = if ($result.Passed) { 'PASS' } else { 'FAIL' }
    $detail = ($result.Detail -replace '[\r\n]+',' ') -replace '\|','\|'
    $lines += "| $($result.Name) | $label | $detail |"
}
$lines += @('', 'Behavioral regression coverage is reported separately by the feature and timer lifecycle suites. Manual visual inspection, real hardware sensor availability, and display scaling on other monitors remain outside this automated UI harness.')
[IO.File]::WriteAllLines($ReportPath, $lines, [Text.UTF8Encoding]::new($false))
[pscustomobject]@{ Overall=$status; Passed=$results.Count-$failures.Count; Failed=$failures.Count; Report=$ReportPath }
if ($failures.Count) { throw "$($failures.Count) v5 checks failed; inspect $ReportPath." }

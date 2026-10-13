param([string]$PackagePath = (Split-Path -Parent $MyInvocation.MyCommand.Path), [switch]$FullApplication)
$ErrorActionPreference = 'Stop'
if ((Split-Path -Leaf $PackagePath) -eq 'Tests') { $PackagePath=Split-Path -Parent $PackagePath }
$sourcePath = if (Test-Path -LiteralPath (Join-Path $PackagePath 'Source\ModernPanels.cs')) { Join-Path $PackagePath 'Source' } else { $PackagePath }
if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne [Threading.ApartmentState]::STA) { throw 'Run panel checks with Windows PowerShell -STA.' }
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$flags = [Reflection.BindingFlags]'Public,NonPublic,Instance'
$scratch = Join-Path (Join-Path $PackagePath 'test-output') ('PanelsIntegration-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($scratch) | Out-Null
$form = $null
$panels = $null
$mainForm = $null
$passes = 0
function Get-PanelField($object,[string]$name) { ,$object.GetType().GetField($name,$flags).GetValue($object) }
function Get-PanelDescendants($parent) { foreach ($control in $parent.Controls) { $control; Get-PanelDescendants $control } }
function Invoke-PanelMethod($object,[string]$name,[object[]]$arguments=@()) {
    $method = @($object.GetType().GetMethods($flags) | Where-Object { $_.Name -eq $name -and $_.GetParameters().Count -eq $arguments.Count })[0]
    $method.Invoke($object,(Unwrap-PanelArguments $arguments))
}
function Unwrap-PanelArguments([object[]]$arguments) {
    $unwrapped = [object[]]::new($arguments.Count)
    for ($i=0; $i -lt $arguments.Count; $i++) { $unwrapped[$i] = if ($null -eq $arguments[$i]) { $null } else { $arguments[$i].PSObject.BaseObject } }
    ,$unwrapped
}
function New-PanelObject($type,[object[]]$arguments) { [Activator]::CreateInstance($type,$flags,$null,(Unwrap-PanelArguments $arguments),$null) }
function Check-Panel([bool]$condition,[string]$name) { if (-not $condition) { throw $name }; $script:passes++; Write-Host ('PASS: ' + $name) }
function Pump-Panels([int]$milliseconds=250) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.ElapsedMilliseconds -lt $milliseconds) { [Windows.Forms.Application]::DoEvents(); [Threading.Thread]::Sleep(10) }
}
try {
    # This isolated harness instantiates only the overlay controls; it has no timer watcher or production state.
    $dll = Join-Path $scratch 'PanelControls.dll'
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    & $compiler /nologo /target:library /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll ('/out:' + $dll) (Join-Path $sourcePath 'ModernDesign.cs') (Join-Path $sourcePath 'ModernPanels.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Isolated panel compilation failed.' }
    $assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($dll))
    $type = $assembly.GetType('GameManagement.OverlayPanels',$true)
    $form = [Windows.Forms.Form]::new()
    $form.ClientSize = [Drawing.Size]::new(1100,820)
    $form.ShowInTaskbar = $false
    $form.StartPosition = 'Manual'
    $form.Location = [Drawing.Point]::new(-4000,-4000)
    $hostPanel = [Windows.Forms.Panel]::new()
    $hostPanel.Dock = 'Fill'
    $form.Controls.Add($hostPanel)
    $previewData = Join-Path $scratch 'preview'
    $panels = New-PanelObject $type ([object[]]@($hostPanel,$previewData,$false))
    $form.Show()
    $originalBounds = $hostPanel.Bounds
    Invoke-PanelMethod $panels 'ShowForPreview' @('both')
    Pump-Panels
    $calendar = Get-PanelField $panels 'calendarPanel'
    $tasks = Get-PanelField $panels 'tasksPanel'
    Check-Panel ($calendar.Left -eq $tasks.Left -and $calendar.Width -eq 280 -and $tasks.Top -ge $calendar.Bottom) 'Calendar and Tasks form one narrow stacked overlay column'
    Check-Panel ($tasks.Bottom -le $hostPanel.ClientSize.Height -and $hostPanel.Bounds -eq $originalBounds) 'Pinned overlays fit the desktop without shifting dashboard bounds'
    Check-Panel (-not (Test-Path -LiteralPath (Join-Path $previewData 'panel-preferences.json'))) 'Preview pinning never writes preferences'
    Check-Panel (-not (Get-PanelField $panels 'rangeFrom').Visible) 'Preset calendars keep custom date editors compact'
    $rangeState = @{ Count=0; Start=$null; End=$null }
    $rangeHandler = [Action[datetime,datetime]]{ param($start,$end) $rangeState.Count++; $rangeState.Start=$start; $rangeState.End=$end }.GetNewClosure()
    $panels.add_RangeChanged($rangeHandler)
    Check-Panel ($rangeState.Count -eq 0) 'Subscribing to Calendar emits no synthetic initial selection'
    Invoke-PanelMethod $panels 'SelectCalendarRange' @('This Month')
    Check-Panel ($rangeState.Count -eq 1 -and $rangeState.Start.Day -eq 1 -and $rangeState.End -ge $rangeState.Start) 'Real preset selections emit the report date range'
    Invoke-PanelMethod $panels 'SelectCalendarRange' @('Custom')
    Check-Panel ((Get-PanelField $panels 'rangeFrom').Visible -and $rangeState.Count -eq 1) 'Custom reveals native editors without changing the report range'
    (Get-PanelField $panels 'rangeFrom').Value = [datetime]::Today.AddDays(-2)
    Check-Panel ($rangeState.Count -eq 2 -and $rangeState.Start -eq [datetime]::Today.AddDays(-2)) 'Native custom date edits emit actual dates'
    $reportState = @{ Count=0 }
    $reportHandler = [Action]{ $reportState.Count++ }.GetNewClosure()
    $panels.add_ViewReportsRequested($reportHandler)
    (Get-PanelField $panels 'viewReports').PerformClick()
    Check-Panel ($reportState.Count -eq 1) 'View reports is a working accessible action'
    (Get-PanelField $panels 'taskTitle').Focus() | Out-Null
    Invoke-PanelMethod $panels 'SetPanelPinned' @('tasks',$false)
    $panels.GetType().GetField('tasksCloseAt',$flags).SetValue($panels,[datetime]::UtcNow.AddSeconds(-2))
    Invoke-PanelMethod $panels 'CloseTimerTick' @($null,[EventArgs]::Empty)
    Check-Panel ($panels.TasksVisible -and (Get-PanelField $panels 'taskTitle').ContainsFocus) 'Focused task input remains open while unpinned'
    (Get-PanelField $panels 'taskTitle').Text='Verify keyboard completion'
    (Get-PanelField $panels 'taskAdd').PerformClick()
    $taskRow=@((Get-PanelField $panels 'taskList').Controls | Where-Object { $_.Tag -ne 'empty' })[0]
    $completeCheck=@($taskRow.Controls | Where-Object Tag -eq 'check')[0]
    $completeCheck.Focus() | Out-Null
    $completeCheck.Checked=$true
    $taskRow=@((Get-PanelField $panels 'taskList').Controls | Where-Object { $_.Tag -ne 'empty' })[0]
    $completeCheck=@($taskRow.Controls | Where-Object Tag -eq 'check')[0]
    Check-Panel ($completeCheck.Checked -and $completeCheck.ContainsFocus -and $rangeState.Count -eq 2) 'Task completion retains keyboard focus and leaves Calendar selection unchanged'
    (Get-PanelField $panels 'previousMonth').Focus() | Out-Null
    $panels.GetType().GetField('tasksCloseAt',$flags).SetValue($panels,[datetime]::UtcNow.AddSeconds(-2))
    Invoke-PanelMethod $panels 'CloseTimerTick' @($null,[EventArgs]::Empty)
    Pump-Panels
    Check-Panel (-not $panels.TasksVisible -and -not $tasks.Visible -and $panels.CalendarVisible) 'Unpinned Tasks settles closed after the pointer and keyboard leave'
    Invoke-PanelMethod (Get-PanelField $panels 'tasksRail') 'OnMouseEnter' @([EventArgs]::Empty)
    Pump-Panels
    Check-Panel ($panels.TasksVisible -and $tasks.Visible -and $null -eq $tasks.Region) 'Hover reveal completes with fully interactive native controls'
    $form.ClientSize = [Drawing.Size]::new(800,400)
    Pump-Panels
    Check-Panel ($tasks.Bottom -le $hostPanel.ClientSize.Height -and $calendar.AutoScroll -and $tasks.AutoScroll) 'Short windows preserve access through scrolling overlays'
    $shortTaskList=Get-PanelField $panels 'taskList'
    if ($tasks.HorizontalScroll.Visible -or $shortTaskList.HorizontalScroll.Visible) {
        Write-Host ("Outer $($tasks.Size) client $($tasks.ClientSize) min $($tasks.AutoScrollMinSize) H $($tasks.HorizontalScroll.Visible) V $($tasks.VerticalScroll.Visible)")
        Write-Host ("Inner $($shortTaskList.Size) client $($shortTaskList.ClientSize) min $($shortTaskList.AutoScrollMinSize) H $($shortTaskList.HorizontalScroll.Visible) V $($shortTaskList.VerticalScroll.Visible)")
        foreach($child in $tasks.Controls) { if ($child.Right -gt $tasks.ClientSize.Width) { Write-Host ("Overflow $($child.GetType().Name) $($child.Tag) $($child.Bounds)") } }
        foreach($child in $shortTaskList.Controls) { Write-Host ("Row $($child.Bounds)") }
    }
    Check-Panel (-not $tasks.HorizontalScroll.Visible -and -not $shortTaskList.HorizontalScroll.Visible) 'Short overlay windows avoid horizontal task scrolling'
    $form.ClientSize = [Drawing.Size]::new(1100,820)
    Invoke-PanelMethod $panels 'SetPanelPinned' @('tasks',$true)
    Invoke-PanelMethod $panels 'SelectCalendarRange' @('Today')
    Pump-Panels
    $image = [Drawing.Bitmap]::new($form.ClientSize.Width,$form.ClientSize.Height)
    try {
        $form.DrawToBitmap($image,[Drawing.Rectangle]::new(0,0,$image.Width,$image.Height))
        $image.Save((Join-Path $PackagePath 'test-output\panels-stacked-preview.png'),[Drawing.Imaging.ImageFormat]::Png)
    } finally { $image.Dispose() }
    $panels.Dispose(); $panels=$null

    $persistData = Join-Path $scratch 'persistent'
    $panels = New-PanelObject $type ([object[]]@($hostPanel,$persistData,$true))
    Invoke-PanelMethod $panels 'SetPanelPinned' @('calendar',$true)
    Invoke-PanelMethod $panels 'SetPanelPinned' @('tasks',$true)
    $preferencePath = Join-Path $persistData 'panel-preferences.json'
    Check-Panel ((Test-Path -LiteralPath $preferencePath) -and (Test-Path -LiteralPath ($preferencePath + '.bak'))) 'Pin preferences use atomic replacement with a recovery backup'
    $panels.Dispose(); $panels=$null
    $panels = New-PanelObject $type ([object[]]@($hostPanel,$persistData))
    Check-Panel ($panels.CalendarPinned -and $panels.TasksPinned -and $panels.CalendarVisible -and $panels.TasksVisible) 'Both pins restore across a new overlay instance'
    $panels.Dispose(); $panels=$null
    [IO.File]::WriteAllText($preferencePath,'{invalid preferences',[Text.UTF8Encoding]::new($false))
    $hash = (Get-FileHash -LiteralPath $preferencePath -Algorithm SHA256).Hash
    $panels = New-PanelObject $type ([object[]]@($hostPanel,$persistData))
    Invoke-PanelMethod $panels 'SetPanelPinned' @('calendar',$true)
    $preferenceStore = Get-PanelField $panels 'panelPreferences'
    Check-Panel ((Get-FileHash -LiteralPath $preferencePath -Algorithm SHA256).Hash -eq $hash -and -not [string]::IsNullOrEmpty($preferenceStore.LastError)) 'Malformed pin preferences remain untouched and report recovery status'
    if ($FullApplication) {
        $fullAssembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $PackagePath 'GameManagement.exe')))
        $mainType = $fullAssembly.GetType('GameManagement.MainForm',$true)
        $staticFlags = [Reflection.BindingFlags]'Public,NonPublic,Static'
        $mainType.GetField('PreviewMode',$staticFlags).SetValue($null,$true)
        $mainRoot = Join-Path $scratch 'full-application'
        [IO.Directory]::CreateDirectory($mainRoot) | Out-Null
        Copy-Item -LiteralPath (Join-Path $PackagePath 'settings.json') -Destination (Join-Path $mainRoot 'settings.json')
        $mainType.GetField('PreviewRoot',$staticFlags).SetValue($null,$mainRoot)
        $mainForm = New-PanelObject $mainType ([object[]]@($false,$false,$false))
        $mainForm.ShowInTaskbar=$false
        $mainForm.StartPosition='Manual'
        $mainForm.Location=[Drawing.Point]::new(-4000,-4000)
        $mainForm.Show()
        foreach ($size in @([Drawing.Size]::new(1280,880),[Drawing.Size]::new(800,600))) {
            $mainForm.ClientSize=$size
            Invoke-PanelMethod $mainForm 'SetPageForPreview' @('Home')
            Invoke-PanelMethod $mainForm 'SetOverlayForPreview' @('both')
            Invoke-PanelMethod $mainForm 'SetWorkViewForPreview' @($true)
            Pump-Panels
            $mainPanels=Get-PanelField $mainForm 'overlays'
            $mainHost=Get-PanelField $mainForm 'homePage'
            $mainSurface=Get-PanelField $mainForm 'workSurface'
            foreach ($name in @('rail','calendarPanel','tasksPanel')) {
                $control=Get-PanelField $mainPanels $name
                Write-Host ("$name at $($size.Width): visible=$($control.Visible) bounds=$($control.Bounds) child=$($mainHost.Controls.GetChildIndex($control))")
                Check-Panel ($control.Visible -and $mainHost.Controls.GetChildIndex($control) -lt $mainHost.Controls.GetChildIndex($mainSurface)) "Full application keeps $name over the work dashboard at $($size.Width)"
            }
            $fullTaskList=Get-PanelField $mainPanels 'taskList'
            Check-Panel (-not $fullTaskList.HorizontalScroll.Visible -and -not (Get-PanelField $mainPanels 'tasksPanel').HorizontalScroll.Visible) "Task scrolling stays vertical at $($size.Width)"
            $capture=[Drawing.Bitmap]::new($mainForm.Width,$mainForm.Height)
            try {
                $mainForm.DrawToBitmap($capture,[Drawing.Rectangle]::new(0,0,$capture.Width,$capture.Height))
                $overlayGraphics=[Drawing.Graphics]::FromImage($capture)
                try { Invoke-PanelMethod $mainPanels 'DrawPreviewOverlay' @($overlayGraphics,$mainForm) }
                finally { $overlayGraphics.Dispose() }
                $capture.Save((Join-Path $PackagePath ("test-output\integration-panels-$($size.Width)-isolated.png")),[Drawing.Imaging.ImageFormat]::Png)
            } finally { $capture.Dispose() }
        }
        $mainForm.Dispose(); $mainForm=$null
        foreach ($factor in @(1.25,1.5)) {
            $mainForm=New-PanelObject $mainType ([object[]]@($false,$false,$false))
            $mainForm.ShowInTaskbar=$false
            $mainForm.StartPosition='Manual'
            $mainForm.Location=[Drawing.Point]::new(-4000,-4000)
            $mainForm.Show()
            $mainForm.AutoScaleMode='None'
            $mainForm.SuspendLayout()
            $fontSnapshot=@(@($mainForm)+@(Get-PanelDescendants $mainForm) | ForEach-Object { [pscustomobject]@{Control=$_;Font=$_.Font} })
            $mainForm.Scale([Drawing.SizeF]::new($factor,$factor))
            foreach ($entry in $fontSnapshot) { $entry.Control.Font=[Drawing.Font]::new($entry.Font.FontFamily,[single]($entry.Font.Size*$factor),$entry.Font.Style,$entry.Font.Unit) }
            $mainForm.ClientSize=[Drawing.Size]::new([int](1280*$factor),[int](880*$factor))
            $mainForm.ResumeLayout($true)
            Invoke-PanelMethod $mainForm 'SetOverlayForPreview' @('both')
            Invoke-PanelMethod $mainForm 'SetWorkViewForPreview' @($true)
            Pump-Panels
            $mainPanels=Get-PanelField $mainForm 'overlays'
            $mainCalendar=Get-PanelField $mainPanels 'calendarPanel'
            $mainTasks=Get-PanelField $mainPanels 'tasksPanel'
            $mainHost=Get-PanelField $mainForm 'homePage'
            $percent=[int]($factor*100)
            Check-Panel ($mainCalendar.Width -eq [int](280*$factor) -and $mainCalendar.Left -eq $mainTasks.Left -and $mainTasks.Top -gt $mainCalendar.Bottom -and $mainHost.ClientRectangle.Contains($mainTasks.Bounds)) "Overlay column and rail respond to $percent percent scaling"
            foreach ($filterName in @('rangeButtons','categoryButtons')) {
                foreach ($button in (Get-PanelField $mainPanels $filterName)) {
                    $textSize=[Windows.Forms.TextRenderer]::MeasureText($button.Text,$button.Font,[Drawing.Size]::new(1000,200),[Windows.Forms.TextFormatFlags]::NoPadding)
                    if ($textSize.Width -gt $button.Width-[int](8*$factor) -or $textSize.Height -gt $button.Height) { Write-Host "Clipped filter: $($button.Text), text=$textSize, bounds=$($button.Bounds), font=$($button.Font.SizeInPoints)" }
                    Check-Panel ($textSize.Width -le $button.Width-[int](8*$factor) -and $textSize.Height -le $button.Height) "Compact $($button.Text) label fits at $percent percent scaling"
                }
            }
            $titleControl=Get-PanelField $mainPanels 'taskTitle'
            $titleControl.Text='Scaled task focus'
            (Get-PanelField $mainPanels 'taskAdd').PerformClick()
            $scaledRow=@((Get-PanelField $mainPanels 'taskList').Controls | Where-Object { $_.Tag -ne 'empty' })[0]
            $scaledTitle=@($scaledRow.Controls | Where-Object Tag -eq 'title')[0]
            Check-Panel ([Math]::Abs($scaledTitle.Font.SizeInPoints-(9*$factor)) -lt 0.1 -and $scaledRow.Height -eq [int](36*$factor)) "New task rows retain $percent percent scaling"
            $capture=[Drawing.Bitmap]::new($mainForm.Width,$mainForm.Height)
            try {
                $mainForm.DrawToBitmap($capture,[Drawing.Rectangle]::new(0,0,$capture.Width,$capture.Height))
                $overlayGraphics=[Drawing.Graphics]::FromImage($capture)
                try { Invoke-PanelMethod $mainPanels 'DrawPreviewOverlay' @($overlayGraphics,$mainForm) }
                finally { $overlayGraphics.Dispose() }
                $capture.Save((Join-Path $PackagePath ("test-output\integration-panels-$percent-percent.png")),[Drawing.Imaging.ImageFormat]::Png)
            } finally { $capture.Dispose() }
            $mainForm.Dispose(); $mainForm=$null
        }
    }
    Write-Host ("Panel integration checks passed: $passes")
} finally {
    if ($mainForm) { $mainForm.Dispose() }
    if ($panels) { $panels.Dispose() }
    if ($form) { $form.Dispose() }
    $absolute = [IO.Path]::GetFullPath($scratch).TrimEnd('\')
    $expectedParent = [IO.Path]::GetFullPath((Join-Path $PackagePath 'test-output')).TrimEnd('\')
    if ([IO.Directory]::GetParent($absolute).FullName.TrimEnd('\') -ne $expectedParent -or [IO.Path]::GetFileName($absolute) -notmatch '^PanelsIntegration-[0-9a-f]{32}$') { throw "Unexpected test cleanup path: $absolute" }
    Remove-Item -LiteralPath $absolute -Recurse -Force
}

param([string]$PackagePath = (Split-Path -Parent $MyInvocation.MyCommand.Path))
$ErrorActionPreference = 'Stop'
if ((Split-Path -Leaf $PackagePath) -eq 'Source') { $PackagePath = Split-Path -Parent $PackagePath }
$package = [IO.Path]::GetFullPath($PackagePath)
if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne [Threading.ApartmentState]::STA) {
    throw 'Build and UI verification require Windows PowerShell -STA.'
}
function Resolve-BuildFile([string]$name) {
    foreach ($folder in @('', 'Source', 'Tests', 'Documentation', 'Documentation\Reports')) {
        $candidate = if ($folder) { Join-Path (Join-Path $package $folder) $name } else { Join-Path $package $name }
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }
    throw "Missing build input: $name"
}
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The Windows .NET Framework C# compiler was not found.' }
$compileArguments = @(
    '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+',
    ('/win32icon:' + (Resolve-BuildFile 'GameManagement.ico')),
    '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll', '/reference:System.Management.dll',
    '/reference:System.Web.Extensions.dll', '/reference:System.IO.Compression.dll',
    '/reference:System.IO.Compression.FileSystem.dll',
    ('/out:' + (Join-Path $package 'GameManagement.exe'))
)
foreach ($name in @('GameManagement.cs','ModernDesign.cs','ModernDesktop.cs','ModernHome.cs','ModernAlerts.cs','ModernSessionOverview.cs','ModernPanels.cs','ModernReports.cs')) {
    $compileArguments += Resolve-BuildFile $name
}
& $compiler @compileArguments
if ($LASTEXITCODE -ne 0) { throw "Application compilation failed with exit code $LASTEXITCODE." }
$frontendRoot = Join-Path $package 'frontend'
$frontendManifest = Join-Path $frontendRoot 'package.json'
if (Test-Path -LiteralPath $frontendManifest) {
    $pnpm = Get-Command pnpm -ErrorAction Stop
    & $pnpm.Source --dir $frontendRoot build
    if ($LASTEXITCODE -ne 0) { throw "React frontend build failed with exit code $LASTEXITCODE." }
    & $pnpm.Source --dir $frontendRoot 'test:history'
    if ($LASTEXITCODE -ne 0) { throw "React history regression tests failed with exit code $LASTEXITCODE." }
    & $pnpm.Source --dir $frontendRoot 'test:polling'
    if ($LASTEXITCODE -ne 0) { throw "React polling regression tests failed with exit code $LASTEXITCODE." }
    & (Join-Path $package 'bridge\Build-ReactDesktopHost.ps1') -ProjectRoot $package
    if ($LASTEXITCODE -ne 0) { throw 'The React desktop host build did not complete.' }
} elseif (-not (Test-Path -LiteralPath (Join-Path $package 'GameManagement.Desktop.exe')) -or
          -not (Test-Path -LiteralPath (Join-Path $frontendRoot 'dist\index.html'))) {
    throw 'The React source is unavailable and the prebuilt desktop interface is incomplete.'
}
$reportRoot = if (Test-Path -LiteralPath (Join-Path $package 'GameManagement.cs')) { $package } else { Join-Path $package 'Documentation\Reports' }
[IO.Directory]::CreateDirectory($reportRoot) | Out-Null
$suites = [ordered]@{
    'Test-GameManagementSecurity.ps1' = 'SECURITY-REPORT.md'
    'Test-GameManagementFeatures.ps1' = 'FEATURE-TEST-REPORT.md'
    'Test-GameManagementTimerLifecycle.ps1' = 'TIMER-LIFECYCLE-TEST-REPORT.md'
    'Test-GameManagementV5.ps1' = 'V5-TEST-REPORT.md'
    'Test-GameManagementIntegration.ps1' = 'INTEGRATION-TEST-REPORT.md'
}
foreach ($name in $suites.Keys) {
    & (Resolve-BuildFile $name) -PackagePath $package -ReportPath (Join-Path $reportRoot $suites[$name])
    if ($LASTEXITCODE -ne 0) { throw "Verification failed: $name" }
}
& (Resolve-BuildFile 'Test-PanelsIntegration.ps1') -PackagePath $package -FullApplication
if ($LASTEXITCODE -ne 0) { throw 'The full application panel checks did not complete.' }
& (Resolve-BuildFile 'Build-GameManagementInstaller.ps1') -PackagePath $package
if ($LASTEXITCODE -ne 0) { throw 'The installer build did not complete.' }

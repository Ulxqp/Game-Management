param([string]$PackagePath = (Split-Path -Parent $MyInvocation.MyCommand.Path))
$ErrorActionPreference = 'Stop'
if ((Split-Path -Leaf $PackagePath) -eq 'Source') { $PackagePath = Split-Path -Parent $PackagePath }
$package = [IO.Path]::GetFullPath($PackagePath)
$specialPayloads = @{
    'frontend-index.html' = 'frontend\dist\index.html'
    'frontend-app.js' = 'frontend\dist\assets\app.js'
    'frontend-app.css' = 'frontend\dist\assets\app.css'
}
function Resolve-InstallerFile([string]$name) {
    if ($specialPayloads.ContainsKey($name)) {
        $specialCandidate = Join-Path $package $specialPayloads[$name]
        if (Test-Path -LiteralPath $specialCandidate -PathType Leaf) { return $specialCandidate }
        throw "Missing installer payload: $name ($specialCandidate)"
    }
    foreach ($folder in @('', 'Source', 'Tests', 'Documentation', 'Documentation\Reports')) {
        $candidate = if ($folder) { Join-Path (Join-Path $package $folder) $name } else { Join-Path $package $name }
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }
    throw "Missing installer payload: $name"
}
$distDirectory = Join-Path $package 'dist'
[IO.Directory]::CreateDirectory($distDirectory) | Out-Null
$output = Join-Path $distDirectory 'GameManagement-Setup.exe'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$payloads = @(
    'GameManagement.exe',
    'GameManagement.Desktop.exe',
    'Microsoft.Web.WebView2.Core.dll',
    'Microsoft.Web.WebView2.WinForms.dll',
    'WebView2Loader.dll',
    'Microsoft.Web.WebView2.LICENSE.txt',
    'Microsoft.Web.WebView2.NOTICE.txt',
    'frontend-index.html',
    'frontend-app.js',
    'frontend-app.css',
    'GameManagement.cs',
    'ModernDesign.cs',
    'ModernDesktop.cs',
    'ModernHome.cs',
    'ModernAlerts.cs',
    'ModernSessionOverview.cs',
    'ModernPanels.cs',
    'ModernReports.cs',
    'GameManagementSetup.cs',
    'Build-GameManagement.ps1',
    'Build-GameManagementInstaller.ps1',
    'Build-GameManagementRelease.ps1',
    'GameManagement.ico',
    'GameManagement.ps1',
    'Install-GameManagement.ps1',
    'Pause-GameManagement.ps1',
    'Undo-GameManagement.ps1',
    'Uninstall-GameManagement.ps1',
    'Test-GameManagementSecurity.ps1',
    'Test-GameManagementFeatures.ps1',
    'Test-GameManagementTimerLifecycle.ps1',
    'Test-GameManagementV5.ps1',
    'Test-GameManagementIntegration.ps1',
    'Test-PanelsIntegration.ps1',
    'README.txt',
    'README.md',
    'AGENTS.md',
    'CHANGELOG.md',
    'DESIGN-SYSTEM.md',
    'V5-IMPLEMENTATION.md',
    'V5-TEST-REPORT.md',
    'COMPONENT-REFERENCES.md',
    'INTEGRATION-TEST-REPORT.md',
    'SECURITY-REPORT.md',
    'FEATURE-TEST-REPORT.md',
    'TIMER-LIFECYCLE-TEST-REPORT.md',
    'MODE-SWITCH-TEST-REPORT.md',
    'TEMPERATURE-GUIDE.md',
    'TIMER-STATE-GRAPH.md',
    'settings.json'
)

if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The Windows .NET Framework C# compiler was not found.'
}

$arguments = @(
    '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+',
    ('/win32icon:' + (Resolve-InstallerFile 'GameManagement.ico')),
    '/reference:System.dll', '/reference:System.Core.dll',
    '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll',
    '/reference:System.Management.dll',
    ('/out:' + $output)
)
foreach ($name in $payloads) {
    $path = Resolve-InstallerFile $name
    $arguments += ('/resource:' + $path + ',Payload.' + $name)
}
$arguments += (Resolve-InstallerFile 'GameManagementSetup.cs')
$arguments += (Resolve-InstallerFile 'ModernDesign.cs')

& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed with exit code $LASTEXITCODE." }

$verification = Start-Process -FilePath $output -ArgumentList '--verify' -WindowStyle Hidden -Wait -PassThru
if ($verification.ExitCode -ne 0) { throw 'The completed installer failed payload verification.' }

$compatibilityTest = Start-Process -FilePath $output -ArgumentList '--compatibility-test' -WindowStyle Hidden -Wait -PassThru
if ($compatibilityTest.ExitCode -ne 0) { throw 'The completed installer failed RTX 20/30 compatibility classification tests.' }

Write-Host "Created and verified universal installer: $output" -ForegroundColor Green

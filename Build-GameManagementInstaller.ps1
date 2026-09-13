$ErrorActionPreference = 'Stop'
$package = Split-Path -Parent $MyInvocation.MyCommand.Path
$output = Join-Path (Split-Path -Parent $package) 'GameManagement-Setup.exe'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$payloads = @(
    'GameManagement.exe',
    'GameManagement.cs',
    'GameManagement.ico',
    'GameManagement.ps1',
    'Install-GameManagement.ps1',
    'Pause-GameManagement.ps1',
    'Undo-GameManagement.ps1',
    'Uninstall-GameManagement.ps1',
    'Test-GameManagementSecurity.ps1',
    'README.txt',
    'TEMPERATURE-GUIDE.md',
    'settings.json'
)

if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The Windows .NET Framework C# compiler was not found.'
}

$arguments = @(
    '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+',
    ('/win32icon:' + (Join-Path $package 'GameManagement.ico')),
    '/reference:System.dll', '/reference:System.Core.dll',
    '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll',
    '/reference:System.Management.dll',
    ('/out:' + $output)
)
foreach ($name in $payloads) {
    $path = Join-Path $package $name
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing installer payload: $name" }
    $arguments += ('/resource:' + $path + ',Payload.' + $name)
}
$arguments += (Join-Path $package 'GameManagementSetup.cs')

& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed with exit code $LASTEXITCODE." }

$verification = Start-Process -FilePath $output -ArgumentList '--verify' -Wait -PassThru
if ($verification.ExitCode -ne 0) { throw 'The completed installer failed payload verification.' }

$compatibilityTest = Start-Process -FilePath $output -ArgumentList '--compatibility-test' -Wait -PassThru
if ($compatibilityTest.ExitCode -ne 0) { throw 'The completed installer failed RTX 20/30 compatibility classification tests.' }

$distDirectory = Join-Path $package 'dist'
New-Item -ItemType Directory -Path $distDirectory -Force | Out-Null
Copy-Item -LiteralPath $output -Destination (Join-Path $distDirectory 'GameManagement-Setup.exe') -Force

Write-Host "Created and verified universal installer: $output" -ForegroundColor Green

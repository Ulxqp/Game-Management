param([string]$ProjectRoot = (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)))
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework compiler not found.' }
$source = Join-Path $ProjectRoot 'bridge\ReactDesktopHost.cs'
$bridgeRoot = Join-Path $ProjectRoot 'bridge'
$coreAssembly = Join-Path $bridgeRoot 'Microsoft.Web.WebView2.Core.dll'
$formsAssembly = Join-Path $bridgeRoot 'Microsoft.Web.WebView2.WinForms.dll'
$loader = Join-Path $bridgeRoot 'WebView2Loader.dll'
foreach ($dependency in @($coreAssembly, $formsAssembly, $loader)) {
    if (-not (Test-Path -LiteralPath $dependency)) { throw "Missing desktop dependency: $dependency" }
}
$output = Join-Path $ProjectRoot 'GameManagement.Desktop.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Windows.Forms.dll "/reference:$coreAssembly" "/reference:$formsAssembly" "/out:$output" $source
if ($LASTEXITCODE -ne 0) { throw "React desktop host compilation failed with exit code $LASTEXITCODE." }
function Copy-DependencyIfChanged([string]$Source, [string]$Destination) {
    if ((Test-Path -LiteralPath $Destination) -and
        (Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash -eq (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash) {
        return
    }
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
}
Copy-DependencyIfChanged $coreAssembly (Join-Path $ProjectRoot 'Microsoft.Web.WebView2.Core.dll')
Copy-DependencyIfChanged $formsAssembly (Join-Path $ProjectRoot 'Microsoft.Web.WebView2.WinForms.dll')
Copy-DependencyIfChanged $loader (Join-Path $ProjectRoot 'WebView2Loader.dll')
foreach ($legalName in @('Microsoft.Web.WebView2.LICENSE.txt', 'Microsoft.Web.WebView2.NOTICE.txt')) {
    $legalSource = Join-Path $bridgeRoot $legalName
    if (Test-Path -LiteralPath $legalSource) { Copy-Item -LiteralPath $legalSource -Destination (Join-Path $ProjectRoot $legalName) -Force }
}
Write-Host "Built $output"

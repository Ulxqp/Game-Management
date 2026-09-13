$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The Windows .NET Framework C# compiler was not found.'
}

& $compiler /nologo /target:winexe /platform:anycpu /optimize+ `
    ('/win32icon:' + (Join-Path $root 'HardwareSquisher.ico')) `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll /reference:System.Management.dll `
    /reference:System.Web.Extensions.dll `
    ('/out:' + (Join-Path $root 'HardwareSquisher.exe')) `
    (Join-Path $root 'HardwareSquisher.cs')
if ($LASTEXITCODE -ne 0) { throw "Application compilation failed with exit code $LASTEXITCODE." }

& (Join-Path $root 'Test-HardwareSquisherSecurity.ps1')
if ($LASTEXITCODE -ne 0) { throw 'The safety checks did not complete.' }

& (Join-Path $root 'Build-HardwareSquisherInstaller.ps1')
if ($LASTEXITCODE -ne 0) { throw 'The installer build did not complete.' }


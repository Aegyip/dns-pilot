# Windows'un yerleşik .NET Framework 4.8 derleyicisiyle derler. Ek kurulum gerekmez.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
New-Item -ItemType Directory -Force bin | Out-Null
& $csc /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 `
    /out:bin\DnsDegistirici.exe /win32manifest:src\app.manifest `
    /win32icon:src\app.ico /resource:src\app.ico,DnsPilot.app.ico `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
    src\*.cs
if ($LASTEXITCODE -ne 0) { throw "Derleme başarısız" }
Write-Host "Tamam: bin\DnsDegistirici.exe"

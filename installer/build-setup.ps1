# Uygulamayı derler, sonra onu içine gömen kurucuyu üretir: dist\DNSPilot-Setup-<sürüm>.exe
# Uygulama ayrı bir klasöre derlenir (bin\ içindeki çalışan exe kilitli olabilir).
# Store'a yüklemeden önce kurucu bir kod imzalama sertifikasıyla imzalanmalı (signtool sign ...).
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$version = '1.0.0'
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
New-Item -ItemType Directory -Force dist\obj | Out-Null

& $csc /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 `
    /out:dist\obj\DnsDegistirici.exe /win32manifest:src\app.manifest `
    /win32icon:src\app.ico /resource:src\app.ico,DnsPilot.app.ico `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
    src\*.cs
if ($LASTEXITCODE -ne 0) { throw "Uygulama derlenemedi" }

$out = "dist\DNSPilot-Setup-$version.exe"
& $csc /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 `
    /out:$out /win32manifest:installer\setup.manifest /win32icon:src\app.ico `
    /resource:dist\obj\DnsDegistirici.exe,app.exe `
    /r:System.dll /r:System.Windows.Forms.dll `
    installer\Setup.cs
if ($LASTEXITCODE -ne 0) { throw "Kurucu derlenemedi" }
Write-Host "Tamam: $out"

# DNS Pilot (WinUI 3) kurucusunu üretir: dist\DNSPilot-Setup-<sürüm>.exe
# 1) WinUI\publish.ps1 ile uygulama dist\winui-publish\ klasörüne yayımlanır.
# 2) Klasör zip'lenip kurucunun içine gömülür; kurucu kurarken Program Files\DNS Pilot\ altına açar.
# Store'a yüklemeden önce kurucu bir kod imzalama sertifikasıyla imzalanmalı (signtool sign ...).
param([switch]$SkipPublish)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$version = '2.0.0'
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
New-Item -ItemType Directory -Force dist\obj | Out-Null

if (-not $SkipPublish) { & (Join-Path $root 'WinUI\publish.ps1') }
if (-not (Test-Path dist\winui-publish\DnsPilot.exe)) { throw "dist\winui-publish\DnsPilot.exe yok; önce WinUI\publish.ps1" }

$zip = Join-Path $root 'dist\obj\app.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $root 'dist\winui-publish'), $zip, [IO.Compression.CompressionLevel]::Optimal, $false)

$out = "dist\DNSPilot-Setup-$version.exe"
& $csc /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 `
    /out:$out /win32manifest:installer\setup.manifest /win32icon:src\app.ico `
    /resource:dist\obj\app.zip,app.zip `
    /r:System.dll /r:System.Windows.Forms.dll /r:System.IO.Compression.dll `
    installer\Setup.cs
if ($LASTEXITCODE -ne 0) { throw "Kurucu derlenemedi" }
$mb = (Get-Item $out).Length / 1MB
Write-Host ("Tamam: {0} ({1:N1} MB)" -f $out, $mb)

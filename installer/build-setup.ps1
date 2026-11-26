# DNS Pilot (WinUI 3) kurucusunu üretir: dist\DNSPilot-Setup-<sürüm>.exe
# 1) WinUI\publish.ps1 ile uygulama dist\winui-publish\ klasörüne yayımlanır.
# 2) Klasör ve zapret motoru zip'lenip kurucunun içine gömülür; kurucu kurarken Program Files\DNS Pilot\ altına açar.
# Store'a yüklemeden önce kurucu bir kod imzalama sertifikasıyla imzalanmalı (signtool sign ...).
# -Engine: zapret motor klasörü (varsayılan bin\zapret\motor). Motor kurulumla gelir; ilk açılışta Zapret hazırdır.
param([switch]$SkipPublish, [string]$Engine = 'bin\zapret\motor')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$version = '2.2.0'
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
New-Item -ItemType Directory -Force dist\obj | Out-Null

if (-not $SkipPublish) { & (Join-Path $root 'WinUI\publish.ps1') }
if (-not (Test-Path dist\winui-publish\DnsPilot.exe)) { throw "dist\winui-publish\DnsPilot.exe yok; önce WinUI\publish.ps1" }

# Kurulum içeriği: uygulama + zapret motoru (yalnızca çalışması için gerekenler; MSIX ile aynı liste).
if (-not (Test-Path (Join-Path $Engine 'winws2.exe'))) { throw "Zapret motoru bulunamadı: $Engine" }
$stage = Join-Path $root 'dist\obj\setup-stage'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
Copy-Item (Join-Path $root 'dist\winui-publish') $stage -Recurse
New-Item -ItemType Directory -Force "$stage\zapret\motor" | Out-Null
foreach ($f in 'winws2.exe', 'winws.exe', 'WinDivert.dll', 'WinDivert64.sys', 'cygwin1.dll', 'windivert.filter', 'lua', 'files') {
    $src = Join-Path $Engine $f
    if (Test-Path $src) { Copy-Item $src "$stage\zapret\motor" -Recurse }
}
$ver = Join-Path (Split-Path $Engine -Parent) 'surum.txt'
if (Test-Path $ver) { Copy-Item $ver "$stage\zapret\surum.txt" }

$zip = Join-Path $root 'dist\obj\app.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)

$out = "dist\DNSPilot-Setup-$version.exe"
& $csc /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 `
    /out:$out /win32manifest:installer\setup.manifest /win32icon:src\app.ico `
    /resource:dist\obj\app.zip,app.zip `
    /r:System.dll /r:System.Windows.Forms.dll /r:System.IO.Compression.dll `
    installer\Setup.cs
if ($LASTEXITCODE -ne 0) { throw "Kurucu derlenemedi" }
$mb = (Get-Item $out).Length / 1MB
Write-Host ("Tamam: {0} ({1:N1} MB)" -f $out, $mb)

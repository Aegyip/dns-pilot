# WinUI 3 sürümünü dağıtım için yayımlar: dist\winui-publish\
# .NET 8 ve Windows App SDK içine gömülü (self-contained); hedef bilgisayara ek kurulum gerekmez.
# Kurucu (installer\build-setup.ps1) ve MSIX (msix\build-msix.ps1) bu klasörü kullanır.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }
$out = Join-Path $root 'dist\winui-publish'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

& $dotnet publish WinUI\DnsPilot.WinUI.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true `
    -p:DebugType=None -p:DebugSymbols=false -o $out
if ($LASTEXITCODE -ne 0) { throw "Yayımlama başarısız" }

# Geliştirmeye ait dosyalar dağıtıma girmesin.
Get-ChildItem $out -Recurse -Include *.pdb, *.xml -File | Where-Object { $_.Name -notmatch 'AppxManifest' } | Remove-Item -Force

$size = (Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("Tamam: {0} ({1:N1} MB)" -f $out, $size)

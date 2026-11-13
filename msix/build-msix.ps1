# MSIX paketini üretir: dist\DNSPilot.msix
#   -Tools  : makeappx.exe ve signtool.exe'nin bulunduğu klasör (Windows SDK / Microsoft.Windows.SDK.BuildTools)
#   -Engine : zapret motor klasörü (winws2.exe, WinDivert.dll, WinDivert64.sys, cygwin1.dll, lua\ ...)
#   -CertPfx: (isteğe bağlı) yerel test için imza sertifikası. Store'a gönderilecek paket imzasız kalır; Store imzalar.
param(
    [Parameter(Mandatory = $true)][string]$Tools,
    [Parameter(Mandatory = $true)][string]$Engine,
    [string]$CertPfx,
    [string]$CertPassword
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
Add-Type -AssemblyName System.Drawing

# 1) Uygulama
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
New-Item -ItemType Directory -Force dist\obj | Out-Null
& $csc /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 `
    /out:dist\obj\DnsDegistirici.exe /win32manifest:src\app.manifest `
    /win32icon:src\app.ico /resource:src\app.ico,DnsPilot.app.ico `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
    src\*.cs
if ($LASTEXITCODE -ne 0) { throw "Uygulama derlenemedi" }

# 2) Paket düzeni
$layout = 'dist\msix-layout'
if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
New-Item -ItemType Directory -Force "$layout\Assets", "$layout\zapret\motor" | Out-Null
Copy-Item dist\obj\DnsDegistirici.exe $layout
Copy-Item msix\AppxManifest.xml $layout
# Motor: yalnızca çalışması için gerekenler (cmd betikleri ve örnekler hariç)
foreach ($f in 'winws2.exe', 'winws.exe', 'WinDivert.dll', 'WinDivert64.sys', 'cygwin1.dll', 'windivert.filter') {
    $p = Join-Path $Engine $f
    if (Test-Path $p) { Copy-Item $p "$layout\zapret\motor" -Recurse }
}
foreach ($d in 'lua', 'files') {
    $p = Join-Path $Engine $d
    if (Test-Path $p) { Copy-Item $p "$layout\zapret\motor" -Recurse }
}
$ver = Join-Path (Split-Path $Engine -Parent) 'surum.txt'
if (Test-Path $ver) { Copy-Item $ver "$layout\zapret\surum.txt" }

# Simgeler: logo PNG'lerinden gereken boyutlar
function Save-Scaled($src, $size, $dst) {
    $img = [System.Drawing.Image]::FromFile((Resolve-Path $src))
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'
    $g.DrawImage($img, 0, 0, $size, $size)
    $bmp.Save($dst, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose(); $img.Dispose()
}
Save-Scaled src\icon\logo-256.png 150 "$layout\Assets\Square150x150Logo.png"
Save-Scaled src\icon\logo-256.png 44 "$layout\Assets\Square44x44Logo.png"
Save-Scaled src\icon\logo-256.png 50 "$layout\Assets\StoreLogo.png"

# 3) Paketle
$out = 'dist\DNSPilot.msix'
& (Join-Path $Tools 'makeappx.exe') pack /d $layout /p $out /o
if ($LASTEXITCODE -ne 0) { throw "makeappx başarısız" }

# 4) Yerel test için imzala (isteğe bağlı)
if ($CertPfx) {
    & (Join-Path $Tools 'signtool.exe') sign /fd SHA256 /f $CertPfx /p $CertPassword $out
    if ($LASTEXITCODE -ne 0) { throw "signtool başarısız" }
}
Write-Host "Tamam: $out"

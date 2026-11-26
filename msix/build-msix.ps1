# MSIX paketini üretir: dist\DNSPilot.msix
#   -Tools  : makeappx.exe ve signtool.exe'nin bulunduğu klasör (Windows SDK / Microsoft.Windows.SDK.BuildTools)
#   -CertPfx: (isteğe bağlı) yerel test için imza sertifikası. Store'a gönderilecek paket imzasız kalır; Store imzalar.
param(
    [Parameter(Mandatory = $true)][string]$Tools,
    [string]$CertPfx,
    [string]$CertPassword,
    [string]$CertThumbprint,  # yerel test: kullanıcı deposundaki sertifikayla imzala
    # Local test only: package under a test identity signed with a self-signed certificate.
    [string]$TestName,
    [string]$TestPublisher,
    [int]$TestRevision = 0,  # yerel test: aynı sürümü yeniden kurabilmek için son hane
    [string]$Out = 'dist\DNSPilot.msix'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
Add-Type -AssemblyName System.Drawing

# 1) Uygulama (WinUI 3, self-contained): dist\winui-publish\
if (-not (Test-Path dist\winui-publish\DnsPilot.exe)) { & (Join-Path $root 'WinUI\publish.ps1') }

# 2) Paket düzeni
$layout = 'dist\msix-layout'
if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
New-Item -ItemType Directory -Force "$layout\Assets" | Out-Null
Copy-Item dist\winui-publish\* $layout -Recurse -Force  # Assets klasörü pakette zaten var: birleştir
Copy-Item msix\AppxManifest.xml $layout
if ($TestName -and $TestPublisher) {
    $mf = Join-Path $layout 'AppxManifest.xml'
    $x = [IO.File]::ReadAllText($mf)
    $x = [regex]::Replace($x, 'Name="Aegyip\.DNSPilot"', "Name=""$TestName""")
    $x = [regex]::Replace($x, 'Publisher="CN=[^"]+"', "Publisher=""$TestPublisher""")
    if ($TestRevision -gt 0) { $x = [regex]::Replace($x, '(<Identity [^>]*Version="\d+\.\d+\.\d+\.)0"', ('${1}' + $TestRevision + '"')) }
    [IO.File]::WriteAllText($mf, $x, (New-Object Text.UTF8Encoding $false))
}
# Paket sürücü içermez (Store politikası 10.2.4.2). Eski bir yayımlama klasöründen kalmış olsa bile denetlenir.
$bad = Get-ChildItem $layout -Recurse -File | Where-Object { $_.Extension -eq '.sys' -or $_.Name -match '^(winws2?|WinDivert.*|cygwin1)\.' }
if ($bad) { throw "Pakette sürücü/Zapret dosyası var: $($bad.FullName -join ', ')" }

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
# Görev çubuğu/Başlat için plakasız (unplated) ve ölçekli logolar WinUI\Assets içinde; WinUI derlemesi onları
# resources.pri'ye ekler. Windows bunları bulamazsa logoyu vurgu renginde bir kare plaka içinde gösterir.

# 3) Paketle
$out = $Out
& (Join-Path $Tools 'makeappx.exe') pack /d $layout /p $out /o
if ($LASTEXITCODE -ne 0) { throw "makeappx başarısız" }

# 4) Yerel test için imzala (isteğe bağlı)
if ($CertThumbprint) {
    & (Join-Path $Tools 'signtool.exe') sign /fd SHA256 /sha1 $CertThumbprint /s My $out
    if ($LASTEXITCODE -ne 0) { throw "signtool başarısız" }
}
elseif ($CertPfx) {
    & (Join-Path $Tools 'signtool.exe') sign /fd SHA256 /f $CertPfx /p $CertPassword $out
    if ($LASTEXITCODE -ne 0) { throw "signtool başarısız" }
}
Write-Host "Tamam: $out"

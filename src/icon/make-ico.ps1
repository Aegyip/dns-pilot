# logo-*.png dosyalarından çok boyutlu app.ico üretir.
# Küçük boyutlar 32 bit BMP (eski GDI+ okuyabilsin diye), 256 PNG olarak yazılır.
# Logo Figma'da değişirse PNG'leri yeniden dışa aktarıp bu betiği çalıştır.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
Add-Type -AssemblyName System.Drawing

$sizes = 16, 24, 32, 48, 64, 256
$entries = @()
foreach ($s in $sizes) {
    $path = Join-Path $PSScriptRoot "logo-$s.png"
    if ($s -eq 256) {
        $entries += ,@($s, [IO.File]::ReadAllBytes($path))
        continue
    }
    $bmp = New-Object System.Drawing.Bitmap $path
    $ms = New-Object IO.MemoryStream
    $w = New-Object IO.BinaryWriter $ms
    # BITMAPINFOHEADER (yükseklik iki katı: renk + AND maskesi)
    $w.Write([int]40); $w.Write([int]$s); $w.Write([int]($s * 2)); $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
    for ($y = $s - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $s; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
        }
    }
    $maskRow = [int]([Math]::Ceiling($s / 32.0) * 4)
    $w.Write((New-Object byte[] ($maskRow * $s)))
    $w.Flush()
    $entries += ,@($s, $ms.ToArray())
    $bmp.Dispose()
}

$out = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $out
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$entries.Count)
$offset = 6 + 16 * $entries.Count
foreach ($e in $entries) {
    $s = $e[0]; $data = $e[1]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]$data.Length); $w.Write([int]$offset)
    $offset += $data.Length
}
foreach ($e in $entries) { $w.Write([byte[]]$e[1]) }
$w.Flush()
[IO.File]::WriteAllBytes((Join-Path $PSScriptRoot '..\app.ico'), $out.ToArray())
Write-Host ("app.ico: {0} boyut, {1} bayt" -f $entries.Count, $out.Length)

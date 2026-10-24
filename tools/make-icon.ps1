# Generates assets\gili.ico (a gradient "island" pill) with PNG-compressed entries.
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'assets\gili.ico'
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null

$sizes = 256, 64, 48, 32, 24, 16
$pngs = foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $w = $s * 0.9; $h = $s * 0.44
    $x = ($s - $w) / 2; $y = ($s - $h) / 2
    $rect = New-Object System.Drawing.RectangleF $x, $y, $w, $h
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $h
    $path.AddArc($x, $y, $d, $d, 90, 180)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 180)
    $path.CloseFigure()
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 72, 220, 170)), ([System.Drawing.Color]::FromArgb(255, 150, 120, 255)), 0.0
    $g.FillPath($brush, $path)
    $dot = $h * 0.46
    $g.FillEllipse([System.Drawing.Brushes]::White, $x + $w - $h * 0.5 - $dot / 2, $y + ($h - $dot) / 2, $dot, $dot)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}

$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$len); $bw.Write([uint32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close()
Write-Output "Wrote $out"

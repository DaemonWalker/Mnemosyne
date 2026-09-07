# Generates the multi-size application icon (mnemosyne.ico) plus a 256px PNG preview.
# Usage: powershell -File scripts/generate-icon.ps1
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\src\Mnemosyne\Resources\mnemosyne.ico'),
    [string]$PreviewPath = (Join-Path $PSScriptRoot '..\src\Mnemosyne\Resources\mnemosyne-256.png')
)

Add-Type -AssemblyName System.Drawing

function New-RoundedRectPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

# --- Draw master at 1024px ---
$master = 1024
$bmp = New-Object System.Drawing.Bitmap $master, $master, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias

$path = New-RoundedRectPath 0 0 $master $master ($master * 0.22)
$g.SetClip($path)

$bgRect = New-Object System.Drawing.Rectangle 0, 0, $master, $master
$topColor = [System.Drawing.Color]::FromArgb(255, 30, 156, 229)    # lighter accent
$bottomColor = [System.Drawing.Color]::FromArgb(255, 0, 92, 165)   # darker accent
$brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $bgRect, $topColor, $bottomColor, 115
$g.FillRectangle($brush, $bgRect)
$brush.Dispose()
$g.ResetClip()

$font = New-Object System.Drawing.Font('Segoe UI', ($master * 0.62), [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$sf = New-Object System.Drawing.StringFormat
$sf.Alignment = [System.Drawing.StringAlignment]::Center
$sf.LineAlignment = [System.Drawing.StringAlignment]::Center
$center = New-Object System.Drawing.PointF(($master / 2), ($master * 0.47))
$g.DrawString('M', $font, [System.Drawing.Brushes]::White, $center, $sf)

$font.Dispose(); $sf.Dispose(); $path.Dispose(); $g.Dispose()

# --- Resize into ICO frames ---
$sizes = @(256, 128, 64, 48, 32, 24, 16)
# Small/medium frames use classic BMP (DIB) encoding for compatibility with
# System.Drawing.Icon and older tools; large frames use PNG compression.
$frameData = @{}
foreach ($size in $sizes) {
    $resized = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $rg = [System.Drawing.Graphics]::FromImage($resized)
    $rg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $rg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $rg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $destRect = New-Object System.Drawing.Rectangle 0, 0, $size, $size
    $rg.DrawImage($bmp, $destRect, 0, 0, $master, $master, [System.Drawing.GraphicsUnit]::Pixel)
    $rg.Dispose()

    $ms = New-Object System.IO.MemoryStream
    if ($size -ge 128) {
        $resized.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    else {
        # DIB frame: BITMAPINFOHEADER with doubled height, bottom-up BGRA pixels, zero AND mask
        $bw = New-Object System.IO.BinaryWriter $ms
        $maskRowSize = [Math]::Floor(($size + 31) / 32) * 4
        $bw.Write([UInt32]40)                            # biSize
        $bw.Write([Int32]$size)                          # biWidth
        $bw.Write([Int32]($size * 2))                    # biHeight (XOR + AND)
        $bw.Write([UInt16]1)                             # biPlanes
        $bw.Write([UInt16]32)                            # biBitCount
        $bw.Write([UInt32]0)                             # biCompression (BI_RGB)
        $bw.Write([UInt32]($size * $size * 4 + $maskRowSize * $size))
        $bw.Write([Int32]0); $bw.Write([Int32]0)         # biXPelsPerMeter, biYPelsPerMeter
        $bw.Write([UInt32]0); $bw.Write([UInt32]0)       # biClrUsed, biClrImportant
        for ($y = $size - 1; $y -ge 0; $y--) {
            for ($x = 0; $x -lt $size; $x++) {
                $c = $resized.GetPixel($x, $y)
                $bw.Write($c.B); $bw.Write($c.G); $bw.Write($c.R); $bw.Write($c.A)
            }
        }
        $bw.Write((New-Object byte[] ($maskRowSize * $size)))
        $bw.Flush()
    }
    $frameData[$size] = $ms.ToArray()
    $ms.Dispose(); $resized.Dispose()
}
$bmp.Dispose()

# --- Assemble multi-size ICO ---
$outDir = Split-Path $OutputPath -Parent
if (!(Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

$fs = [System.IO.File]::Create($OutputPath)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($size in $sizes) {
    $dim = [byte]($size -band 0xFF)
    if ($size -ge 256) { $dim = [byte]0 }
    $bw.Write($dim)                    # width
    $bw.Write($dim)                    # height
    $bw.Write([byte]0)                 # palette colors
    $bw.Write([byte]0)                 # reserved
    $bw.Write([UInt16]1)               # color planes
    $bw.Write([UInt16]32)              # bits per pixel
    $bw.Write([UInt32]$frameData[$size].Length)
    $bw.Write([UInt32]$offset)
    $offset += $frameData[$size].Length
}
foreach ($size in $sizes) { $bw.Write($frameData[$size]) }
$bw.Close()

[System.IO.File]::WriteAllBytes($PreviewPath, $frameData[256])
Write-Host "Wrote $OutputPath ($([Math]::Round((Get-Item $OutputPath).Length / 1KB, 1)) KB)"
Write-Host "Wrote $PreviewPath"

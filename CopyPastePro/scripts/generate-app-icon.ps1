# Regenerate Assets\AppIcon.ico from transparent PNG (prefers AppIcon-256px.png, else AppIcon.png).
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$projectDir = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $projectDir "Assets"
$png256 = Join-Path $assets "AppIcon-256px.png"
$png = Join-Path $assets "AppIcon.png"
$ico = Join-Path $assets "AppIcon.ico"
$sourcePng = if (Test-Path $png256) { $png256 } elseif (Test-Path $png) { $png } else { $null }

if (-not $sourcePng) {
    Write-Error "Missing AppIcon-256px.png or AppIcon.png — add a transparent PNG first."
}
Write-Host "Source: $sourcePng"

$src = [System.Drawing.Image]::FromFile($sourcePng)
$images = @()
foreach ($s in 16, 32, 48, 256) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($src, 0, 0, $s, $s)
    $g.Dispose()
    $images += $bmp
}
$src.Dispose()

$ms = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $ms
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$images.Count)
$offset = 6 + (16 * $images.Count)
$dataList = @()
foreach ($bmp in $images) {
    $pngMs = New-Object System.IO.MemoryStream
    $bmp.Save($pngMs, [System.Drawing.Imaging.ImageFormat]::Png)
    $dataList += ,$pngMs.ToArray()
}
$i = 0
foreach ($bmp in $images) {
    $bytes = $dataList[$i++]
    $w = if ($bmp.Width -eq 256) { 0 } else { $bmp.Width -band 0xFF }
    $h = if ($bmp.Height -eq 256) { 0 } else { $bmp.Height -band 0xFF }
    $writer.Write([byte]$w)
    $writer.Write([byte]$h)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$bytes.Length)
    $writer.Write([uint32]$offset)
    $offset += $bytes.Length
}
foreach ($bytes in $dataList) { $writer.Write($bytes) }
[System.IO.File]::WriteAllBytes($ico, $ms.ToArray())
foreach ($bmp in $images) { $bmp.Dispose() }
Write-Host "Wrote $ico"

# Rebuild the Windows ICO from the same 256-unit geometric design as Assets/app-icon.svg.
# Each size is rendered directly with antialiasing; no external tools are required.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$iconPath = Join-Path $PSScriptRoot '../src/MS3DPRINT.Manager.App/Assets/app-icon.ico'
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = @()
foreach ($size in $sizes) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.ScaleTransform($size / 256.0, $size / 256.0)
    $background = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#25292F'))
    $rounded = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $rounded.AddArc(4, 4, 96, 96, 180, 90)
    $rounded.AddArc(156, 4, 96, 96, 270, 90)
    $rounded.AddArc(156, 156, 96, 96, 0, 90)
    $rounded.AddArc(4, 156, 96, 96, 90, 90)
    $rounded.CloseFigure()
    $graphics.FillPath($background, $rounded)
    $polygons = @(
        @{ Color='#90CAF9'; Points=@(128,46,208,90,128,136,48,90) },
        @{ Color='#2196F3'; Points=@(48,90,128,136,128,218,48,172) },
        @{ Color='#1565C0'; Points=@(128,136,208,90,208,172,128,218) }
    )
    foreach ($polygon in $polygons) {
        $points = [System.Drawing.PointF[]]::new(4)
        for ($i=0; $i -lt 4; $i++) { $points[$i] = [System.Drawing.PointF]::new($polygon.Points[$i*2], $polygon.Points[$i*2+1]) }
        $brush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml($polygon.Color))
        $graphics.FillPolygon($brush, $points)
        $brush.Dispose()
    }
    $pen = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml('#25292F'), 6)
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $graphics.DrawLine($pen, 48, 90, 128, 136)
    $graphics.DrawLine($pen, 128, 136, 208, 90)
    $graphics.DrawLine($pen, 128, 136, 128, 218)
    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += @{ Size=$size; Data=$stream.ToArray() }
    if ($size -eq 256) { $bitmap.Save((Join-Path (Split-Path $iconPath) 'app-icon.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $stream.Dispose(); $pen.Dispose(); $rounded.Dispose(); $background.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$output = [System.IO.File]::Create($iconPath)
$writer = [System.IO.BinaryWriter]::new($output)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Data.Length); $writer.Write([uint32]$offset)
        $offset += $frame.Data.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Data) }
} finally { $writer.Dispose(); $output.Dispose() }
Write-Output "Generated $iconPath (7 sizes, 16–256 px)."

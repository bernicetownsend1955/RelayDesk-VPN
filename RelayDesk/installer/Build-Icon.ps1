$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assets = Join-Path (Split-Path $PSScriptRoot -Parent) 'Assets'
New-Item -ItemType Directory -Path $assets -Force | Out-Null
$stream = [IO.File]::Create((Join-Path $assets 'RelayDesk.ico'))
$writer = New-Object IO.BinaryWriter($stream)
try {
    $images = @()
    foreach ($size in @(16,32,48,256)) {
        $bitmap = New-Object Drawing.Bitmap($size,$size)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([Drawing.ColorTranslator]::FromHtml('#286548'))
        $graphics.ScaleTransform($size/64.0,$size/64.0)
        $pen = New-Object Drawing.Pen([Drawing.Color]::White,5)
        $pen.StartCap = $pen.EndCap = [Drawing.Drawing2D.LineCap]::Square
        $graphics.DrawLines($pen,[Drawing.PointF[]]@([Drawing.PointF]::new(18,47),[Drawing.PointF]::new(18,17),[Drawing.PointF]::new(38,17),[Drawing.PointF]::new(46,25),[Drawing.PointF]::new(38,33),[Drawing.PointF]::new(20,33)))
        $graphics.DrawLine($pen,32,33,47,48)
        $memory = New-Object IO.MemoryStream
        $bitmap.Save($memory,[Drawing.Imaging.ImageFormat]::Png)
        $images += ,@($size,$memory.ToArray())
        $memory.Dispose(); $pen.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
    }
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
    $offset = 6 + 16*$images.Count
    foreach ($entry in $images) {
        $dimension = if ($entry[0] -eq 256) { 0 } else { $entry[0] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$entry[1].Length); $writer.Write([uint32]$offset)
        $offset += $entry[1].Length
    }
    foreach ($entry in $images) { $writer.Write([byte[]]$entry[1]) }
} finally { $writer.Dispose(); $stream.Dispose() }

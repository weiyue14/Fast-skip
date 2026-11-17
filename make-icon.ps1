$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetDir = Join-Path $PSScriptRoot 'assets'
$sizes = @(16,24,32,48,64,128,256)
$frames = @()
foreach ($size in $sizes) {
    $bitmap = New-Object System.Drawing.Bitmap($size,$size)
    $canvas = [System.Drawing.Graphics]::FromImage($bitmap)
    $canvas.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $canvas.Clear([System.Drawing.Color]::Transparent)
    $canvas.ScaleTransform(($size / 256.0),($size / 256.0))
    $shape = New-Object System.Drawing.Drawing2D.GraphicsPath
    $shape.AddArc(8,8,112,112,180,90)
    $shape.AddArc(136,8,112,112,270,90)
    $shape.AddArc(136,136,112,112,0,90)
    $shape.AddArc(8,136,112,112,90,90)
    $shape.CloseFigure()
    $fill = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#5865DF'))
    $canvas.FillPath($fill,$shape)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White,20)
    $pen.StartCap = $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    foreach ($coords in @(@(69,91,187,91),@(187,91,162,66),@(187,91,162,116),@(187,165,69,165),@(69,165,94,140),@(69,165,94,190))) {
        $canvas.DrawLine($pen,[single]$coords[0],[single]$coords[1],[single]$coords[2],[single]$coords[3])
    }
    $buffer = New-Object System.IO.MemoryStream
    $bitmap.Save($buffer,[System.Drawing.Imaging.ImageFormat]::Png)
    $frames += ,$buffer.ToArray()
    if ($size -eq 256) { $bitmap.Save((Join-Path $assetDir 'FastSkip.png'),[System.Drawing.Imaging.ImageFormat]::Png) }
    $buffer.Dispose(); $pen.Dispose(); $fill.Dispose(); $shape.Dispose(); $canvas.Dispose(); $bitmap.Dispose()
}
$file = [System.IO.File]::Create((Join-Path $assetDir 'FastSkip.ico'))
$writer = New-Object System.IO.BinaryWriter($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i=0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose(); $file.Dispose() }
Write-Host '已生成 SVG / PNG / 多尺寸 ICO 图标。'

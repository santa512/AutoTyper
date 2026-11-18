Add-Type -AssemblyName System.Drawing
$iconPath = Join-Path $PSScriptRoot '..\Assets\TypingPractice.ico'
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($iconPath)) | Out-Null
$images = @()
foreach ($size in @(16, 32, 48, 64, 128, 256)) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform($size / 256.0, $size / 256.0)
    $background = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 30, 77, 183))
    $keys = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
    $accent = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 255, 196, 68))
    $shape = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $shape.AddArc(8, 8, 64, 64, 180, 90)
    $shape.AddArc(184, 8, 64, 64, 270, 90)
    $shape.AddArc(184, 184, 64, 64, 0, 90)
    $shape.AddArc(8, 184, 64, 64, 90, 90)
    $shape.CloseFigure()
    $graphics.FillPath($background, $shape)
    for ($row = 0; $row -lt 3; $row++) {
        for ($column = 0; $column -lt 5; $column++) {
            $keyBrush = if ($row -eq 1 -and $column -eq 2) { $accent } else { $keys }
            $graphics.FillRectangle($keyBrush, 41 + $column * 36, 63 + $row * 36, 28, 26)
        }
    }
    $graphics.FillRectangle($keys, 77, 175, 100, 18)
    $memory = [System.IO.MemoryStream]::new()
    $bitmap.Save($memory, [System.Drawing.Imaging.ImageFormat]::Png)
    $images += ,@($size, $memory.ToArray())
    $memory.Dispose()
    $shape.Dispose()
    $background.Dispose()
    $keys.Dispose()
    $accent.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}
$stream = [System.IO.File]::Create($iconPath)
$writer = [System.IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$images.Count)
    $offset = 6 + 16 * $images.Count
    foreach ($entry in $images) {
        $dimension = if ($entry[0] -eq 256) { 0 } else { $entry[0] }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$entry[1].Length)
        $writer.Write([uint32]$offset)
        $offset += $entry[1].Length
    }
    foreach ($entry in $images) { $writer.Write([byte[]]$entry[1]) }
} finally {
    $writer.Dispose()
    $stream.Dispose()
}

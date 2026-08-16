$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$assetDirectory = Join-Path $PSScriptRoot '..\src\DeepSeekHarnessDesktop\Assets'
[System.IO.Directory]::CreateDirectory($assetDirectory) | Out-Null
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$images = [System.Collections.Generic.List[byte[]]]::new()

foreach ($size in $sizes) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $inset = [Math]::Max(1, [Math]::Round($size * 0.04))
        $radius = [Math]::Max(2, [Math]::Round($size * 0.19))
        $bounds = [System.Drawing.RectangleF]::new($inset, $inset, $size - (2 * $inset), $size - (2 * $inset))
        $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
        try {
            $diameter = 2 * $radius
            $path.AddArc($bounds.Left, $bounds.Top, $diameter, $diameter, 180, 90)
            $path.AddArc($bounds.Right - $diameter, $bounds.Top, $diameter, $diameter, 270, 90)
            $path.AddArc($bounds.Right - $diameter, $bounds.Bottom - $diameter, $diameter, $diameter, 0, 90)
            $path.AddArc($bounds.Left, $bounds.Bottom - $diameter, $diameter, $diameter, 90, 90)
            $path.CloseFigure()
            $background = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 31, 41, 51))
            try { $graphics.FillPath($background, $path) } finally { $background.Dispose() }
        }
        finally { $path.Dispose() }

        $whiteWidth = [Math]::Max(1.4, $size * 0.075)
        $accentWidth = [Math]::Max(1.2, $size * 0.055)
        $whitePen = [System.Drawing.Pen]::new([System.Drawing.Color]::White, $whiteWidth)
        $accentPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 16, 185, 129), $accentWidth)
        try {
            $whitePen.StartCap = $whitePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
            $accentPen.StartCap = $accentPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
            $left = $size * 0.31
            $right = $size * 0.69
            $top = $size * 0.26
            $bottom = $size * 0.74
            $graphics.DrawLine($whitePen, $left, $top, $left, $bottom)
            $graphics.DrawLine($whitePen, $right, $top, $right, $bottom)
            foreach ($fraction in @(0.36, 0.50, 0.64)) {
                $y = $size * $fraction
                $graphics.DrawLine($accentPen, $left, $y, $right, $y)
            }
        }
        finally {
            $whitePen.Dispose()
            $accentPen.Dispose()
        }

        $stream = [System.IO.MemoryStream]::new()
        try {
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            $images.Add($stream.ToArray())
            if ($size -eq 256) {
                [System.IO.File]::WriteAllBytes((Join-Path $assetDirectory 'App.png'), $stream.ToArray())
            }
        }
        finally { $stream.Dispose() }
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$iconPath = Join-Path $assetDirectory 'App.ico'
$file = [System.IO.File]::Open($iconPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
$writer = [System.IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)
    $offset = 6 + (16 * $sizes.Count)
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $size = $sizes[$index]
        $payload = $images[$index]
        $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
        $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$payload.Length)
        $writer.Write([uint32]$offset)
        $offset += $payload.Length
    }
    foreach ($payload in $images) {
        $writer.Write($payload)
    }
}
finally {
    $writer.Dispose()
    $file.Dispose()
}

Write-Output $iconPath

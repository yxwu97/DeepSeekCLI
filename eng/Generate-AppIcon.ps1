$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase, System.Drawing

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$assetDirectory = Join-Path $repositoryRoot 'src\DeepSeekHarnessDesktop\Assets'
$kitDirectory = Join-Path $repositoryRoot 'output\shiwei-lingguang-icon-kit'
[IO.Directory]::CreateDirectory($kitDirectory) | Out-Null
$source = [xml][IO.File]::ReadAllText((Join-Path $assetDirectory 'Shiwei-Lingguang.svg'))
$paths = @($source.SelectNodes("//*[local-name()='path']") | ForEach-Object { $_.GetAttribute('d') })
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$variants = @(
    @{ Name = 'app-cinnabar'; Color = '#FFF6E9'; Tile = '#B94432'; Label = 'APP / CINNABAR' },
    @{ Name = 'app-ivory'; Color = '#B94432'; Tile = '#F0E5D5'; Label = 'APP / IVORY' },
    @{ Name = 'mark-cinnabar'; Color = '#B94432'; Tile = ''; Label = 'MARK / CINNABAR' },
    @{ Name = 'mark-ivory'; Color = '#FFF6E9'; Tile = ''; Label = 'MARK / IVORY' },
    @{ Name = 'mark-black'; Color = '#000000'; Tile = ''; Label = 'MONO / BLACK' },
    @{ Name = 'mark-white'; Color = '#FFFFFF'; Tile = ''; Label = 'MONO / WHITE' }
)

function ConvertTo-Brush([string]$Color) {
    return [Windows.Media.BrushConverter]::new().ConvertFromString($Color)
}

function Export-Png($Variant, [int]$Size, [string]$Path) {
    $renderSize = [Math]::Max(1024, $Size)
    $visual = [Windows.Media.DrawingVisual]::new()
    $context = $visual.RenderOpen()
    try {
        $context.PushTransform([Windows.Media.ScaleTransform]::new($renderSize / 256.0, $renderSize / 256.0))
        if ($Variant.Tile) {
            $context.DrawRoundedRectangle((ConvertTo-Brush $Variant.Tile), $null, [Windows.Rect]::new(0, 0, 256, 256), 54, 54)
        }
        foreach ($pathData in $paths) {
            $context.DrawGeometry((ConvertTo-Brush $Variant.Color), $null, [Windows.Media.Geometry]::Parse($pathData))
        }
        $context.Pop()
    }
    finally { $context.Close() }
    $render = [Windows.Media.Imaging.RenderTargetBitmap]::new($renderSize, $renderSize, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $render.Render($visual)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($render))
    $stream = [IO.MemoryStream]::new()
    $image = $null; $bitmap = $null; $graphics = $null
    try {
        $encoder.Save($stream); $stream.Position = 0
        $image = [Drawing.Image]::FromStream($stream)
        $bitmap = [Drawing.Bitmap]::new($Size, $Size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.DrawImage($image, [Drawing.Rectangle]::new(0, 0, $Size, $Size))
        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        if ($graphics) { $graphics.Dispose() }
        if ($bitmap) { $bitmap.Dispose() }
        if ($image) { $image.Dispose() }
        $stream.Dispose()
    }
}

function Export-Ico([string]$Name) {
    $payloads = @($sizes | ForEach-Object { ,([IO.File]::ReadAllBytes((Join-Path $kitDirectory "png\$Name\$_.png"))) })
    $writer = [IO.BinaryWriter]::new([IO.File]::Create((Join-Path $kitDirectory "ico\$Name.ico")))
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        for ($index = 0; $index -lt $sizes.Count; $index++) {
            $encodedSize = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
            $writer.Write([byte]$encodedSize); $writer.Write([byte]$encodedSize)
            $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$payloads[$index].Length); $writer.Write([uint32]$offset)
            $offset += $payloads[$index].Length
        }
        foreach ($payload in $payloads) { $writer.Write([byte[]]$payload) }
    }
    finally { $writer.Dispose() }
}

function Export-Svg($Variant) {
    $background = if ($Variant.Tile) { '<rect width="256" height="256" rx="54" fill="' + $Variant.Tile + '"/>' } else { '' }
    $geometry = ($paths | ForEach-Object { '<path d="' + $_ + '"/>' }) -join ''
    $svg = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256"><title>SHIWEI Lingguang / ' + $Variant.Name + '</title>' + $background + '<g fill="' + $Variant.Color + '">' + $geometry + '</g></svg>'
    [IO.File]::WriteAllText((Join-Path $kitDirectory "svg\$($Variant.Name).svg"), $svg, [Text.UTF8Encoding]::new($false))
}

function Export-Preview {
    $board = [Drawing.Bitmap]::new(1500, 950)
    $graphics = [Drawing.Graphics]::FromImage($board)
    $font = [Drawing.Font]::new('Segoe UI', 22, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)
    $titleFont = [Drawing.Font]::new('Microsoft YaHei UI', 38, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
    $textBrush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#513D34'))
    try {
        $graphics.Clear([Drawing.ColorTranslator]::FromHtml('#FAF4EB'))
        $graphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
        $graphics.DrawString('SHIWEI / 识微 · 灵光', $titleFont, $textBrush, 56, 32)
        for ($index = 0; $index -lt $variants.Count; $index++) {
            $variant = $variants[$index]; $x = 50 + ($index % 3) * 490; $y = 115 + [Math]::Floor($index / 3) * 410
            $back = if ($variant.Name -in @('mark-white', 'mark-ivory')) { '#B94432' } else { '#FFFFFF' }
            $brush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($back))
            $picture = [Drawing.Image]::FromFile((Join-Path $kitDirectory "png\$($variant.Name)\256.png"))
            try {
                $graphics.FillRectangle($brush, [single]$x, [single]$y, 460, 330)
                $graphics.DrawImage($picture, [Drawing.Rectangle]::new($x + 102, $y + 20, 256, 256))
            }
            finally { $picture.Dispose(); $brush.Dispose() }
            $graphics.DrawString($variant.Label, $font, $textBrush, [single]($x + 10), [single]($y + 348))
        }
        $board.Save((Join-Path $kitDirectory 'preview.png'), [Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $textBrush.Dispose(); $titleFont.Dispose(); $font.Dispose(); $graphics.Dispose(); $board.Dispose() }
}

foreach ($directory in @('svg', 'ico')) { [IO.Directory]::CreateDirectory((Join-Path $kitDirectory $directory)) | Out-Null }
foreach ($variant in $variants) {
    $pngDirectory = Join-Path $kitDirectory "png\$($variant.Name)"
    [IO.Directory]::CreateDirectory($pngDirectory) | Out-Null
    foreach ($size in @($sizes) + @(512, 1024)) { Export-Png $variant $size (Join-Path $pngDirectory "$size.png") }
    Export-Svg $variant
    Export-Ico $variant.Name
}
Copy-Item -LiteralPath (Join-Path $kitDirectory 'ico\app-cinnabar.ico') -Destination (Join-Path $assetDirectory 'App.ico') -Force
Copy-Item -LiteralPath (Join-Path $kitDirectory 'png\app-cinnabar\256.png') -Destination (Join-Path $assetDirectory 'App.png') -Force
Export-Preview
$readme = @'
# 识微 · 灵光 / SHIWEI Lingguang

选定方案：C / 灵光。朱砂 #B94432，暖白 #FFF6E9，浅底 #F0E5D5。

- app-cinnabar：朱砂底暖白标志，应用 EXE、窗口及托盘的默认图标。
- app-ivory：浅底朱砂标志。
- mark-cinnabar / mark-ivory：透明底品牌图形及反白版。
- mark-black / mark-white：透明底单色版；不是黑色背景。
- SVG：6 个矢量版本；PNG：每版 16/20/24/32/40/48/64/128/256/512/1024 像素。
- ICO：每版包含前述 16 至 256 像素的 9 帧，支持 Windows 小图标和 DPI 缩放。

母版：src/DeepSeekHarnessDesktop/Assets/Shiwei-Lingguang.svg。
重新生成：在仓库根目录执行 .\eng\Generate-AppIcon.ps1（Windows / WPF）。
固定图形比例，保持主形与微星相对位置，不拉伸、不重新拼接。
本次为设计选定和应用落地，不代表已完成该新图形的商标检索。
'@
[IO.File]::WriteAllText((Join-Path $kitDirectory 'README.md'), $readme, [Text.UTF8Encoding]::new($false))
Compress-Archive -Path (Join-Path $kitDirectory '*') -DestinationPath "$kitDirectory.zip" -Force
Write-Output (Join-Path $assetDirectory 'App.ico')
Write-Output "$kitDirectory.zip"

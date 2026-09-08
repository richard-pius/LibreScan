Add-Type -AssemblyName System.Drawing

$assetsDir = Join-Path $PSScriptRoot "Assets"
if (-not (Test-Path $assetsDir)) {
    New-Item -ItemType Directory -Path $assetsDir -Force | Out-Null
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngBytesList = [System.Collections.Generic.List[byte[]]]::new()

foreach ($sz in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($sz, $sz, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = $sz / 32.0

    # Shield polygon
    $pts = @(
        [System.Drawing.PointF]::new(16.0 * $s, 1.5 * $s),
        [System.Drawing.PointF]::new(29.5 * $s, 7.5 * $s),
        [System.Drawing.PointF]::new(29.5 * $s, 17.5 * $s),
        [System.Drawing.PointF]::new(16.0 * $s, 30.5 * $s),
        [System.Drawing.PointF]::new(2.5 * $s, 17.5 * $s),
        [System.Drawing.PointF]::new(2.5 * $s, 7.5 * $s)
    )

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddLines($pts)
    $path.CloseFigure()

    $rect = New-Object System.Drawing.RectangleF(0, 0, $sz, $sz)
    $c1 = [System.Drawing.Color]::FromArgb(45, 212, 191)    # Teal 400
    $c2 = [System.Drawing.Color]::FromArgb(16, 185, 129)    # Emerald 500
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $c1, $c2, [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
    $g.FillPath($brush, $path)

    # Subtle inner glow / border
    $penWidth = [Math]::Max(1.0, 0.8 * $s)
    $borderPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(180, 255, 255, 255), $penWidth)
    $g.DrawPath($borderPen, $path)

    # White Checkmark
    $checkWidth = [Math]::Max(1.5, 2.5 * $s)
    $checkPen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, $checkWidth)
    $checkPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $checkPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $checkPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    $checkPts = @(
        [System.Drawing.PointF]::new(10.0 * $s, 16.0 * $s),
        [System.Drawing.PointF]::new(14.0 * $s, 21.0 * $s),
        [System.Drawing.PointF]::new(22.5 * $s, 10.5 * $s)
    )
    $g.DrawLines($checkPen, $checkPts)

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngBytesList.Add($ms.ToArray())

    $g.Dispose()
    $bmp.Dispose()
    $brush.Dispose()
    $borderPen.Dispose()
    $checkPen.Dispose()
    $path.Dispose()
    $ms.Dispose()
}

$icoPath = Join-Path $assetsDir "librescan.ico"
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)

# ICONDIR header
$bw.Write([uint16]0) # Reserved
$bw.Write([uint16]1) # Icon type
$bw.Write([uint16]$sizes.Count) # Count

$offset = 6 + ($sizes.Count * 16)

for ($i = 0; $i -lt $sizes.Count; $i++) {
    $szVal = $sizes[$i]
    $bSize = if ($szVal -ge 256) { [byte]0 } else { [byte]$szVal }
    $data = $pngBytesList[$i]

    $bw.Write($bSize) # Width
    $bw.Write($bSize) # Height
    $bw.Write([byte]0) # Colors
    $bw.Write([byte]0) # Reserved
    $bw.Write([uint16]1) # Planes
    $bw.Write([uint16]32) # Bit count
    $bw.Write([uint32]$data.Length) # Bytes in res
    $bw.Write([uint32]$offset) # Offset

    $offset += $data.Length
}

for ($i = 0; $i -lt $sizes.Count; $i++) {
    $bw.Write($pngBytesList[$i])
}

$bw.Flush()
$bw.Close()
$fs.Close()

Write-Host "Generated multi-resolution icon at: $icoPath" -ForegroundColor Green

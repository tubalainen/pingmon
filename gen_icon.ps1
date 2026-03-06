Add-Type -AssemblyName System.Drawing

function New-PingMonBitmap([int]$Size) {
    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = [double]$Size / 32.0

    # Background circle - dark navy
    $m = [float]([Math]::Max(0.5, 0.5 * $s))
    $bg = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 22, 44, 90))
    $g.FillEllipse($bg, $m, $m, ($Size - 2*$m), ($Size - 2*$m))
    $bg.Dispose()

    # WiFi-style ping arcs opening upward
    # 270 deg = top in GDI+; anchor at bottom-center of bg circle
    $cx = $Size / 2.0
    $cy = $Size * 0.67

    $arcColor = [System.Drawing.Color]::FromArgb(255, 130, 195, 255)
    $lw = [float]([Math]::Max(1.1, 1.7 * $s))
    $pen = New-Object System.Drawing.Pen($arcColor, $lw)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round

    foreach ($rf in [double[]]@(3.0, 5.8, 8.6)) {
        $r = $rf * $s
        $rect = New-Object System.Drawing.RectangleF(($cx - $r), ($cy - $r), ($r * 2), ($r * 2))
        $g.DrawArc($pen, $rect, 210.0, 120.0)
    }
    $pen.Dispose()

    # Center dot
    $dotR = [float]([Math]::Max(1.4, 2.1 * $s))
    $dotBrush = New-Object System.Drawing.SolidBrush($arcColor)
    $g.FillEllipse($dotBrush, ($cx - $dotR), ($cy - $dotR), ($dotR * 2), ($dotR * 2))
    $dotBrush.Dispose()

    $g.Dispose()
    return $bmp
}

$sizes  = @(16, 32, 48, 256)
$bmps   = $sizes | ForEach-Object { New-PingMonBitmap -Size $_ }

# Collect PNG byte arrays
$pngData = @()
foreach ($bmp in $bmps) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $arr = $ms.ToArray()
    $pngData += ,$arr
    $ms.Dispose()
}

# Write ICO file
$icoPath = 'q:\Claude\pingmon\PingMon.ico'
$fs = [System.IO.File]::Create($icoPath)
$w  = New-Object System.IO.BinaryWriter($fs)

# ICO header
$w.Write([uint16]0)                   # Reserved
$w.Write([uint16]1)                   # Type: 1 = ICO
$w.Write([uint16]$sizes.Count)        # Number of images

# Directory entries (offset = header + all directory entries)
$offset = [uint32](6 + 16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]
    $wb = if ($sz -eq 256) { [byte]0 } else { [byte]$sz }
    $hb = if ($sz -eq 256) { [byte]0 } else { [byte]$sz }
    $w.Write($wb)                                   # Width  (0 = 256)
    $w.Write($hb)                                   # Height (0 = 256)
    $w.Write([byte]0)                               # ColorCount
    $w.Write([byte]0)                               # Reserved
    $w.Write([uint16]1)                             # Planes
    $w.Write([uint16]32)                            # BitCount
    $w.Write([uint32]$pngData[$i].Length)           # ImageSize
    $w.Write($offset)                               # ImageOffset
    $offset += [uint32]$pngData[$i].Length
}

# PNG image data
foreach ($data in $pngData) { $w.Write($data) }

$w.Dispose()
$fs.Dispose()
foreach ($bmp in $bmps) { $bmp.Dispose() }

$len = (Get-Item $icoPath).Length
Write-Host "Created PingMon.ico: $len bytes"

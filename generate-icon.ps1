$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot 'Assets\app.generated.ico'
$dir = Split-Path $out -Parent
New-Item -ItemType Directory -Force -Path $dir | Out-Null

$size = 256
$bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)

# Dark rounded-square background similar to the compact Task Manager icon.
$bg = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 35, 35, 35))
$border = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 105, 105, 105), 12)
$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$r = 42
$d = $r * 2
$rect = New-Object System.Drawing.Rectangle(18, 18, 220, 220)
$path.AddArc($rect.Left, $rect.Top, $d, $d, 180, 90)
$path.AddArc($rect.Right - $d, $rect.Top, $d, $d, 270, 90)
$path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
$path.AddArc($rect.Left, $rect.Bottom - $d, $d, $d, 90, 90)
$path.CloseFigure()
$g.FillPath($bg, $path)
$g.DrawPath($border, $path)

$white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 245, 245, 245))

# Upload arrow (left).
$up = [System.Drawing.Point[]]@(
    (New-Object System.Drawing.Point(104, 62)),
    (New-Object System.Drawing.Point(64, 116)),
    (New-Object System.Drawing.Point(88, 116)),
    (New-Object System.Drawing.Point(88, 178)),
    (New-Object System.Drawing.Point(120, 178)),
    (New-Object System.Drawing.Point(120, 116)),
    (New-Object System.Drawing.Point(144, 116))
)
$g.FillPolygon($white, $up)

# Download arrow (right).
$down = [System.Drawing.Point[]]@(
    (New-Object System.Drawing.Point(168, 78)),
    (New-Object System.Drawing.Point(168, 140)),
    (New-Object System.Drawing.Point(144, 140)),
    (New-Object System.Drawing.Point(184, 194)),
    (New-Object System.Drawing.Point(224, 140)),
    (New-Object System.Drawing.Point(200, 140)),
    (New-Object System.Drawing.Point(200, 78))
)
$g.FillPolygon($white, $down)

$hIcon = $bmp.GetHicon()
$icon = [System.Drawing.Icon]::FromHandle($hIcon)
$stream = [System.IO.File]::Open($out, [System.IO.FileMode]::Create)
try {
    $icon.Save($stream)
}
finally {
    $stream.Dispose()
    $icon.Dispose()
    $g.Dispose()
    $bmp.Dispose()
    $white.Dispose()
    $border.Dispose()
    $bg.Dispose()
    $path.Dispose()
}

if (-not (Test-Path $out) -or (Get-Item $out).Length -lt 1000) {
    throw 'The generated application icon is missing or invalid.'
}

Write-Host "Generated application icon: $out"

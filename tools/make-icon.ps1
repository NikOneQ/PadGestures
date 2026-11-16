# Draws the PadGestures icon and writes assets\app.ico (16–256 px) and assets\icon.png (512 px).
# Рисует иконку PadGestures: assets\app.ico (16–256 px) и assets\icon.png (512 px).
param([string]$OutDir = (Join-Path $PSScriptRoot '..\assets'))
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $OutDir | Out-Null

function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
  $p = New-Object Drawing.Drawing2D.GraphicsPath
  $d = $r * 2
  $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
  $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
  $p.CloseFigure(); return $p
}

function Draw([int]$size) {
  $bmp = New-Object Drawing.Bitmap $size, $size, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.Clear([Drawing.Color]::Transparent)
  $s = $size / 256.0
  $g.ScaleTransform($s, $s)

  # фон: «сквиркл» с градиентом фиолетовый → синий / background squircle with gradient
  $bg = RoundRect 8 8 240 240 64
  $grad = New-Object Drawing.Drawing2D.LinearGradientBrush ((New-Object Drawing.PointF 8, 8), (New-Object Drawing.PointF 248, 248), ([Drawing.Color]::FromArgb(255, 124, 92, 255)), ([Drawing.Color]::FromArgb(255, 34, 166, 242)))
  $g.FillPath($grad, $bg)
  # мягкий блик сверху / soft top highlight
  $hl = RoundRect 8 8 240 120 64
  $hlb = New-Object Drawing.Drawing2D.LinearGradientBrush ((New-Object Drawing.PointF 0, 8), (New-Object Drawing.PointF 0, 128), ([Drawing.Color]::FromArgb(60, 255, 255, 255)), ([Drawing.Color]::FromArgb(0, 255, 255, 255)))
  $g.FillPath($hlb, $hl)

  # двойная стрелка свайпа над тачпадом / swipe double arrow above the pad
  $ap = New-Object Drawing.Pen ([Drawing.Color]::White), 12
  $ap.StartCap = 'Round'; $ap.EndCap = 'Round'; $ap.LineJoin = 'Round'
  $g.DrawLine($ap, 80, 50, 176, 50)
  $g.DrawLines($ap, [Drawing.PointF[]]@((New-Object Drawing.PointF 95, 36), (New-Object Drawing.PointF 80, 50), (New-Object Drawing.PointF 95, 64)))
  $g.DrawLines($ap, [Drawing.PointF[]]@((New-Object Drawing.PointF 161, 36), (New-Object Drawing.PointF 176, 50), (New-Object Drawing.PointF 161, 64)))

  # тачпад / touchpad
  $padPath = RoundRect 40 88 176 128 28
  $g.FillPath((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(46, 255, 255, 255))), $padPath)
  $pen = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(235, 255, 255, 255)), 10
  $g.DrawPath($pen, $padPath)

  # подсвеченная верхняя зона / highlighted top zone
  $zone = RoundRect 54 102 148 34 17
  $g.FillPath((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(95, 255, 255, 255))), $zone)

  # след свайпа и палец в зоне / swipe trail and the finger inside the zone
  $g.FillEllipse((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(70, 255, 255, 255))), 82, 110, 18, 18)
  $g.FillEllipse((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(130, 255, 255, 255))), 106, 109, 20, 20)
  $g.FillEllipse((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(80, 255, 255, 255))), 132, 98, 42, 42)
  $g.FillEllipse([Drawing.Brushes]::White, 140, 106, 26, 26)

  $g.Dispose(); return $bmp
}

# PNG для README / PNG for README
$big = Draw 512; $big.Save((Join-Path $OutDir 'icon.png'), [Drawing.Imaging.ImageFormat]::Png); $big.Dispose()

# ICO с PNG-кадрами / ICO with PNG frames
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$frames = @()
foreach ($sz in $sizes) { $b = Draw $sz; $ms = New-Object IO.MemoryStream; $b.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $frames += ,$ms.ToArray(); $b.Dispose() }
$out = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $sz = $sizes[$i]; $dim = if ($sz -ge 256) { 0 } else { $sz }
  $w.Write([Byte]$dim); $w.Write([Byte]$dim); $w.Write([Byte]0); $w.Write([Byte]0)
  $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$frames[$i].Length); $w.Write([UInt32]$offset)
  $offset += $frames[$i].Length
}
foreach ($f in $frames) { $w.Write($f) }
$w.Flush(); [IO.File]::WriteAllBytes((Join-Path $OutDir 'app.ico'), $out.ToArray())
"icon written to $OutDir"

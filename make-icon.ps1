Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot 'Calculator.ico'
$preview = Join-Path $PSScriptRoot '_icon_preview.png'
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = New-Object System.Collections.ArrayList

function New-RoundedPath([double]$x, [double]$y, [double]$w, [double]$h, [double]$r) {
  $p = New-Object System.Drawing.Drawing2D.GraphicsPath
  $d = [float]($r * 2)
  $p.AddArc([float]$x, [float]$y, $d, $d, 180, 90)
  $p.AddArc([float]($x + $w - $d), [float]$y, $d, $d, 270, 90)
  $p.AddArc([float]($x + $w - $d), [float]($y + $h - $d), $d, $d, 0, 90)
  $p.AddArc([float]$x, [float]($y + $h - $d), $d, $d, 90, 90)
  $p.CloseFigure()
  return $p
}

function New-VGradient([System.Drawing.RectangleF]$rect, $c1, $c2) {
  return New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $c1, $c2, 90.0)
}

foreach ($s in $sizes) {
  $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
  $g.Clear([System.Drawing.Color]::Transparent)
  $k = $s / 64.0
  $g.ScaleTransform([float]$k, [float]$k)

  # body
  $bg = New-RoundedPath 0.5 0.5 63 63 15
  $bgBrush = New-VGradient (New-Object System.Drawing.RectangleF(0, 0, 64, 64)) `
    ([System.Drawing.Color]::FromArgb(255, 60, 60, 66)) `
    ([System.Drawing.Color]::FromArgb(255, 9, 9, 10))
  $g.FillPath($bgBrush, $bg)
  $bgBrush.Dispose()

  # top glass reflection
  $g.SetClip($bg)
  $gloss = New-VGradient (New-Object System.Drawing.RectangleF(0, 0, 64, 34)) `
    ([System.Drawing.Color]::FromArgb(26, 255, 255, 255)) `
    ([System.Drawing.Color]::FromArgb(0, 255, 255, 255))
  $g.FillRectangle($gloss, 0, 0, 64, 34)
  $gloss.Dispose()
  $g.ResetClip()

  # display
  $screen = New-RoundedPath 9 8 46 15.5 5
  $screenShadow = New-RoundedPath 9 9.2 46 15.5 5
  $shadowBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(60, 0, 0, 0))
  $g.FillPath($shadowBrush, $screenShadow)
  $shadowBrush.Dispose()
  $screenBrush = New-VGradient (New-Object System.Drawing.RectangleF(9, 8, 46, 15.5)) `
    ([System.Drawing.Color]::FromArgb(255, 255, 188, 84)) `
    ([System.Drawing.Color]::FromArgb(255, 255, 138, 0))
  $g.FillPath($screenBrush, $screen)
  $screenBrush.Dispose()

  $g.SetClip($screen)
  $sheen = New-VGradient (New-Object System.Drawing.RectangleF(9, 8, 46, 9)) `
    ([System.Drawing.Color]::FromArgb(90, 255, 255, 255)) `
    ([System.Drawing.Color]::FromArgb(0, 255, 255, 255))
  $g.FillRectangle($sheen, 9, 8, 46, 9)
  $sheen.Dispose()
  $g.ResetClip()

  # keys
  $keyCenters = @(
    @(17.5, 35.5), @(32.0, 35.5), @(46.5, 35.5),
    @(17.5, 49.5), @(32.0, 49.5), @(46.5, 49.5)
  )
  for ($i = 0; $i -lt $keyCenters.Count; $i++) {
    $cx = $keyCenters[$i][0]
    $cy = $keyCenters[$i][1]
    $r = 5.1
    $shadowBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(70, 0, 0, 0))
    $g.FillEllipse($shadowBrush, [float]($cx - $r), [float]($cy - $r + 0.7), [float](2 * $r), [float](2 * $r))
    $shadowBrush.Dispose()

    if ($i -eq 5) {
      $keyBrush = New-VGradient (New-Object System.Drawing.RectangleF(($cx - $r), ($cy - $r), (2 * $r), (2 * $r))) `
        ([System.Drawing.Color]::FromArgb(255, 255, 188, 84)) `
        ([System.Drawing.Color]::FromArgb(255, 255, 138, 0))
    } else {
      $keyBrush = New-VGradient (New-Object System.Drawing.RectangleF(($cx - $r), ($cy - $r), (2 * $r), (2 * $r))) `
        ([System.Drawing.Color]::FromArgb(255, 248, 248, 250)) `
        ([System.Drawing.Color]::FromArgb(255, 198, 198, 204))
    }
    $g.FillEllipse($keyBrush, [float]($cx - $r), [float]($cy - $r), [float](2 * $r), [float](2 * $r))
    $keyBrush.Dispose()
  }

  # inner border
  $border = New-RoundedPath 0.8 0.8 62.4 62.4 14.4
  $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(34, 255, 255, 255), 0.9)
  $g.DrawPath($pen, $border)
  $pen.Dispose()

  $g.ResetTransform()
  $g.Dispose()

  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  [void]$pngs.Add($ms.ToArray())
  $ms.Dispose()

  if ($s -eq 256) {
    $bmp.Save($preview, [System.Drawing.Imaging.ImageFormat]::Png)
  }
  $bmp.Dispose()
}

$stream = [System.IO.File]::Create($out)
$writer = New-Object System.IO.BinaryWriter($stream)
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $size = $sizes[$i]
  $dim = if ($size -ge 256) { 0 } else { $size }
  $writer.Write([Byte]$dim)
  $writer.Write([Byte]$dim)
  $writer.Write([Byte]0)
  $writer.Write([Byte]0)
  $writer.Write([UInt16]1)
  $writer.Write([UInt16]32)
  $writer.Write([UInt32]$pngs[$i].Length)
  $writer.Write([UInt32]$offset)
  $offset += $pngs[$i].Length
}
foreach ($png in $pngs) { $writer.Write($png) }
$writer.Flush()
$writer.Close()
$stream.Dispose()

Write-Output ("Created " + $out + " (" + (Get-Item $out).Length + " bytes)")

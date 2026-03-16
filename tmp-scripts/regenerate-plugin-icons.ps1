$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot\..
Add-Type -AssemblyName System.Drawing

function New-RoundedPath([float]$x,[float]$y,[float]$w,[float]$h,[float]$r) {
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $d = $r * 2
  $path.AddArc($x,$y,$d,$d,180,90)
  $path.AddArc($x + $w - $d,$y,$d,$d,270,90)
  $path.AddArc($x + $w - $d,$y + $h - $d,$d,$d,0,90)
  $path.AddArc($x,$y + $h - $d,$d,$d,90,90)
  $path.CloseFigure()
  return $path
}

function New-Icon([int]$size, [string]$targetPath) {
  $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $shadowBrush = $null
  $faceBrush = $null
  $navyBrush = $null
  $tealBrush = $null
  $borderPen = $null
  $deckPen = $null
  $archPen = $null
  $cablePen = $null

  try {
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)

    $scale = $size / 96.0
    $mist = [System.Drawing.Color]::FromArgb(241,238,232)
    $mistBorder = [System.Drawing.Color]::FromArgb(222,216,205)
    $navy = [System.Drawing.Color]::FromArgb(31,41,55)
    $gold = [System.Drawing.Color]::FromArgb(196,145,58)
    $teal = [System.Drawing.Color]::FromArgb(22,163,151)
    $shadow = [System.Drawing.Color]::FromArgb(26,17,24,39)

    $shadowBrush = New-Object System.Drawing.SolidBrush($shadow)
    $faceBrush = New-Object System.Drawing.SolidBrush($mist)
    $navyBrush = New-Object System.Drawing.SolidBrush($navy)
    $tealBrush = New-Object System.Drawing.SolidBrush($teal)
    $borderPen = New-Object System.Drawing.Pen($mistBorder, (2.2 * $scale))
    $deckPen = New-Object System.Drawing.Pen($navy, (6.5 * $scale))
    $archPen = New-Object System.Drawing.Pen($gold, (5.0 * $scale))
    $cablePen = New-Object System.Drawing.Pen($teal, (3.0 * $scale))

    $deckPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $deckPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $archPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $archPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $archPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $cablePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $cablePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

    $outerX = 6 * $scale
    $outerY = 6 * $scale
    $outerW = $size - (12 * $scale)
    $outerH = $size - (12 * $scale)
    $g.FillEllipse($shadowBrush, $outerX + (2 * $scale), $outerY + (3 * $scale), $outerW, $outerH)
    $g.FillEllipse($faceBrush, $outerX, $outerY, $outerW, $outerH)
    $g.DrawEllipse($borderPen, $outerX, $outerY, $outerW, $outerH)

    $deckY = 58 * $scale
    $g.DrawLine($deckPen, 22 * $scale, $deckY, 74 * $scale, $deckY)

    $path = New-RoundedPath (43 * $scale) (26 * $scale) (10 * $scale) (36 * $scale) (4 * $scale)
    try { $g.FillPath($navyBrush, $path) } finally { $path.Dispose() }
    $path = New-RoundedPath (20 * $scale) (48 * $scale) (7 * $scale) (16 * $scale) (3 * $scale)
    try { $g.FillPath($navyBrush, $path) } finally { $path.Dispose() }
    $path = New-RoundedPath (69 * $scale) (48 * $scale) (7 * $scale) (16 * $scale) (3 * $scale)
    try { $g.FillPath($navyBrush, $path) } finally { $path.Dispose() }

    $g.DrawBezier($archPen, 21 * $scale, $deckY, 28 * $scale, 34 * $scale, 38 * $scale, 24 * $scale, 48 * $scale, 24 * $scale)
    $g.DrawBezier($archPen, 48 * $scale, 24 * $scale, 58 * $scale, 24 * $scale, 68 * $scale, 34 * $scale, 75 * $scale, $deckY)

    $g.DrawLine($cablePen, 31 * $scale, 40 * $scale, 31 * $scale, 56 * $scale)
    $g.DrawLine($cablePen, 40 * $scale, 30 * $scale, 40 * $scale, 56 * $scale)
    $g.DrawLine($cablePen, 56 * $scale, 30 * $scale, 56 * $scale, 56 * $scale)
    $g.DrawLine($cablePen, 65 * $scale, 40 * $scale, 65 * $scale, 56 * $scale)
    $g.FillEllipse($tealBrush, 69 * $scale, 18 * $scale, 11 * $scale, 11 * $scale)

    $bmp.Save($targetPath, [System.Drawing.Imaging.ImageFormat]::Png)
  }
  finally {
    if ($shadowBrush) { $shadowBrush.Dispose() }
    if ($faceBrush) { $faceBrush.Dispose() }
    if ($navyBrush) { $navyBrush.Dispose() }
    if ($tealBrush) { $tealBrush.Dispose() }
    if ($borderPen) { $borderPen.Dispose() }
    if ($deckPen) { $deckPen.Dispose() }
    if ($archPen) { $archPen.Dispose() }
    if ($cablePen) { $cablePen.Dispose() }
    $g.Dispose()
    $bmp.Dispose()
  }
}

$assetDir = (Resolve-Path "src/BN.WorkflowDoc.XrmToolBox/Assets").Path
$smallPath = Join-Path $assetDir "BridgeNexa-Plugin-32.png"
$bigPath = Join-Path $assetDir "BridgeNexa-Plugin-96.png"
$txtPath = Join-Path $assetDir "plugin-icon-base64.txt"

New-Icon 32 $smallPath
New-Icon 96 $bigPath

$small64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($smallPath))
$big64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($bigPath))
Set-Content -Path $txtPath -Value @("SMALL=$small64", "BIG=$big64")

Get-Item $smallPath, $bigPath, $txtPath | Select-Object Name, Length, LastWriteTime

Add-Type -AssemblyName PresentationCore

$accent = [System.Windows.Media.Color]::FromRgb(0x4C, 0xC2, 0xFF)
$bg     = [System.Windows.Media.Color]::FromRgb(0x2A, 0x31, 0x40)

function New-IconPng([int]$size) {
  $dv = New-Object System.Windows.Media.DrawingVisual
  $dc = $dv.RenderOpen()
  $k = $size / 32.0

  $plate = New-Object System.Windows.Media.RectangleGeometry ( New-Object System.Windows.Rect (1.5*$k), (1.5*$k), (29*$k), (29*$k) ), (7*$k), (7*$k)
  $bgBrush = New-Object System.Windows.Media.SolidColorBrush $bg
  $bgBrush.Freeze()
  $dc.DrawGeometry($bgBrush, $null, $plate)

  $accentBrush = New-Object System.Windows.Media.SolidColorBrush $accent
  $accentBrush.Freeze()
  $pen = New-Object System.Windows.Media.Pen $accentBrush, (3*$k)
  $pen.Freeze()
  $dc.DrawLine($pen, (New-Object System.Windows.Point (16*$k), (6*$k)), (New-Object System.Windows.Point (16*$k), (11*$k)))

  $body = New-Object System.Windows.Media.RectangleGeometry ( New-Object System.Windows.Rect (13*$k), (11*$k), (6*$k), (12*$k) ), (2*$k), (2*$k)
  $dc.DrawGeometry($null, $pen, $body)

  $stub = New-Object System.Windows.Media.RectangleGeometry ( New-Object System.Windows.Rect (14.5*$k), (23*$k), (3*$k), (5*$k) ), (1.5*$k), (1.5*$k)
  $dc.DrawGeometry($accentBrush, $null, $stub)
  $dc.Close()

  $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
  $rtb.Render($dv)
  $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
  $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
  $ms = New-Object System.IO.MemoryStream
  $enc.Save($ms)
  , $ms.ToArray()
}

function Write-Ico([string]$path) {
  $sizes = @(16, 24, 32, 48, 256)
  $pngs = @{}
  foreach ($s in $sizes) { $pngs[$s] = New-IconPng $s }

  $ms = New-Object System.IO.MemoryStream
  $bw = New-Object System.IO.BinaryWriter $ms
  $bw.Write([uint16]0)   # reserved
  $bw.Write([uint16]1)   # icon
  $bw.Write([uint16]$sizes.Count)

  $offset = 6 + 16 * $sizes.Count
  foreach ($s in $sizes) {
    $b = $pngs[$s]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim)  # width
    $bw.Write([byte]$dim)  # height
    $bw.Write([byte]0)     # palette
    $bw.Write([byte]0)     # reserved
    $bw.Write([uint16]1)   # planes
    $bw.Write([uint16]32)  # bpp
    $bw.Write([uint32]$b.Length)
    $bw.Write([uint32]$offset)
    $offset += $b.Length
  }
  foreach ($s in $sizes) { $bw.Write($pngs[$s]) }
  $bw.Flush()
  [System.IO.File]::WriteAllBytes($path, $ms.ToArray())
}

Write-Ico (Join-Path $PSScriptRoot "..\src\USBControl.App\Assets\usb.ico")
Write-Host "usb.ico written"

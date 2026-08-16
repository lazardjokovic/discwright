<#
  PARKED - not part of the app.

  Printable disc-face generator, pulled out of DiscWright.ps1 to keep the tool to
  one job: produce an ISO. Printing a disc is the printer software's business
  (Epson Print CD, Canon Easy-PhotoPrint, Nero CoverDesigner) - that software already
  does circular layout, text, inner/outer print diameters and, crucially, the
  printer-specific tray alignment we should never try to replicate. This only ever
  produced an image; it never printed anything and never touched the ISO.

  Kept because it works and matches the menu's styling, so a future "make the printed
  face match the on-screen menu" feature can start from here rather than from scratch.

  To use standalone:
      . .\extras\DiscLabel.ps1
      New-DiscLabel 'C:\art\wallpaper.jpg' 'The Witcher - Enhanced Edition' 'C:\out\label.png'

  Output: square PNG, 120 mm at 300 dpi (1417 px), transparent centre hub and
  transparent outside the rim.
#>

Add-Type -AssemblyName System.Drawing

# $unit matters: Font sizes in POINTS by default, so on a 300 dpi bitmap a "size"
# meant as pixels comes out ~4x too big.
function New-TitleFont([single]$size,[System.Drawing.GraphicsUnit]$unit=[System.Drawing.GraphicsUnit]::Point) {
    try { return New-Object System.Drawing.Font("Bahnschrift SemiBold",$size,[System.Drawing.FontStyle]::Bold,$unit) }
    catch { return New-Object System.Drawing.Font("Segoe UI",$size,[System.Drawing.FontStyle]::Bold,$unit) }
}

function Clear-ReadOnly([string]$path) {
    if ([string]::IsNullOrWhiteSpace($path) -or -not (Test-Path $path)) { return }
    try {
        $item = Get-Item -LiteralPath $path -Force
        if (-not $item.PSIsContainer -and $item.IsReadOnly) { $item.IsReadOnly = $false }
    } catch {}
}

function Save-PngAtomic($bmp,[string]$outPng) {
    $tmp = [IO.Path]::Combine([IO.Path]::GetTempPath(), ([Guid]::NewGuid().ToString('N')+'.png'))
    $bmp.Save($tmp,[System.Drawing.Imaging.ImageFormat]::Png)
    Clear-ReadOnly $outPng
    Move-Item -LiteralPath $tmp -Destination $outPng -Force
}

# Printable disc face. Standard disc is 120 mm across; the centre is left fully
# transparent so printing software masks the hub correctly. Output carries a real
# 300 dpi pHYs header so Nero CoverDesigner / Epson Print CD size it without scaling.
function New-DiscLabel([string]$imgPath,[string]$title,[string]$outPng,[double]$outerMm=120,[double]$innerMm=36,[int]$dpi=300) {
    $px    = [int][math]::Round($outerMm/25.4*$dpi)
    $inner = [int][math]::Round($innerMm/25.4*$dpi)
    $bmp = New-Object System.Drawing.Bitmap($px,$px,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bmp.SetResolution($dpi,$dpi)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode='AntiAlias'; $g.InterpolationMode='HighQualityBicubic'
    $g.PixelOffsetMode='HighQuality'; $g.TextRenderingHint='AntiAlias'

    # Donut-shaped clip: one region covering outer edge and centre hole at once,
    # so nothing drawn afterwards can spill onto the hub or past the rim.
    $ring = New-Object System.Drawing.Drawing2D.GraphicsPath
    $ring.FillMode = 'Alternate'
    $ring.AddEllipse(0,0,$px,$px)
    $off = ($px-$inner)/2
    $ring.AddEllipse($off,$off,$inner,$inner)
    $g.Clip = New-Object System.Drawing.Region($ring)

    # artwork, cover-scaled to fill the disc
    $img=[System.Drawing.Image]::FromFile($imgPath)
    $scale=[math]::Max($px/$img.Width,$px/$img.Height)
    $sw=[int]($img.Width*$scale); $sh=[int]($img.Height*$scale)
    $g.DrawImage($img,[int](($px-$sw)/2),[int](($px-$sh)/2),$sw,$sh)
    $img.Dispose()

    # darken the lower half so the title stays readable over any artwork
    $grRect = New-Object System.Drawing.Rectangle(0,([int]($px*0.42)),$px,([int]($px*0.58)))
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush($grRect,
        [System.Drawing.Color]::FromArgb(0,0,0,0),[System.Drawing.Color]::FromArgb(220,2,5,7),90.0)
    $g.FillRectangle($grad,$grRect)

    # title, shrunk until it fits the chord at that height
    # NB: do not call this $px - PowerShell variables are case-insensitive and it
    # would clobber the $px dimension above.
    $unitPx = [System.Drawing.GraphicsUnit]::Pixel
    $maxW = $px*0.74
    $size = [single]($px*0.058)
    $f = New-TitleFont $size $unitPx
    while ($size -gt ($px*0.022) -and $g.MeasureString($title,$f).Width -gt $maxW) {
        $f.Dispose(); $size = [single]($size - $px*0.002); $f = New-TitleFont $size $unitPx
    }
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment='Center'; $sf.LineAlignment='Center'
    $ty = [single]($px*0.70)
    $shadowRect = New-Object System.Drawing.RectangleF(([single]($px*0.003)),([single]($ty-$size+$px*0.003)),([single]$px),([single]($size*2)))
    $titleRect  = New-Object System.Drawing.RectangleF(0,([single]($ty-$size)),([single]$px),([single]($size*2)))
    $g.DrawString($title,$f,(New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(190,0,0,0))),$shadowRect,$sf)
    $g.DrawString($title,$f,(New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(240,235,245,248))),$titleRect,$sf)
    $f.Dispose()

    # accent ring, echoing the menu
    $inset = [single]($px*0.018)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(200,0,190,200),([single]($px*0.004)))
    $g.DrawEllipse($pen,$inset,$inset,([single]($px-2*$inset)),([single]($px-2*$inset)))

    $g.Dispose()
    Save-PngAtomic $bmp $outPng
    $bmp.Dispose()
}

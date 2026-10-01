<#
  Print artwork for a disc: the face that goes on the disc, and the wrap that
  goes in the case.

  WHY THIS EXISTS

  The public guide to making your own game discs spends most of its length in a
  design application: find a manufacturer's template, build a 7x7 canvas with a
  circular mask for the disc, build a wrap from a back panel, a spine and a
  front panel, then fight the printer's "expansion" slider so it stops scaling
  the page. DiscWright already knows the artwork, the title and the game, so all
  of that is arithmetic it can do.

  WHAT IT DELIBERATELY DOES NOT DO

  It does not print, and it does not align anything to a tray. Printer software
  owns that, it is specific to the printer, and getting it wrong wastes a disc.
  This produces correctly sized artwork and hands it over.

  WHY PDF

  A PDF page carries its true physical size, so "print at 100%, no scaling" is a
  thing the driver can honour. A PNG carries pixels and a dpi tag that printers
  routinely ignore, which is the whole reason that guide tells people to drag a
  slider to Min. Disc faces come out as PNG as well, because printer software
  for printable discs wants an image.

  MEASURED, NOT ASSUMED

  Sources disagree about the wrap: 273 x 183 mm and 273 x 181 mm are both
  published. New-CalibrationSheet prints the candidates as outlines to hold
  against a real case, which is how the number gets settled here.
#>

Add-Type -AssemblyName System.Drawing

# ---------------------------------------------------------------- the formats

# One table, because "all the combinations" is only cheap if every combination
# is a row rather than a code path. Everything below reads from it, and so do
# the tests, so a new case size is one row and nothing else.
#
# A wrap is a back panel, a spine and a front panel on one sheet. The panel is
# narrower than the case, and the wrap shorter, because it sits inside the
# sleeve: a standard 190 x 135 mm case takes a 129 mm panel and a 183 mm height.
# WrapWidthMm is the published trim rather than something derived, because the
# panel is what is left over once the spine is taken out, not the other way
# round, and the published widths do not always divide evenly.
$script:CaseFormats = @(
    @{ Key = 'dvd';        Name = 'DVD case, standard 14 mm'
       WrapWidthMm = 273; HeightMm = 183; SpineMm = 14; Verified = $false }
    @{ Key = 'dvd-slim';   Name = 'DVD case, slim 7 mm'
       WrapWidthMm = 266; HeightMm = 183; SpineMm = 7;  Verified = $false }
    @{ Key = 'dvd-double'; Name = 'DVD case, double 14 mm'
       WrapWidthMm = 273; HeightMm = 183; SpineMm = 14; Verified = $false }
    @{ Key = 'bluray';     Name = 'Blu-ray case, 12 mm'
       WrapWidthMm = 270; HeightMm = 164; SpineMm = 12; Verified = $false }
    @{ Key = 'cd';         Name = 'CD jewel case, front insert'
       WrapWidthMm = 120; HeightMm = 120; SpineMm = 0;  Verified = $false }
)

# The disc itself. Inner is where printing starts: a hub-printable disc is
# printable almost to the centre ring, an ordinary one leaves a wide clear hub,
# and an adhesive label has a hole punched in it.
$script:DiscFormats = @(
    @{ Key = 'hub';     Name = 'Hub-printable inkjet disc'
       InnerMm = 22; OuterMm = 118; Verified = $true }
    @{ Key = 'inkjet';  Name = 'Ordinary printable inkjet disc'
       InnerMm = 36; OuterMm = 118; Verified = $false }
    @{ Key = 'label';   Name = 'Full-face adhesive label'
       InnerMm = 41; OuterMm = 117; Verified = $false }
)

$script:PageSizes = @{
    a4     = @{ WidthMm = 210.0; HeightMm = 297.0 }
    letter = @{ WidthMm = 215.9; HeightMm = 279.4 }
}

function Get-CaseFormat([string]$key) {
    $f = $script:CaseFormats | Where-Object { $_.Key -eq $key }
    if (-not $f) { throw "No case format '$key'. Known: $(($script:CaseFormats.Key) -join ', ')" }
    return $f
}

function Get-DiscFormat([string]$key) {
    $f = $script:DiscFormats | Where-Object { $_.Key -eq $key }
    if (-not $f) { throw "No disc format '$key'. Known: $(($script:DiscFormats.Key) -join ', ')" }
    return $f
}

# What is left for each panel once the spine is taken out of the trim width.
function Get-PanelWidthMm($case) { return ($case.WrapWidthMm - $case.SpineMm) / 2.0 }

# ---------------------------------------------------------------- units

# 300 dpi is what the guide's workflow assumes and what photo printers want.
$script:Dpi = 300

function ConvertTo-Px([double]$mm, [int]$dpi = $script:Dpi) {
    return [int][Math]::Round($mm / 25.4 * $dpi)
}

# A PDF point is 1/72 inch, and the page box is what makes "print at 100%" mean
# something.
function ConvertTo-Points([double]$mm) { return [Math]::Round($mm / 25.4 * 72, 4) }

# ---------------------------------------------------------------- the PDF

<#
    A one-page PDF holding a single JPEG at an exact physical size.

    Written by hand rather than with a library because the app takes no
    dependencies, and because this is the simplest useful PDF there is: a
    catalogue, a page, a content stream that draws one image, and the image.
    JPEG rides in as-is under /DCTDecode, so nothing here has to compress
    anything.
#>
function Export-ImageAsPdf {
    param(
        [Parameter(Mandatory)][System.Drawing.Bitmap]$Image,
        [Parameter(Mandatory)][string]$OutPdf,
        [Parameter(Mandatory)][double]$PageWidthMm,
        [Parameter(Mandatory)][double]$PageHeightMm,
        [int]$Quality = 92
    )
    $jpeg = ConvertTo-JpegBytes $Image $Quality
    $w = ConvertTo-Points $PageWidthMm
    $h = ConvertTo-Points $PageHeightMm

    $objects = New-Object System.Collections.ArrayList
    $null = $objects.Add("<< /Type /Catalog /Pages 2 0 R >>")
    $null = $objects.Add("<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
    $null = $objects.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 $w $h] " +
                         "/Resources << /XObject << /Im0 5 0 R >> >> /Contents 4 0 R >>")
    # The image fills the page exactly: scale by the page box, no offset.
    $content = "q`n$w 0 0 $h 0 0 cm`n/Im0 Do`nQ`n"
    $null = $objects.Add("<< /Length $($content.Length) >>`nstream`n$content`nendstream")
    $null = $objects.Add("<< /Type /XObject /Subtype /Image /Width $($Image.Width) " +
                         "/Height $($Image.Height) /ColorSpace /DeviceRGB /BitsPerComponent 8 " +
                         "/Filter /DCTDecode /Length $($jpeg.Length) >>`nstream`n%%JPEG%%`nendstream")

    $out = New-Object System.IO.MemoryStream
    $ascii = [Text.Encoding]::ASCII
    function Write-Ascii($stream, [string]$text) {
        $bytes = $ascii.GetBytes($text); $stream.Write($bytes, 0, $bytes.Length)
    }

    Write-Ascii $out "%PDF-1.4`n"
    # A binary comment, so anything moving this file treats it as binary.
    $out.Write([byte[]](0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A), 0, 6)

    $offsets = @(0)
    for ($i = 0; $i -lt $objects.Count; $i++) {
        $offsets += [int]$out.Position
        $body = $objects[$i]
        if ($body -match '%%JPEG%%') {
            $parts = $body -split '%%JPEG%%'
            Write-Ascii $out "$($i + 1) 0 obj`n$($parts[0])"
            $out.Write($jpeg, 0, $jpeg.Length)
            Write-Ascii $out "$($parts[1])`nendobj`n"
        } else {
            Write-Ascii $out "$($i + 1) 0 obj`n$body`nendobj`n"
        }
    }

    $xref = [int]$out.Position
    Write-Ascii $out "xref`n0 $($objects.Count + 1)`n0000000000 65535 f `n"
    for ($i = 1; $i -le $objects.Count; $i++) {
        Write-Ascii $out ("{0:0000000000} 00000 n `n" -f $offsets[$i])
    }
    Write-Ascii $out ("trailer`n<< /Size $($objects.Count + 1) /Root 1 0 R >>`n" +
                      "startxref`n$xref`n%%EOF`n")

    [IO.File]::WriteAllBytes($OutPdf, $out.ToArray())
    $out.Dispose()
    return $OutPdf
}

function ConvertTo-JpegBytes([System.Drawing.Bitmap]$image, [int]$quality) {
    $codec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() |
             Where-Object { $_.MimeType -eq 'image/jpeg' }
    $params = New-Object System.Drawing.Imaging.EncoderParameters 1
    $params.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter(
        [System.Drawing.Imaging.Encoder]::Quality, [long]$quality)
    # Flattened onto white: a JPEG has no alpha, and a disc face is drawn with a
    # transparent hub.
    $flat = New-Object System.Drawing.Bitmap $image.Width, $image.Height,
                       ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($flat)
    $g.Clear([System.Drawing.Color]::White)
    $g.DrawImage($image, 0, 0, $image.Width, $image.Height)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $flat.Save($ms, $codec, $params)
    $flat.Dispose()
    $bytes = $ms.ToArray()
    $ms.Dispose()
    return $bytes
}

# ---------------------------------------------------------------- calibration

<#
    A sheet to print and hold against a real case, because the published numbers
    disagree and a wrap that is 2 mm too tall jams in the sleeve.

    It draws the candidate trim rectangles, the spine band, and a ruler along
    each edge. Nothing on it is artwork: it exists to be measured and thrown
    away, and whatever it settles becomes the number in the table above.
#>
function New-CalibrationSheet {
    param(
        [Parameter(Mandatory)][string]$OutPdf,
        [string]$Case = 'dvd',
        [ValidateSet('a4', 'letter')][string]$Page = 'a4',
        # The candidates worth testing, widest and tallest first. Published
        # sources give 273 x 183 and 272 x 181 for the same case.
        [double[]]$WidthsMm = @(273, 272),
        [double[]]$HeightsMm = @(183, 181)
    )
    $fmt = Get-CaseFormat $Case
    $paper = $script:PageSizes[$Page]
    # Landscape: a 273 mm wrap does not fit across a portrait page.
    $pageW = $paper.HeightMm; $pageH = $paper.WidthMm

    $bmp = New-Object System.Drawing.Bitmap (ConvertTo-Px $pageW), (ConvertTo-Px $pageH)
    $bmp.SetResolution($script:Dpi, $script:Dpi)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::White)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit

    $solid = New-Object System.Drawing.Pen ([System.Drawing.Color]::Black), 3
    $dashed = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(120, 120, 120)), 3
    $dashed.DashStyle = [System.Drawing.Drawing2D.DashStyle]::Dash
    $hair = New-Object System.Drawing.Pen ([System.Drawing.Color]::Black), 2
    $font = New-Object System.Drawing.Font 'Segoe UI', 8, ([System.Drawing.GraphicsUnit]::Point)
    $bold = New-Object System.Drawing.Font 'Segoe UI', 9, ([System.Drawing.FontStyle]::Bold),
                       ([System.Drawing.GraphicsUnit]::Point)
    $ink = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::Black)
    $gry = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(120, 120, 120))

    $cx = (ConvertTo-Px $pageW) / 2
    $cy = (ConvertTo-Px $pageH) / 2

    # Candidate A is drawn solid, candidate B dashed, and their labels sit in
    # opposite corners: two rectangles 2 mm apart read as one line otherwise,
    # which is exactly what the first version of this sheet did.
    $pairs = @()
    for ($i = 0; $i -lt [Math]::Max($WidthsMm.Count, $HeightsMm.Count); $i++) {
        $pairs += , @{
            W = $WidthsMm[[Math]::Min($i, $WidthsMm.Count - 1)]
            H = $HeightsMm[[Math]::Min($i, $HeightsMm.Count - 1)]
            Tag = [char](65 + $i)
        }
    }

    $n = 0
    foreach ($p in $pairs) {
        $w = ConvertTo-Px $p.W; $h = ConvertTo-Px $p.H
        $x = $cx - ($w / 2); $y = $cy - ($h / 2)
        $pen = if ($n -eq 0) { $solid } else { $dashed }
        $brush = if ($n -eq 0) { $ink } else { $gry }
        $g.DrawRectangle($pen, $x, $y, $w, $h)
        $label = "$($p.Tag): $($p.W) x $($p.H) mm"
        if ($n -eq 0) {
            $g.DrawString($label, $bold, $brush, [single]($x + (ConvertTo-Px 4.0)),
                          [single]($y + (ConvertTo-Px 3.0)))
        } else {
            $size = $g.MeasureString($label, $bold)
            $g.DrawString($label, $bold, $brush,
                          [single]($x + $w - $size.Width - (ConvertTo-Px 4.0)),
                          [single]($y + $h - $size.Height - (ConvertTo-Px 3.0)))
        }
        $n++
    }

    # The spine band, centred on the widest candidate, which is the other thing
    # to hold against a case.
    $spineW = ConvertTo-Px $fmt.SpineMm
    $tall = ConvertTo-Px ($HeightsMm | Measure-Object -Maximum).Maximum
    $g.DrawRectangle($hair, $cx - ($spineW / 2), $cy - ($tall / 2), $spineW, $tall)
    $g.DrawString("spine $($fmt.SpineMm) mm", $font, $ink,
                  [single]($cx + ($spineW / 2) + (ConvertTo-Px 2.0)), [single]($cy))

    # Rulers in both directions, inside the left panel where nothing else is
    # drawn. If either does not measure what it says, the page was scaled and
    # nothing else on the sheet means anything.
    $rx = $cx - (ConvertTo-Px (($WidthsMm | Measure-Object -Maximum).Maximum / 2)) + (ConvertTo-Px 12.0)
    $ry = $cy - (ConvertTo-Px 20.0)
    for ($mm = 0; $mm -le 100; $mm++) {
        $x = $rx + (ConvertTo-Px ([double]$mm))
        $len = if ($mm % 10 -eq 0) { 6.0 } elseif ($mm % 5 -eq 0) { 4.0 } else { 2.0 }
        $g.DrawLine($hair, $x, $ry, $x, $ry - (ConvertTo-Px $len))
    }
    $g.DrawLine($hair, $rx, $ry, $rx + (ConvertTo-Px 100.0), $ry)
    $g.DrawString('100 mm across. Measure this first.', $font, $ink,
                  [single]$rx, [single]($ry + (ConvertTo-Px 2.0)))

    $vy = $ry + (ConvertTo-Px 14.0)
    for ($mm = 0; $mm -le 50; $mm++) {
        $y = $vy + (ConvertTo-Px ([double]$mm))
        $len = if ($mm % 10 -eq 0) { 6.0 } elseif ($mm % 5 -eq 0) { 4.0 } else { 2.0 }
        $g.DrawLine($hair, $rx, $y, $rx + (ConvertTo-Px $len), $y)
    }
    $g.DrawLine($hair, $rx, $vy, $rx, $vy + (ConvertTo-Px 50.0))
    $g.DrawString('50 mm down.', $font, $ink,
                  [single]($rx + (ConvertTo-Px 8.0)), [single]($vy + (ConvertTo-Px 20.0)))

    # The heading sits in the page margin, clear of every outline.
    $g.DrawString("$($fmt.Name)  -  print at 100%, no scaling, then hold the outlines against a real case",
                  $font, $ink, [single](ConvertTo-Px 6.0), [single](ConvertTo-Px 4.0))

    foreach ($d in @($g, $solid, $dashed, $hair, $font, $bold, $ink, $gry)) { $d.Dispose() }
    $null = Export-ImageAsPdf -Image $bmp -OutPdf $OutPdf -PageWidthMm $pageW -PageHeightMm $pageH
    $bmp.Dispose()
    return $OutPdf
}

# ---------------------------------------------------------------- the artwork

<#
    Type that fits, rather than type that is the size somebody guessed.

    A game title can be "Gothic" or it can be "The Chronicles of Riddick:
    Escape from Butcher Bay", and on a 14 mm spine the difference is whether
    the words are there at all. So the size comes down until it fits, and the
    caller says how small is too small.
#>
function Get-FittedFont {
    param(
        [Parameter(Mandatory)][System.Drawing.Graphics]$Graphics,
        [Parameter(Mandatory)][string]$Text,
        [Parameter(Mandatory)][double]$MaxWidthPx,
        [Parameter(Mandatory)][double]$StartPt,
        [double]$MinPt = 6,
        [string]$Family = 'Bahnschrift SemiBold',
        [System.Drawing.FontStyle]$Style = [System.Drawing.FontStyle]::Regular
    )
    # Bahnschrift ships with Windows 10 and later and is condensed, which buys
    # room on a spine. Segoe UI is everywhere, so it catches the rest.
    $chosen = $Family
    try { $probe = New-Object System.Drawing.FontFamily $chosen; $probe.Dispose() }
    catch { $chosen = 'Segoe UI' }

    for ($pt = $StartPt; $pt -ge $MinPt; $pt -= 0.5) {
        $font = New-Object System.Drawing.Font $chosen, $pt, $Style,
                           ([System.Drawing.GraphicsUnit]::Point)
        if ($Graphics.MeasureString($Text, $font).Width -le $MaxWidthPx) { return $font }
        $font.Dispose()
    }
    return (New-Object System.Drawing.Font $chosen, $MinPt, $Style,
                       ([System.Drawing.GraphicsUnit]::Point))
}

<#
    Fill a rectangle with a picture without squashing it: scale to cover, then
    crop what hangs over. Cover art is almost never the shape of the panel it
    has to fill, and a stretched cover is the first thing that looks homemade.
#>
function Set-ImageCover {
    param(
        [Parameter(Mandatory)][System.Drawing.Graphics]$Graphics,
        [Parameter(Mandatory)][System.Drawing.Image]$Image,
        [Parameter(Mandatory)][System.Drawing.RectangleF]$Target
    )
    $scale = [Math]::Max($Target.Width / $Image.Width, $Target.Height / $Image.Height)
    $srcW = $Target.Width / $scale
    $srcH = $Target.Height / $scale
    $src = New-Object System.Drawing.RectangleF(
        ($Image.Width - $srcW) / 2, ($Image.Height - $srcH) / 2, $srcW, $srcH)
    $Graphics.DrawImage($Image, $Target, $src, [System.Drawing.GraphicsUnit]::Pixel)
}

function ConvertFrom-HexColour([string]$hex) {
    $h = $hex.TrimStart([char]35)
    return [System.Drawing.Color]::FromArgb(
        [Convert]::ToInt32($h.Substring(0, 2), 16),
        [Convert]::ToInt32($h.Substring(2, 2), 16),
        [Convert]::ToInt32($h.Substring(4, 2), 16))
}

<#
    The face that goes on the disc.

    Output is a square PNG of the whole 120 mm disc at 300 dpi, because that is
    what Epson Photo+ and Canon Easy-PhotoPrint want: you hand them a picture
    and they own the diameters and the tray. The hub is left transparent so
    their circular mask and ours agree, and so the clear inner ring of the disc
    does not come out covered in ink that then has to be wiped off.

    The art is composed for the printable ring of the disc format asked for,
    22 mm to 118 mm on a hub-printable disc and a much wider clear hub on an
    ordinary one. Composing for the wrong one puts the title under the clamp.
#>
function New-DiscFace {
    param(
        [Parameter(Mandatory)][string]$OutPng,
        [Parameter(Mandatory)][string]$Title,
        [string]$Subtitle,
        [string]$CoverImage,
        [string]$Disc = 'hub',
        [string]$Accent = '#1B2A41'
    )
    $fmt = Get-DiscFormat $Disc
    $side = ConvertTo-Px 120.0
    $bmp = New-Object System.Drawing.Bitmap $side, $side,
                      ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bmp.SetResolution($script:Dpi, $script:Dpi)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
    $g.Clear([System.Drawing.Color]::Transparent)

    # Not $accent: a local differing from a parameter only by case IS that
    # parameter, and [string]$Accent would turn this Color back into text.
    $accentColour = ConvertFrom-HexColour $Accent
    $centre = $side / 2.0
    $outerR = (ConvertTo-Px $fmt.OuterMm) / 2.0
    $innerR = (ConvertTo-Px $fmt.InnerMm) / 2.0

    # Everything is drawn inside the printable ring, so nothing lands on the
    # clamp and nothing has to be wiped off afterwards.
    $ring = New-Object System.Drawing.Drawing2D.GraphicsPath
    $ring.AddEllipse([single]($centre - $outerR), [single]($centre - $outerR),
                     [single]($outerR * 2), [single]($outerR * 2))
    $g.SetClip($ring)

    $face = New-Object System.Drawing.RectangleF(
        [single]($centre - $outerR), [single]($centre - $outerR),
        [single]($outerR * 2), [single]($outerR * 2))

    if ($CoverImage) {
        if (-not (Test-Path -LiteralPath $CoverImage)) { throw "No cover image at '$CoverImage'" }
        $img = [System.Drawing.Image]::FromFile((Resolve-Path -LiteralPath $CoverImage).Path)
        try { Set-ImageCover -Graphics $g -Image $img -Target $face } finally { $img.Dispose() }
    } else {
        $dark = [System.Drawing.Color]::FromArgb(
            [Math]::Max(0, $accentColour.R - 40), [Math]::Max(0, $accentColour.G - 40),
            [Math]::Max(0, $accentColour.B - 40))
        # ::new rather than New-Object: PowerShell picks the Rectangle
        # overload for a RectangleF and then fails converting a Color to an
        # Int32, which it reports as a colour problem rather than an overload
        # one.
        $grad = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            $face, $accentColour, $dark, [single]60.0)
        $g.FillRectangle($grad, $face)
        $grad.Dispose()
    }

    # A band behind the title, so the words stay readable over any cover. Its
    # edges fade rather than stopping dead: a hard rectangle clipped to a
    # circle reads as a stripe laid over the art instead of part of it.
    $bandH = ConvertTo-Px 22.0
    $bandY = $centre + ($outerR * 0.26)
    $bandRect = New-Object System.Drawing.RectangleF(
        [single]($centre - $outerR), [single]($bandY - 1),
        [single]($outerR * 2), [single]($bandH + 2))
    $scrim = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        $bandRect, [System.Drawing.Color]::Black, [System.Drawing.Color]::Black, [single]90.0)
    $blend = New-Object System.Drawing.Drawing2D.ColorBlend 4
    $blend.Colors = @(
        [System.Drawing.Color]::FromArgb(0, 0, 0, 0)
        [System.Drawing.Color]::FromArgb(205, 0, 0, 0)
        [System.Drawing.Color]::FromArgb(205, 0, 0, 0)
        [System.Drawing.Color]::FromArgb(0, 0, 0, 0))
    $blend.Positions = @([single]0.0, [single]0.16, [single]0.84, [single]1.0)
    $scrim.InterpolationColors = $blend
    $g.FillRectangle($scrim, $bandRect)
    $scrim.Dispose()

    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $middle = New-Object System.Drawing.StringFormat
    $middle.Alignment = [System.Drawing.StringAlignment]::Center
    $middle.LineAlignment = [System.Drawing.StringAlignment]::Center

    # The chord across the ring at the title's height, less a margin. This is
    # narrower than the disc, and it is what the words really have to fit in.
    $titleWidth = $outerR * 1.5
    $titleFont = Get-FittedFont -Graphics $g -Text $Title -MaxWidthPx $titleWidth -StartPt 20 -MinPt 8
    $titleBox = New-Object System.Drawing.RectangleF(
        [single]($centre - ($titleWidth / 2)), [single]$bandY,
        [single]$titleWidth, [single]($bandH * 0.58))
    $g.DrawString($Title, $titleFont, $white, $titleBox, $middle)

    if ($Subtitle) {
        $subFont = Get-FittedFont -Graphics $g -Text $Subtitle -MaxWidthPx $titleWidth -StartPt 9 -MinPt 6
        $subInk = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(225, 225, 225, 225))
        $subBox = New-Object System.Drawing.RectangleF(
            [single]($centre - ($titleWidth / 2)), [single]($bandY + ($bandH * 0.56)),
            [single]$titleWidth, [single]($bandH * 0.34))
        $g.DrawString($Subtitle, $subFont, $subInk, $subBox, $middle)
        $subFont.Dispose(); $subInk.Dispose()
    }

    $g.ResetClip()

    # Punch the hub back out to nothing, last, because the art and the band
    # both cross it. SourceCopy rather than drawing transparent paint over the
    # top, which would do nothing at all.
    $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $clear = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::Transparent)
    $g.FillEllipse($clear, [single]($centre - $innerR), [single]($centre - $innerR),
                   [single]($innerR * 2), [single]($innerR * 2))
    $clear.Dispose()

    foreach ($d in @($g, $ring, $white, $titleFont, $middle)) { $d.Dispose() }
    $bmp.Save($OutPng, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return $OutPng
}

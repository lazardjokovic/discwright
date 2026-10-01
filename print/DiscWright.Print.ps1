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
# Panels says what shape the printed sheet is. Two panels and a spine is a
# wrap that goes round the case; one panel is an insert that sits in the front
# of a jewel case and has no back and no spine. Rendering a one-panel insert as
# a wrap produces two half-width panels either side of a spine with no width,
# which is what this table used to do.
$script:CaseFormats = @(
    @{ Key = 'dvd';        Name = 'DVD case, standard 14 mm'
       WrapWidthMm = 273; HeightMm = 183; SpineMm = 14; Panels = 2; Verified = $false }
    @{ Key = 'dvd-slim';   Name = 'DVD case, slim 7 mm'
       WrapWidthMm = 266; HeightMm = 183; SpineMm = 7;  Panels = 2; Verified = $false }
    @{ Key = 'dvd-double'; Name = 'DVD case, double 14 mm'
       WrapWidthMm = 273; HeightMm = 183; SpineMm = 14; Panels = 2; Verified = $false }
    @{ Key = 'bluray';     Name = 'Blu-ray case, 12 mm'
       WrapWidthMm = 270; HeightMm = 164; SpineMm = 12; Panels = 2; Verified = $false }
    @{ Key = 'cd';         Name = 'CD jewel case, front insert'
       WrapWidthMm = 120; HeightMm = 120; SpineMm = 0;  Panels = 1; Verified = $false }
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

# What is left for each panel once the spine is taken out of the trim width. A
# one-panel insert has no spine to take out, so the panel is the whole thing.
function Get-PanelWidthMm($case) {
    if ($case.Panels -eq 1) { return [double]$case.WrapWidthMm }
    return ($case.WrapWidthMm - $case.SpineMm) / 2.0
}

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
    # Every argument wrapped whole: a comma binds tighter than a divide, so
    # `(a - b) / 2, (c - d) / 2` divides by the array `2, (c - d)` instead. The
    # failure only appears once a real image is passed, because without one
    # this line never runs.
    $src = New-Object System.Drawing.RectangleF(
        [single](($Image.Width - $srcW) / 2.0), [single](($Image.Height - $srcH) / 2.0),
        [single]$srcW, [single]$srcH)
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

    # No picture goes through here any more. Fitting somebody's artwork into a
    # layout means cropping it, and a person who made disc art made it to be
    # printed as it is: New-DiscFaceFromArtwork does that. This is the label for
    # someone who has no artwork at all, so it is type on a colour and nothing
    # can be ruined by it.
    $dark = [System.Drawing.Color]::FromArgb(
        [Math]::Max(0, $accentColour.R - 40), [Math]::Max(0, $accentColour.G - 40),
        [Math]::Max(0, $accentColour.B - 40))
    if ($true) {
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

<#
    The wrap that goes in the case: back panel, spine, front panel, printed on
    one sheet and trimmed to the marks.

    The guide this work came from builds that by hand in a design application
    every single time, and the arithmetic is always the same. The panel is
    whatever the trim width leaves once the spine is taken out, the art bleeds
    past the trim so a slightly crooked cut does not leave a white edge, and
    the marks sit outside the bleed so they are cut away with it.

    Printed face up, the order across the sheet is back, spine, front. That is
    not a style choice: the sheet wraps round the case from the back, so the
    front panel has to be the right-hand one.
#>
function New-CaseWrap {
    param(
        [Parameter(Mandatory)][string]$OutPdf,
        [Parameter(Mandatory)][string]$Title,
        [string]$Subtitle,
        # What is actually on the disc, one line each. On a two game disc this
        # is the thing somebody reads on the shelf.
        [string[]]$Contents = @(),
        [string]$Case = 'dvd',
        [ValidateSet('a4', 'letter')][string]$Page = 'a4',
        [double]$BleedMm = 3,
        [string]$Accent = '#1B2A41',
        # Cover art usually carries the game's own logo, and a second title
        # drawn over it fights the artwork. That is the app's rule for the menu
        # background and it is the same picture here. The spine and the back
        # still name the disc, so nothing becomes unidentifiable.
        [bool]$ShowTitleOnFront = $true,
        [switch]$NoCropMarks
    )
    $fmt = Get-CaseFormat $Case
    $paper = $script:PageSizes[$Page]
    $pageW = $paper.HeightMm; $pageH = $paper.WidthMm   # landscape

    $trimW = $fmt.WrapWidthMm
    $trimH = $fmt.HeightMm
    if (($trimW + ($BleedMm * 2)) -gt $pageW -or ($trimH + ($BleedMm * 2)) -gt $pageH) {
        throw ("A $($fmt.Name) wrap with $BleedMm mm of bleed is " +
               "$($trimW + ($BleedMm * 2)) x $($trimH + ($BleedMm * 2)) mm, which does not fit " +
               "on $Page landscape at $pageW x $pageH mm. Use a smaller bleed or a bigger page.")
    }

    $bmp = New-Object System.Drawing.Bitmap (ConvertTo-Px $pageW), (ConvertTo-Px $pageH)
    $bmp.SetResolution($script:Dpi, $script:Dpi)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::White)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit

    $accentColour = ConvertFrom-HexColour $Accent
    $panelMm = Get-PanelWidthMm $fmt

    $bleed  = ConvertTo-Px $BleedMm
    # Each call parenthesised: in command parsing mode a bare minus after an
    # argument is another argument, not a subtraction.
    $trimX  = ((ConvertTo-Px $pageW) - (ConvertTo-Px $trimW)) / 2.0
    $trimY  = ((ConvertTo-Px $pageH) - (ConvertTo-Px $trimH)) / 2.0
    $trimWp = ConvertTo-Px $trimW
    $trimHp = ConvertTo-Px $trimH
    $panelW = ConvertTo-Px $panelMm
    $spineW = ConvertTo-Px $fmt.SpineMm

    # Each panel runs past the trim on the sides that reach a cut edge, so the
    # art is still there if the cut wanders. A one-panel insert reaches every
    # cut edge, because it is the whole sheet.
    $twoPanel = ($fmt.Panels -ne 1)
    if ($twoPanel) {
        $backRect = New-Object System.Drawing.RectangleF(
            [single]($trimX - $bleed), [single]($trimY - $bleed),
            [single]($panelW + $bleed), [single]($trimHp + ($bleed * 2)))
        $spineRect = New-Object System.Drawing.RectangleF(
            [single]($trimX + $panelW), [single]($trimY - $bleed),
            [single]$spineW, [single]($trimHp + ($bleed * 2)))
        $frontRect = New-Object System.Drawing.RectangleF(
            [single]($trimX + $panelW + $spineW), [single]($trimY - $bleed),
            [single]($panelW + $bleed), [single]($trimHp + ($bleed * 2)))
    } else {
        $backRect = $null
        $spineRect = $null
        $frontRect = New-Object System.Drawing.RectangleF(
            [single]($trimX - $bleed), [single]($trimY - $bleed),
            [single]($trimWp + ($bleed * 2)), [single]($trimHp + ($bleed * 2)))
        if ($Contents.Count) {
            # Said out loud rather than dropped: the list belongs on a back
            # panel, and a front insert has not got one.
            Write-Warning ("A $($fmt.Name) is a single panel, so the contents list has " +
                           'nowhere to go and is not printed.')
        }
    }

    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $faint = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(205, 225, 230, 238))
    $middle = New-Object System.Drawing.StringFormat
    $middle.Alignment = [System.Drawing.StringAlignment]::Center
    $middle.LineAlignment = [System.Drawing.StringAlignment]::Center

    $darker = [System.Drawing.Color]::FromArgb(
        [Math]::Max(0, $accentColour.R - 45), [Math]::Max(0, $accentColour.G - 45),
        [Math]::Max(0, $accentColour.B - 45))

    # ---- the front panel, which is the right-hand one.
    # Type on a colour, with no picture in it. See New-DiscFace for why.
    $grad = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        $frontRect, $accentColour, $darker, [single]70.0)
    $g.FillRectangle($grad, $frontRect)
    $grad.Dispose()

    # A scrim up from the foot of the front panel, so a title stays readable
    # over artwork nobody chose for its contrast. No lettering, no scrim: it
    # would only be a shadow across somebody's cover art.
    if ($ShowTitleOnFront) {
    $scrimH = $trimHp * 0.34
    $scrimRect = New-Object System.Drawing.RectangleF(
        $frontRect.X, [single]($frontRect.Bottom - $scrimH), $frontRect.Width, [single]$scrimH)
    $scrim = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        $scrimRect, [System.Drawing.Color]::Black, [System.Drawing.Color]::Black, [single]90.0)
    $blend = New-Object System.Drawing.Drawing2D.ColorBlend 3
    $blend.Colors = @(
        [System.Drawing.Color]::FromArgb(0, 0, 0, 0)
        [System.Drawing.Color]::FromArgb(170, 0, 0, 0)
        [System.Drawing.Color]::FromArgb(225, 0, 0, 0))
    $blend.Positions = @([single]0.0, [single]0.55, [single]1.0)
    $scrim.InterpolationColors = $blend
    $g.FillRectangle($scrim, $scrimRect)
    $scrim.Dispose()
    }

    $pad = ConvertTo-Px 8.0
    $textW = $panelW - ($pad * 2)
    $left = New-Object System.Drawing.StringFormat
    $left.Alignment = [System.Drawing.StringAlignment]::Near
    $left.LineAlignment = [System.Drawing.StringAlignment]::Far

    $titleFont = Get-FittedFont -Graphics $g -Text $Title -MaxWidthPx $textW -StartPt 30 -MinPt 11
    $subH = if ($Subtitle -and $ShowTitleOnFront) { ConvertTo-Px 9.0 } else { 0 }
    if ($ShowTitleOnFront) {
    $titleBox = New-Object System.Drawing.RectangleF(
        [single]($frontRect.X + $pad),
        [single]($frontRect.Bottom - $bleed - $pad - $subH - (ConvertTo-Px 16.0)),
        [single]$textW, [single](ConvertTo-Px 16.0))
    $g.DrawString($Title, $titleFont, $white, $titleBox, $left)

    if ($Subtitle) {
        $subFont = Get-FittedFont -Graphics $g -Text $Subtitle -MaxWidthPx $textW -StartPt 11 -MinPt 7
        $subBox = New-Object System.Drawing.RectangleF(
            [single]($frontRect.X + $pad), [single]($frontRect.Bottom - $bleed - $pad - $subH),
            [single]$textW, [single]$subH)
        $g.DrawString($Subtitle, $subFont, $faint, $subBox, $left)
        $subFont.Dispose()
    }
    }   # end of the front-panel lettering

    # ---- the spine, and the back panel, neither of which a front insert has
    if ($twoPanel) {
    $g.FillRectangle((New-Object System.Drawing.SolidBrush $darker), $spineRect)
    $spineLen = $trimHp - (ConvertTo-Px 16.0)
    # Starts near what a 14 mm spine can hold rather than at a timid size: the
    # fitter only ever comes down, so a low start just wastes the spine.
    $spineStart = [Math]::Min(18.0, ($fmt.SpineMm * 1.4))
    $spineFont = Get-FittedFont -Graphics $g -Text $Title -MaxWidthPx $spineLen `
                                -StartPt $spineStart -MinPt 5
    $state = $g.Save()
    $g.TranslateTransform([single]($spineRect.X + ($spineW / 2)), [single]($trimY + ($trimHp / 2)))
    # Clockwise, so the title reads downwards with the case standing up, which
    # is how a shelf of DVDs reads.
    $g.RotateTransform(90)
    $spineBox = New-Object System.Drawing.RectangleF(
        [single](-$spineLen / 2), [single](-$spineW / 2), [single]$spineLen, [single]$spineW)
    $g.DrawString($Title, $spineFont, $white, $spineBox, $middle)
    $g.Restore($state)

    # ---- the back panel
    $grad = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        $backRect, $darker, $accentColour, [single]70.0)
    $g.FillRectangle($grad, $backRect)
    $grad.Dispose()

    $topLeft = New-Object System.Drawing.StringFormat
    $topLeft.Alignment = [System.Drawing.StringAlignment]::Near
    $topLeft.LineAlignment = [System.Drawing.StringAlignment]::Near

    $backX = $trimX + $pad
    $backY = $trimY + $pad
    $backFont = Get-FittedFont -Graphics $g -Text $Title -MaxWidthPx $textW -StartPt 17 -MinPt 9
    $g.DrawString($Title, $backFont, $white,
                  (New-Object System.Drawing.RectangleF(
                      [single]$backX, [single]$backY, [single]$textW, [single](ConvertTo-Px 11.0))),
                  $topLeft)
    $backFont.Dispose()

    if ($Contents.Count) {
        $head = New-Object System.Drawing.Font 'Segoe UI', 8,
                           ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Point)
        $y = $backY + (ConvertTo-Px 16.0)
        $g.DrawString('ON THIS DISC', $head, $faint,
                      (New-Object System.Drawing.RectangleF(
                          [single]$backX, [single]$y, [single]$textW, [single](ConvertTo-Px 6.0))),
                      $topLeft)
        $head.Dispose()
        $y += ConvertTo-Px 8.0
        foreach ($line in $Contents) {
            $item = Get-FittedFont -Graphics $g -Text $line -MaxWidthPx ($textW - (ConvertTo-Px 5.0)) `
                                   -StartPt 10 -MinPt 6
            $g.DrawString([char]0x2022 + ' ' + $line, $item, $white,
                          (New-Object System.Drawing.RectangleF(
                              [single]$backX, [single]$y, [single]$textW, [single](ConvertTo-Px 7.0))),
                          $topLeft)
            $item.Dispose()
            $y += ConvertTo-Px 7.0
        }
    }

    # The foot of the back panel, where a publisher would put its name.
    $footFont = New-Object System.Drawing.Font 'Segoe UI', 7,
                           ([System.Drawing.GraphicsUnit]::Point)
    $foot = New-Object System.Drawing.StringFormat
    $foot.Alignment = [System.Drawing.StringAlignment]::Near
    $foot.LineAlignment = [System.Drawing.StringAlignment]::Far
    $g.DrawString('Made with DiscWright', $footFont, $faint,
                  (New-Object System.Drawing.RectangleF(
                      [single]$backX, [single]($trimY + $trimHp - $pad - (ConvertTo-Px 6.0)),
                      [single]$textW, [single](ConvertTo-Px 6.0))),
                  $foot)
    $footFont.Dispose(); $foot.Dispose()
    }   # end of the two-panel-only work

    # ---- where to cut and where to fold
    if (-not $NoCropMarks) {
        $mark = New-Object System.Drawing.Pen ([System.Drawing.Color]::Black), 2
        $gap = ConvertTo-Px 1.0
        $len = ConvertTo-Px 5.0
        $outside = $bleed + $gap
        foreach ($x in @($trimX, ($trimX + $trimWp))) {
            $g.DrawLine($mark, [single]$x, [single]($trimY - $outside),
                        [single]$x, [single]($trimY - $outside - $len))
            $g.DrawLine($mark, [single]$x, [single]($trimY + $trimHp + $outside),
                        [single]$x, [single]($trimY + $trimHp + $outside + $len))
        }
        foreach ($y in @($trimY, ($trimY + $trimHp))) {
            $g.DrawLine($mark, [single]($trimX - $outside), [single]$y,
                        [single]($trimX - $outside - $len), [single]$y)
            $g.DrawLine($mark, [single]($trimX + $trimWp + $outside), [single]$y,
                        [single]($trimX + $trimWp + $outside + $len), [single]$y)
        }
        # Fold marks: shorter, and only at the spine, so they are not mistaken
        # for a cut.
        $fold = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(130, 130, 130)), 2
        $fold.DashStyle = [System.Drawing.Drawing2D.DashStyle]::Dot
        if ($twoPanel) {
        # Every element parenthesised: a comma binds tighter than a plus, so
        # @($a + $b, $a + $c) is not the two sums it looks like.
        foreach ($x in @(($trimX + $panelW), ($trimX + $panelW + $spineW))) {
            $g.DrawLine($fold, [single]$x, [single]($trimY - $outside),
                        [single]$x, [single]($trimY - $outside - ($len * 0.6)))
            $g.DrawLine($fold, [single]$x, [single]($trimY + $trimHp + $outside),
                        [single]$x, [single]($trimY + $trimHp + $outside + ($len * 0.6)))
        }
        }   # no fold marks on a single panel: there is nothing to fold
        $mark.Dispose(); $fold.Dispose()
    }

    foreach ($d in @($g, $white, $faint, $middle, $left, $topLeft, $titleFont, $spineFont)) {
        if ($d) { $d.Dispose() }
    }
    $null = Export-ImageAsPdf -Image $bmp -OutPdf $OutPdf -PageWidthMm $pageW -PageHeightMm $pageH
    $bmp.Dispose()
    return $OutPdf
}

# ------------------------------------------------- artwork for a real project

<#
    Make the wrap and the disc face for a disc DiscWright has already planned.

    It reads `discproject.json`, which the app writes beside the ISO, and
    nothing else. That direction matters: the app may reach into this file, but
    this file never reaches into the app. A project file is a published format
    with a version number, so reading it keeps the two separable, which is the
    point of keeping the ISO builder able to ship on its own one day.

    Everything the artwork needs is already in there and already chosen by the
    person: the title they typed, the games they added, and the background
    image they picked for the menu, which is the cover art. Asking them for it
    again would be asking twice.
#>
function New-ArtworkForProject {
    param(
        # The project file, or the folder holding it.
        [Parameter(Mandatory)][string]$ProjectPath,
        [string]$OutDir,
        [string]$Case = 'dvd',
        [string]$Disc = 'hub',
        [ValidateSet('a4', 'letter')][string]$Page = 'a4',
        [string]$Accent = '#1B2A41'
    )
    $path = $ProjectPath
    if (Test-Path -LiteralPath $path -PathType Container) {
        $path = Join-Path $path 'discproject.json'
    }
    if (-not (Test-Path -LiteralPath $path)) {
        throw "No project file at '$path'. DiscWright writes discproject.json beside the ISO."
    }

    try { $p = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json }
    catch { throw "'$path' is not a readable project file: $($_.Exception.Message)" }

    # TitleText is what the menu shows and is the better name when it is there.
    # Label is the disc's volume label, which is always set, so it is the floor.
    $title = $p.TitleText
    if ([string]::IsNullOrWhiteSpace($title)) { $title = $p.Label }
    if ([string]::IsNullOrWhiteSpace($title)) { $title = 'Untitled disc' }

    # Add-ons belong to a game rather than standing beside it, so the back panel
    # lists the games and says how many add-ons came with them.
    $entries = @($p.Games)
    $games = @($entries | Where-Object { $_.Kind -ne 'AddOn' } |
               ForEach-Object { $_.GameName } | Where-Object { $_ })
    $addOns = @($entries | Where-Object { $_.Kind -eq 'AddOn' }).Count
    $contents = @($games)
    if ($addOns -gt 0) {
        $contents += ('{0} add-on{1}' -f $addOns, $(if ($addOns -eq 1) { '' } else { 's' }))
    }

    # BgPath is the menu background the person already chose. It can be missing
    # if the project moved machines, which is not an error: the artwork falls
    # back to the accent colour and the caller is told which happened.
    $cover = $null
    if ($p.BgPath -and (Test-Path -LiteralPath $p.BgPath)) { $cover = $p.BgPath }

    if (-not $OutDir) { $OutDir = Split-Path -Parent $path }

    $r = New-ArtworkForDisc -Title $title -Label $p.Label -Games $games -AddOnCount $addOns `
                            -CoverImage $cover `
                            -OutDir $OutDir -Case $Case -Disc $Disc -Page $Page -Accent $Accent
    $r | Add-Member -NotePropertyName ProjectFile -NotePropertyValue $path -PassThru
}

<#
    The same work, from values rather than from a file.

    The app calls this one with what is on the form, because a button that
    writes discproject.json to read it straight back would overwrite a saved
    project with whatever happened to be on screen. The file reader above calls
    it too, so there is one description of what a disc's artwork looks like.
#>
function New-ArtworkForDisc {
    param(
        [Parameter(Mandatory)][string]$Title,
        [string]$Label,
        [string[]]$Games = @(),
        [int]$AddOnCount = 0,
        [string]$CoverImage,
        # A cover is tall and a disc face is a circle, so one picture rarely
        # suits both. Left empty, the cover picture is used for both, which is
        # what happened before this existed.
        [string]$DiscImage,
        [Parameter(Mandatory)][string]$OutDir,
        [string]$Case = 'dvd',
        [string]$Disc = 'hub',
        [ValidateSet('a4', 'letter')][string]$Page = 'a4',
        [string]$Accent = '#1B2A41'
    )
    $contents = @($Games | Where-Object { $_ })
    if ($AddOnCount -gt 0) {
        $contents += ('{0} add-on{1}' -f $AddOnCount, $(if ($AddOnCount -eq 1) { '' } else { 's' }))
    }

    $cover = $null
    if ($CoverImage -and (Test-Path -LiteralPath $CoverImage)) { $cover = $CoverImage }
    $facePic = $cover
    if ($DiscImage -and (Test-Path -LiteralPath $DiscImage)) { $facePic = $DiscImage }

    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

    $stem = Get-SafeFileStem $Title
    $wrap = Join-Path $OutDir "$stem-wrap-$Case-$Page.pdf"
    $facePath = Join-Path $OutDir "$stem-disc-face-$Disc.png"

    # The volume label is only worth printing when it says something the title
    # does not. On most discs they are the same words, and printing both just
    # prints the name twice.
    $subtitle = $null
    if ($Label -and $Label.Trim() -ne $Title.Trim()) { $subtitle = $Label }

    # Only the plain label has a title to show or hide, since a picture is now
    # printed untouched either way.
    $titleOnFront = $true

    # A picture is printed as a picture. There is no longer a path that fits one
    # into a layout, because fitting means cropping and nobody who made a cover
    # wants a slice taken off it. With no picture, what comes out is a plain
    # label: type on a colour, which is still worth having on a disc in a stack.
    $fmt = Get-CaseFormat $Case
    $wrapKind = $null
    if ($cover) {
        $img = [System.Drawing.Image]::FromFile((Resolve-Path -LiteralPath $cover).Path)
        try { $wrapKind = Get-ArtworkKind -Width $img.Width -Height $img.Height -Case $fmt }
        finally { $img.Dispose() }
    }
    # Only a picture already shaped like a cover is a cover. A menu background
    # is 16:9 because it sits behind buttons, and printing one across a 273 mm
    # wrap gives a stretched wallpaper with no spine, no back and no game list,
    # which is worse than the plain label. So the shape decides: close to a wrap
    # or a panel and it is somebody's finished work, printed untouched; anything
    # else and the label is printed and the picture left alone. Nothing is ever
    # cropped or squashed either way.
    $coverIsArtwork = $wrapKind -and $wrapKind.OffByPct -le 6

    if ($coverIsArtwork) {
        $null = New-WrapFromArtwork -OutPdf $wrap -Artwork $cover -Case $Case -Page $Page
    } else {
        $null = New-CaseWrap -OutPdf $wrap -Case $Case -Page $Page -Accent $Accent `
                             -Title $Title -Subtitle $subtitle `
                             -Contents $contents -ShowTitleOnFront $titleOnFront
    }
    # The face keeps its title whatever the wrap does: a disc out of its case
    # with no writing on it is the one nobody can identify.
    # The disc face, the same way. A picture chosen for the face is printed;
    # with none, it is a plain label with the title on it, which is what tells
    # one unlabelled disc from another.
    # Disc art is square, because a disc is round. A picture of any other shape
    # was not drawn for a disc.
    $faceIsArtwork = $false
    if ($facePic) {
        $img = [System.Drawing.Image]::FromFile((Resolve-Path -LiteralPath $facePic).Path)
        try { $faceIsArtwork = [Math]::Abs(($img.Width / [double]$img.Height) - 1.0) -le 0.06 }
        finally { $img.Dispose() }
    }
    if ($faceIsArtwork) {
        $null = New-DiscFaceFromArtwork -OutPng $facePath -Artwork $facePic -Disc $Disc
    } else {
        $null = New-DiscFace -OutPng $facePath -Disc $Disc -Accent $Accent `
                             -Title $Title -Subtitle $subtitle
    }

    return [pscustomobject]@{
        Title        = $Title
        Contents     = $contents
        CoverImage   = $cover
        UsedCover    = [bool]$cover
        DiscImage    = $facePic
        # Which way each was made, so the app can say so rather than leave
        # somebody wondering why their cover came back with a title on it.
        WrapFromArtwork = [bool]$coverIsArtwork
        FaceFromArtwork = [bool]$faceIsArtwork
        # How far the cover is from the shape a case wants, so the app can say
        # what to change rather than silently placing it with bands either side.
        CoverOffByPct   = $(if ($wrapKind) { $wrapKind.OffByPct } else { 0 })
        CoverKind       = $(if ($wrapKind) { $wrapKind.Kind } else { '' })
        TitleOnCover = $titleOnFront
        Wrap         = $wrap
        DiscFace     = $facePath
    }
}

<#
    A file name from a disc title. Titles carry colons and question marks, which
    Windows will not have in a file name, and a renderer that throws at the last
    step after drawing everything is a poor way to find that out.
#>
function Get-SafeFileStem([string]$text) {
    $clean = $text
    foreach ($bad in [IO.Path]::GetInvalidFileNameChars()) {
        $clean = $clean.Replace($bad, '-')
    }
    $clean = ($clean -replace '\s+', '-') -replace '-{2,}', '-'
    $clean = $clean.Trim('-', '.')
    if ([string]::IsNullOrWhiteSpace($clean)) { return 'disc' }
    if ($clean.Length -gt 60) { $clean = $clean.Substring(0, 60).TrimEnd('-') }
    return $clean
}

# --------------------------------------------- somebody else's finished artwork

<#
    A finished cover, printed and nothing else.

    There is a whole community that makes these: The Cover Project, CoverGalaxy,
    SteamGameCovers and others, and a collector who has found one for their game
    does not want a layout imposed on top of it. For them this app is not a
    designer, it is the part that gets the millimetres right: exact trim, bleed
    the artwork does not have, crop marks outside it, and a page that prints at
    its true size.

    So this draws the picture and the marks. No title, no scrim, no contents
    list, nothing of mine anywhere on it.
#>
function Get-ArtworkKind {
    param(
        [Parameter(Mandatory)][int]$Width,
        [Parameter(Mandatory)][int]$Height,
        [Parameter(Mandatory)]$Case
    )
    $panel = (Get-PanelWidthMm $Case) / [double]$Case.HeightMm
    $wrap  = $Case.WrapWidthMm / [double]$Case.HeightMm
    $a = $Width / [double]$Height
    # Which of the two shapes is it closer to, measured as a ratio rather than a
    # difference so that being 20% out is the same answer either way round.
    $dPanel = [Math]::Abs([Math]::Log($a / $panel))
    $dWrap  = [Math]::Abs([Math]::Log($a / $wrap))
    $kind = if ($dWrap -le $dPanel) { 'wrap' } else { 'front' }
    $want = if ($kind -eq 'wrap') { $wrap } else { $panel }
    return [pscustomobject]@{
        Kind        = $kind
        Aspect      = [Math]::Round($a, 3)
        WantAspect  = [Math]::Round($want, 3)
        # How far off the expected shape it is, as a percentage. Anything much
        # above a few per cent will be visibly stretched or cropped.
        OffByPct    = [int][Math]::Round(100 * [Math]::Abs($a - $want) / $want)
    }
}

<#
    Bleed an artwork does not have.

    A downloaded cover is drawn to the trim, with nothing beyond it. Printed and
    cut by hand, a cut a millimetre out leaves a white sliver down the edge. The
    usual fix is to extend the outermost pixels outwards, which invents nothing
    and is invisible on anything but a hard-edged border.
#>
function Expand-EdgesTo {
    param(
        [Parameter(Mandatory)][System.Drawing.Graphics]$Graphics,
        [Parameter(Mandatory)][System.Drawing.Image]$Image,
        # Where the picture itself sits, and how far its edges must reach.
        [Parameter(Mandatory)][System.Drawing.RectangleF]$Inner,
        [Parameter(Mandatory)][System.Drawing.RectangleF]$Outer
    )
    $iw = $Image.Width; $ih = $Image.Height
    $px = [single]1
    $left   = [single]($Inner.X - $Outer.X)
    $right  = [single]($Outer.Right - $Inner.Right)
    $top    = [single]($Inner.Y - $Outer.Y)
    $bottom = [single]($Outer.Bottom - $Inner.Bottom)

    $sides = @()
    if ($left -gt 0) { $sides += @{
        Src = New-Object System.Drawing.RectangleF(0, 0, $px, $ih)
        Dst = New-Object System.Drawing.RectangleF($Outer.X, $Inner.Y, $left, $Inner.Height) } }
    if ($right -gt 0) { $sides += @{
        Src = New-Object System.Drawing.RectangleF(($iw - $px), 0, $px, $ih)
        Dst = New-Object System.Drawing.RectangleF($Inner.Right, $Inner.Y, $right, $Inner.Height) } }
    if ($top -gt 0) { $sides += @{
        Src = New-Object System.Drawing.RectangleF(0, 0, $iw, $px)
        Dst = New-Object System.Drawing.RectangleF($Inner.X, $Outer.Y, $Inner.Width, $top) } }
    if ($bottom -gt 0) { $sides += @{
        Src = New-Object System.Drawing.RectangleF(0, ($ih - $px), $iw, $px)
        Dst = New-Object System.Drawing.RectangleF($Inner.X, $Inner.Bottom, $Inner.Width, $bottom) } }
    foreach ($s in $sides) {
        $Graphics.DrawImage($Image, $s.Dst, $s.Src, [System.Drawing.GraphicsUnit]::Pixel)
    }

    # The four corners, each from the single corner pixel.
    $corners = @(
        @{ Sx = 0;           Sy = 0;           X = $Outer.X;    Y = $Outer.Y;      W = $left;  H = $top }
        @{ Sx = ($iw - $px); Sy = 0;           X = $Inner.Right; Y = $Outer.Y;     W = $right; H = $top }
        @{ Sx = 0;           Sy = ($ih - $px); X = $Outer.X;    Y = $Inner.Bottom; W = $left;  H = $bottom }
        @{ Sx = ($iw - $px); Sy = ($ih - $px); X = $Inner.Right; Y = $Inner.Bottom; W = $right; H = $bottom }
    )
    foreach ($c in $corners) {
        if ($c.W -le 0 -or $c.H -le 0) { continue }
        $src = New-Object System.Drawing.RectangleF($c.Sx, $c.Sy, $px, $px)
        $dst = New-Object System.Drawing.RectangleF($c.X, $c.Y, $c.W, $c.H)
        $Graphics.DrawImage($Image, $dst, $src, [System.Drawing.GraphicsUnit]::Pixel)
    }
}

function New-WrapFromArtwork {
    param(
        [Parameter(Mandatory)][string]$OutPdf,
        [Parameter(Mandatory)][string]$Artwork,
        [string]$Case = 'dvd',
        [ValidateSet('a4', 'letter')][string]$Page = 'a4',
        [double]$BleedMm = 3,
        [switch]$NoCropMarks
    )
    if (-not (Test-Path -LiteralPath $Artwork)) { throw "No artwork at '$Artwork'" }
    $fmt = Get-CaseFormat $Case
    $paper = $script:PageSizes[$Page]
    $pageW = $paper.HeightMm; $pageH = $paper.WidthMm

    $img = [System.Drawing.Image]::FromFile((Resolve-Path -LiteralPath $Artwork).Path)
    try {
        $kind = Get-ArtworkKind -Width $img.Width -Height $img.Height -Case $fmt
        # A front-only cover is printed at panel width; a full wrap at trim
        # width. Guessing wrong prints a wrap squashed into half a case.
        $trimW = if ($kind.Kind -eq 'wrap') { [double]$fmt.WrapWidthMm } else { Get-PanelWidthMm $fmt }
        $trimH = [double]$fmt.HeightMm

        if (($trimW + ($BleedMm * 2)) -gt $pageW -or ($trimH + ($BleedMm * 2)) -gt $pageH) {
            throw ("That artwork needs $trimW x $trimH mm plus bleed, which does not fit on " +
                   "$Page landscape. Use a smaller bleed or a bigger page.")
        }

        $bmp = New-Object System.Drawing.Bitmap (ConvertTo-Px $pageW), (ConvertTo-Px $pageH)
        $bmp.SetResolution($script:Dpi, $script:Dpi)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.Clear([System.Drawing.Color]::White)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

        $trimWp = ConvertTo-Px $trimW
        $trimHp = ConvertTo-Px $trimH
        $trimX = ((ConvertTo-Px $pageW) - $trimWp) / 2.0
        $trimY = ((ConvertTo-Px $pageH) - $trimHp) / 2.0
        $trim = New-Object System.Drawing.RectangleF(
            [single]$trimX, [single]$trimY, [single]$trimWp, [single]$trimHp)

        # Drawn to the trim exactly, not cropped to it. Somebody else's cover
        # arrives finished, and taking a slice off it is not this tool's place.
        # If its shape is a little off it is stretched, and Get-ArtworkKind says
        # by how much so the caller can warn.
        $bleed = [single](ConvertTo-Px $BleedMm)
        $outer = New-Object System.Drawing.RectangleF(
            [single]($trim.X - $bleed), [single]($trim.Y - $bleed),
            [single]($trim.Width + ($bleed * 2)), [single]($trim.Height + ($bleed * 2)))

        # Fitted whole, never cropped and never stretched. Somebody's finished
        # cover is the one thing in this project that must come out exactly as
        # it went in, so a shape that is slightly off is placed complete and its
        # own edges are carried outwards to fill the rest. Nothing is lost and
        # nothing is distorted; a cover that is the right shape fills the trim
        # exactly and none of this shows.
        $scale = [Math]::Min($trim.Width / $img.Width, $trim.Height / $img.Height)
        $fw = [single]($img.Width * $scale)
        $fh = [single]($img.Height * $scale)
        $fit = New-Object System.Drawing.RectangleF(
            [single]($trim.X + (($trim.Width - $fw) / 2)),
            [single]($trim.Y + (($trim.Height - $fh) / 2)), $fw, $fh)

        Expand-EdgesTo -Graphics $g -Image $img -Inner $fit -Outer $outer
        $g.DrawImage($img, $fit)

        if (-not $NoCropMarks) {
            $mark = New-Object System.Drawing.Pen ([System.Drawing.Color]::Black), 2
            $gap = ConvertTo-Px 1.0
            $len = ConvertTo-Px 5.0
            $out = $bleed + $gap
            foreach ($x in @($trimX, ($trimX + $trimWp))) {
                $g.DrawLine($mark, [single]$x, [single]($trimY - $out), [single]$x, [single]($trimY - $out - $len))
                $g.DrawLine($mark, [single]$x, [single]($trimY + $trimHp + $out),
                            [single]$x, [single]($trimY + $trimHp + $out + $len))
            }
            foreach ($y in @($trimY, ($trimY + $trimHp))) {
                $g.DrawLine($mark, [single]($trimX - $out), [single]$y, [single]($trimX - $out - $len), [single]$y)
                $g.DrawLine($mark, [single]($trimX + $trimWp + $out), [single]$y,
                            [single]($trimX + $trimWp + $out + $len), [single]$y)
            }
            # Fold marks only make sense on a full wrap, which is the only kind
            # that has a spine.
            if ($kind.Kind -eq 'wrap' -and $fmt.Panels -eq 2) {
                $fold = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(130, 130, 130)), 2
                $fold.DashStyle = [System.Drawing.Drawing2D.DashStyle]::Dot
                $panelW = ConvertTo-Px (Get-PanelWidthMm $fmt)
                $spineW = ConvertTo-Px $fmt.SpineMm
                foreach ($x in @(($trimX + $panelW), ($trimX + $panelW + $spineW))) {
                    $g.DrawLine($fold, [single]$x, [single]($trimY - $out),
                                [single]$x, [single]($trimY - $out - ($len * 0.6)))
                    $g.DrawLine($fold, [single]$x, [single]($trimY + $trimHp + $out),
                                [single]$x, [single]($trimY + $trimHp + $out + ($len * 0.6)))
                }
                $fold.Dispose()
            }
            $mark.Dispose()
        }

        $g.Dispose()
        $null = Export-ImageAsPdf -Image $bmp -OutPdf $OutPdf -PageWidthMm $pageW -PageHeightMm $pageH
        $bmp.Dispose()

        return [pscustomobject]@{
            Pdf        = $OutPdf
            Kind       = $kind.Kind
            TrimWidthMm = $trimW
            TrimHeightMm = $trimH
            Aspect     = $kind.Aspect
            OffByPct   = $kind.OffByPct
            Artwork    = (Resolve-Path -LiteralPath $Artwork).Path
        }
    } finally { $img.Dispose() }
}

<#
    A finished disc face, masked to the printable ring and nothing else.

    No band, no title. Somebody who has made disc art has already put the title
    where they want it, and a disc is round: art drawn for the full face only
    needs the hub taking out of it.
#>
function New-DiscFaceFromArtwork {
    param(
        [Parameter(Mandatory)][string]$OutPng,
        [Parameter(Mandatory)][string]$Artwork,
        [string]$Disc = 'hub'
    )
    if (-not (Test-Path -LiteralPath $Artwork)) { throw "No artwork at '$Artwork'" }
    $fmt = Get-DiscFormat $Disc
    $side = ConvertTo-Px 120.0
    $bmp = New-Object System.Drawing.Bitmap $side, $side,
                      ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bmp.SetResolution($script:Dpi, $script:Dpi)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)

    $centre = $side / 2.0
    $outerR = (ConvertTo-Px $fmt.OuterMm) / 2.0
    $innerR = (ConvertTo-Px $fmt.InnerMm) / 2.0

    $ring = New-Object System.Drawing.Drawing2D.GraphicsPath
    $ring.AddEllipse([single]($centre - $outerR), [single]($centre - $outerR),
                     [single]($outerR * 2), [single]($outerR * 2))
    $g.SetClip($ring)

    $img = [System.Drawing.Image]::FromFile((Resolve-Path -LiteralPath $Artwork).Path)
    try {
        # Disc art is drawn for the whole 120 mm face, so it is placed across
        # the whole face rather than only the printable ring. Anything outside
        # the ring is clipped away, which is where the artist expected the disc
        # to end anyway.
        $face = New-Object System.Drawing.RectangleF(0, 0, [single]$side, [single]$side)
        Set-ImageCover -Graphics $g -Image $img -Target $face
        $aspect = [Math]::Round($img.Width / [double]$img.Height, 3)
    } finally { $img.Dispose() }

    $g.ResetClip()
    $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $clear = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::Transparent)
    $g.FillEllipse($clear, [single]($centre - $innerR), [single]($centre - $innerR),
                   [single]($innerR * 2), [single]($innerR * 2))
    $clear.Dispose()
    $g.Dispose(); $ring.Dispose()

    $bmp.Save($OutPng, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return [pscustomobject]@{ Png = $OutPng; Aspect = $aspect; Disc = $fmt.Name }
}

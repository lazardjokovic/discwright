<#
    The print artwork: the table of physical sizes, the PDF writer, and the
    calibration sheet.

    These are measurements, and a wrong one wastes somebody's paper, ink and
    possibly a disc. So the table is asserted row by row rather than spot
    checked, and every case format is rendered, because "all the combinations"
    is only true if the tests walk all of them too.

    What cannot be asserted here is whether 273 x 183 mm is the right number for
    a real case. That is what the calibration sheet is for, and until it has
    been printed and held against one, the table says so with Verified = $false.
#>

# Dot-sourced here, at the top of the file, and not only in BeforeAll. Pester
# reads the file once to find out what tests exist and again to run them, and
# BeforeAll only runs on the second pass. A `foreach` over $script:CaseFormats
# during discovery therefore loops over nothing, every per-format Context
# silently disappears, and the suite still reports all green. That is exactly
# what happened here: three "every combination" loops ran zero times.
. (Join-Path (Split-Path $PSScriptRoot -Parent) 'print\DiscWright.Print.ps1')

BeforeAll {
    . (Join-Path (Split-Path $PSScriptRoot -Parent) 'print\DiscWright.Print.ps1')
    $script:Sandbox = Join-Path ([IO.Path]::GetTempPath()) ('dwprint_' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Force -Path $script:Sandbox | Out-Null

    function Get-PdfText([string]$path) {
        return [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($path))
    }

    # The cross-reference table is what a reader uses to find anything at all, so
    # a PDF whose offsets are wrong opens as a blank page or not at all.
    function Test-PdfXref([string]$path) {
        $bytes = [IO.File]::ReadAllBytes($path)
        $text = [Text.Encoding]::ASCII.GetString($bytes)
        $startxref = [int]([regex]::Match($text, 'startxref\s+(\d+)').Groups[1].Value)
        if ([Text.Encoding]::ASCII.GetString($bytes[$startxref..($startxref + 3)]) -ne 'xref') { return $false }
        $xref = [Text.Encoding]::ASCII.GetString($bytes[$startxref..($startxref + 400)])
        $offsets = @([regex]::Matches($xref, '(\d{10}) 00000 n') | ForEach-Object { [int]$_.Groups[1].Value })
        if ($offsets.Count -lt 1) { return $false }
        for ($i = 0; $i -lt $offsets.Count; $i++) {
            $at = [Text.Encoding]::ASCII.GetString($bytes[$offsets[$i]..($offsets[$i] + 9)])
            if (-not $at.StartsWith("$($i + 1) 0 obj")) { return $false }
        }
        return $true
    }

    function Get-PdfMediaBox([string]$path) {
        $m = [regex]::Match((Get-PdfText $path), '/MediaBox \[0 0 ([\d.]+) ([\d.]+)\]')
        return @{ W = [double]$m.Groups[1].Value; H = [double]$m.Groups[2].Value }
    }
}

AfterAll {
    if ($script:Sandbox -and (Test-Path $script:Sandbox)) {
        Remove-Item -LiteralPath $script:Sandbox -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Describe 'The table of physical sizes' -Tag 'Unit' {

    It 'has a key, a name and a measurement for every case' {
        foreach ($c in $script:CaseFormats) {
            $c.Key        | Should -Not -BeNullOrEmpty
            $c.Name       | Should -Not -BeNullOrEmpty
            $c.WrapWidthMm | Should -BeGreaterThan 0
            $c.HeightMm   | Should -BeGreaterThan 0
            $c.SpineMm    | Should -BeGreaterOrEqual 0
        }
    }

    It 'leaves room for two panels once the spine is taken out' {
        # Only the ones that have two. A single-panel insert gets the whole
        # width and is checked in its own Describe.
        foreach ($c in ($script:CaseFormats | Where-Object { $_.Panels -eq 2 })) {
            $panel = Get-PanelWidthMm $c
            $panel | Should -BeGreaterThan 0 -Because "$($c.Key) has to have panels"
            (($panel * 2) + $c.SpineMm) | Should -Be $c.WrapWidthMm
        }
    }

    It 'has a key, a name and an inside smaller than an outside for every disc' {
        foreach ($d in $script:DiscFormats) {
            $d.Key  | Should -Not -BeNullOrEmpty
            $d.Name | Should -Not -BeNullOrEmpty
            $d.InnerMm | Should -BeGreaterThan 0
            $d.OuterMm | Should -BeGreaterThan $d.InnerMm
            # No disc is wider than a disc.
            $d.OuterMm | Should -BeLessOrEqual 120
        }
    }

    It 'says plainly which measurements have been held against a real object' {
        # Verified is not decoration: it is the difference between a number from
        # a web page and one from a case in somebody's hand. Nothing is verified
        # until the calibration sheet has been printed.
        foreach ($f in @($script:CaseFormats) + @($script:DiscFormats)) {
            $f.ContainsKey('Verified') | Should -BeTrue -Because "$($f.Key) must say"
            $f.Verified | Should -BeOfType [bool]
        }
    }

    It 'refuses a format it does not have, by name' {
        { Get-CaseFormat 'cassette' } | Should -Throw -ExpectedMessage "*No case format 'cassette'*"
        { Get-DiscFormat 'minidisc' } | Should -Throw -ExpectedMessage "*No disc format 'minidisc'*"
    }
}

Describe 'Millimetres into the units a page is measured in' -Tag 'Unit' {

    It 'puts 72 points in an inch' {
        ConvertTo-Points 25.4 | Should -Be 72
    }

    It 'turns a DVD wrap into the page box a reader will honour' {
        # 273 mm is 10.748 inches, which is 773.86 points. If this is wrong the
        # page prints at the wrong size and every trim is off.
        ConvertTo-Points 273 | Should -Be 773.8583
    }

    # -ForEach, not a pipeline: Pester runs the body in its own scope, so a
    # variable captured outside it is empty by the time the test runs, and the
    # test then quietly asserts nothing against nothing.
    It 'turns <Mm> mm into <Px> px at 300 dpi' -ForEach @(
        @{ Mm = 25.4;  Px = 300 }
        @{ Mm = 120.0; Px = 1417 }
        @{ Mm = 273.0; Px = 3224 }
    ) {
        ConvertTo-Px $Mm | Should -Be $Px
    }
}

Describe 'The PDF it writes' -Tag 'Unit' {

    BeforeAll {
        Add-Type -AssemblyName System.Drawing
        $script:Bmp = New-Object System.Drawing.Bitmap 400, 300
        $g = [System.Drawing.Graphics]::FromImage($script:Bmp)
        $g.Clear([System.Drawing.Color]::White)
        $g.Dispose()
        $script:Pdf = Join-Path $script:Sandbox 'one.pdf'
        $null = Export-ImageAsPdf -Image $script:Bmp -OutPdf $script:Pdf -PageWidthMm 273 -PageHeightMm 183
    }

    It 'is a PDF from its first bytes to its last' {
        $text = Get-PdfText $script:Pdf
        $text.StartsWith('%PDF-1.4') | Should -BeTrue
        $text.TrimEnd().EndsWith('%%EOF') | Should -BeTrue
    }

    It 'can be found its way around, which is what a reader needs' {
        Test-PdfXref $script:Pdf | Should -BeTrue
    }

    It 'carries the page size asked for, in points' {
        $box = Get-PdfMediaBox $script:Pdf
        $box.W | Should -Be (ConvertTo-Points 273)
        $box.H | Should -Be (ConvertTo-Points 183)
    }

    It 'carries the picture as a JPEG of the size that went in' {
        $text = Get-PdfText $script:Pdf
        $text | Should -Match '/Filter /DCTDecode'
        $text | Should -Match '/Width 400'
        $text | Should -Match '/Height 300'
    }
}

Describe 'The calibration sheet' -Tag 'Unit' {

    # Every case format, because a sheet that only works for the one case the
    # author owns is how the other rows rot.
    # -ForEach, not a bare foreach: the loop itself runs at discovery, but the
    # row has to be handed to the run phase or it arrives empty.
    Context 'for a <Name>' -ForEach $script:CaseFormats {
        It 'renders onto a page that fits it, at the size that page really is' {
            $out = Join-Path $script:Sandbox "calib-$Key.pdf"
            $null = New-CalibrationSheet -OutPdf $out -Case $Key
            Test-Path $out | Should -BeTrue
            $box = Get-PdfMediaBox $out
            # A4 landscape: the wrap is wider than a portrait page.
            $box.W | Should -Be (ConvertTo-Points 297)
            $box.H | Should -Be (ConvertTo-Points 210)
            Test-PdfXref $out | Should -BeTrue
        }
    }

    It 'fits a DVD wrap on Letter as well as A4' {
        $out = Join-Path $script:Sandbox 'calib-letter.pdf'
        $null = New-CalibrationSheet -OutPdf $out -Case 'dvd' -Page 'letter'
        $box = Get-PdfMediaBox $out
        $box.W | Should -Be (ConvertTo-Points 279.4)
        # 273 mm of wrap on a 279.4 mm page leaves 3 mm either side: tight, and
        # worth knowing before it is printed rather than after.
        (279.4 - 273) | Should -BeGreaterThan 0
    }

    It 'draws more than one candidate, because the published sizes disagree' {
        # Two rectangles 2 mm apart read as one line unless they are drawn
        # differently, which is what the first version of this sheet did.
        $out = Join-Path $script:Sandbox 'calib-two.pdf'
        $null = New-CalibrationSheet -OutPdf $out -Case 'dvd' -WidthsMm @(273, 272) -HeightsMm @(183, 181)
        (Get-Item $out).Length | Should -BeGreaterThan 10000
    }
}

Describe 'Checking a printer before any paper moves' -Tag 'Unit' {

    BeforeAll {
        # The script reports on whatever printers exist, so dot-sourcing it would
        # run that. Only the decision function is wanted here, and it is pulled
        # out by parsing rather than by running anything.
        $src = Join-Path (Split-Path $PSScriptRoot -Parent) 'print\Test-PrinterSetup.ps1'
        $ast = [System.Management.Automation.Language.Parser]::ParseFile($src, [ref]$null, [ref]$null)
        $fn = $ast.Find({
            param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                      $n.Name -eq 'Get-FitReport'
        }, $true)
        $fn | Should -Not -BeNullOrEmpty -Because 'the fit verdict has to be its own function to be testable'
        . ([scriptblock]::Create($fn.Extent.Text))
    }

    It 'passes everything on a printer with no margins at all' {
        # A4 landscape edge to edge, which is what Microsoft Print to PDF reports.
        $rows = Get-FitReport -AreaWidthMm 297 -AreaHeightMm 210
        @($rows | Where-Object { -not $_.Fits }) | Should -BeNullOrEmpty
    }

    It 'fails the wraps on a portrait page, because a 273 mm wrap is wider than A4' {
        $rows = Get-FitReport -AreaWidthMm 210 -AreaHeightMm 297
        foreach ($r in $rows | Where-Object { $_.WidthMm -gt 210 }) {
            $r.Fits | Should -BeFalse -Because "$($r.Name) is $($r.WidthMm) mm wide"
        }
        # A disc face and a jewel insert are both 120 mm, so they still fit.
        ($rows | Where-Object { $_.Name -eq 'A disc face, printed on paper' }).Fits | Should -BeTrue
    }

    It 'catches the real case, a printer with ordinary margins' {
        # Many inkjets leave about 3 mm all round without borderless, which
        # leaves 291 x 204 mm of A4 landscape. A 273 mm wrap still fits; this is
        # the number the Epson will decide for real.
        $rows = Get-FitReport -AreaWidthMm 291 -AreaHeightMm 204
        ($rows | Where-Object { $_.Name -like 'DVD case, standard*' }).Fits | Should -BeTrue
    }

    It 'says no when the margins eat the width, rather than letting it be scaled' {
        # A driver with a 15 mm unprintable band each side leaves 267 mm, which
        # is less than a standard wrap. Silence here would cost photo paper.
        $rows = Get-FitReport -AreaWidthMm 267 -AreaHeightMm 204
        ($rows | Where-Object { $_.Name -like 'DVD case, standard*' }).Fits | Should -BeFalse
        # The slim case is 266 mm and still fits, so this is a real boundary and
        # not the function failing everything.
        ($rows | Where-Object { $_.Name -like 'DVD case, slim*' }).Fits | Should -BeTrue
    }

    It 'reports on every case in the table plus the disc face' {
        $rows = Get-FitReport -AreaWidthMm 297 -AreaHeightMm 210
        @($rows).Count | Should -Be (@($script:CaseFormats).Count + 1)
    }
}

Describe 'Type that has to fit a disc or a spine' -Tag 'Unit' {

    BeforeAll {
        Add-Type -AssemblyName System.Drawing
        $script:Canvas = New-Object System.Drawing.Bitmap 100, 100
        $script:G = [System.Drawing.Graphics]::FromImage($script:Canvas)
    }

    AfterAll {
        if ($script:G) { $script:G.Dispose() }
        if ($script:Canvas) { $script:Canvas.Dispose() }
    }

    It 'comes down in size until the words actually fit' {
        $long = 'The Chronicles of Riddick: Escape from Butcher Bay'
        $font = Get-FittedFont -Graphics $script:G -Text $long -MaxWidthPx 600 -StartPt 40 -MinPt 6
        try {
            $script:G.MeasureString($long, $font).Width | Should -BeLessOrEqual 600
            $font.SizeInPoints | Should -BeLessThan 40
        } finally { $font.Dispose() }
    }

    It 'leaves a short title at the size asked for' {
        $font = Get-FittedFont -Graphics $script:G -Text 'Gothic' -MaxWidthPx 600 -StartPt 20 -MinPt 6
        try { $font.SizeInPoints | Should -Be 20 } finally { $font.Dispose() }
    }

    It 'stops at the floor rather than shrinking to nothing' {
        # No size fits this, so the caller gets the smallest it allowed and can
        # decide, instead of getting a 0.5 pt font nobody can read.
        $font = Get-FittedFont -Graphics $script:G -Text ('x' * 400) -MaxWidthPx 50 -StartPt 20 -MinPt 7
        try { $font.SizeInPoints | Should -Be 7 } finally { $font.Dispose() }
    }

    It 'falls back to a font that exists when the one asked for does not' {
        $font = Get-FittedFont -Graphics $script:G -Text 'Gothic' -MaxWidthPx 600 -StartPt 12 `
                               -Family 'No Such Typeface Anywhere'
        try { $font.Name | Should -Not -BeNullOrEmpty } finally { $font.Dispose() }
    }
}

Describe 'Reading a colour the caller typed' -Tag 'Unit' {

    It 'takes <Hex> as R<R> G<G> B<B>' -ForEach @(
        @{ Hex = '#1B2A41'; R = 27;  G = 42;  B = 65 }
        @{ Hex = '1B2A41';  R = 27;  G = 42;  B = 65 }
        @{ Hex = '#FFFFFF'; R = 255; G = 255; B = 255 }
    ) {
        $c = ConvertFrom-HexColour $Hex
        $c.R | Should -Be $R
        $c.G | Should -Be $G
        $c.B | Should -Be $B
    }
}

Describe 'The face that goes on the disc' -Tag 'Unit' {

    BeforeAll {
        Add-Type -AssemblyName System.Drawing

        # Sampling the rendered pixels is the only honest check here: the
        # printable ring, the clear hub and the accent colour are all things
        # that either ended up in the image or did not.
        function Get-FacePixel($bitmap, [double]$atMmFromCentre, [double]$angleDeg = 0) {
            $centre = ($bitmap.Width - 1) / 2.0
            $r = ConvertTo-Px $atMmFromCentre
            $x = [int][Math]::Round($centre + ($r * [Math]::Cos($angleDeg * [Math]::PI / 180)))
            $y = [int][Math]::Round($centre + ($r * [Math]::Sin($angleDeg * [Math]::PI / 180)))
            return $bitmap.GetPixel($x, $y)
        }
    }

    Context 'on a <Name>' -ForEach $script:DiscFormats {

            BeforeAll {
                $script:Png = Join-Path $script:Sandbox "face-$Key.png"
                $null = New-DiscFace -OutPng $script:Png -Title 'Gothic' -Subtitle 'GOG edition' `
                                     -Disc $Key
                $script:Face = New-Object System.Drawing.Bitmap $script:Png
            }

            AfterAll { if ($script:Face) { $script:Face.Dispose() } }

            It 'is a 120 mm square at 300 dpi, which is the whole disc' {
                $script:Face.Width  | Should -Be (ConvertTo-Px 120.0)
                $script:Face.Height | Should -Be (ConvertTo-Px 120.0)
                [Math]::Round($script:Face.HorizontalResolution) | Should -Be 300
            }

            It 'leaves the hub clear, so no ink lands on the clamp' {
                (Get-FacePixel $script:Face 0).A | Should -Be 0
                # Just inside the printable edge of the hub, still clear.
                (Get-FacePixel $script:Face (($InnerMm / 2) - 1)).A | Should -Be 0
            }

            It 'puts ink where the disc can take it' {
                # A millimetre outside the hub, and a millimetre inside the rim.
                (Get-FacePixel $script:Face (($InnerMm / 2) + 1)).A | Should -BeGreaterThan 0
                (Get-FacePixel $script:Face (($OuterMm / 2) - 1)).A | Should -BeGreaterThan 0
            }

            It 'puts no ink past the printable rim' {
                # Half a millimetre, not one: a 118 mm rim plus 1 mm is outside
                # the 120 mm image, and the sample falls off the bitmap.
                (Get-FacePixel $script:Face (($OuterMm / 2) + 0.5)).A | Should -Be 0
                # The corner of the square is well outside any disc.
                $script:Face.GetPixel(2, 2).A | Should -Be 0
            }
    }

    It 'uses the accent colour it was given' {
        # A local that differs from a typed parameter only by case IS that
        # parameter, so this once rendered every disc in the default navy no
        # matter what was asked for, silently. Pixels, not promises.
        $png = Join-Path $script:Sandbox 'face-accent.png'
        $null = New-DiscFace -OutPng $png -Title 'Gothic' -Accent '#B02020'
        $face = New-Object System.Drawing.Bitmap $png
        try {
            $centre = ($face.Width - 1) / 2.0
            $px = $face.GetPixel([int]$centre, [int]($centre - (ConvertTo-Px 30.0)))
            $px.R | Should -BeGreaterThan $px.B -Because 'a red accent has to come out red'
            $px.R | Should -BeGreaterThan 100
        } finally { $face.Dispose() }
    }

    It 'takes no picture at all any more, so it cannot crop one' {
        # Fitting somebody's artwork into this layout meant cropping it. The
        # parameter is gone rather than deprecated, so there is no accidental
        # way back to that behaviour. A picture goes to the artwork renderer,
        # which prints it as it is.
        (Get-Command New-DiscFace).Parameters.Keys | Should -Not -Contain 'CoverImage'
        (Get-Command New-DiscFaceFromArtwork).Parameters.Keys | Should -Contain 'Artwork'
    }

    It 'fits a long title instead of running it off the disc' {
        $png = Join-Path $script:Sandbox 'face-longtitle.png'
        { New-DiscFace -OutPng $png -Disc 'hub' `
                       -Title 'The Chronicles of Riddick: Escape from Butcher Bay' } |
            Should -Not -Throw
        (Get-Item $png).Length | Should -BeGreaterThan 1000
    }
}

Describe 'The wrap that goes in the case' -Tag 'Unit' {

    BeforeAll {
        Add-Type -AssemblyName System.Drawing

        # The wrap goes out as a PDF, so the pixels are reached by pulling the
        # page image back out of it. That also proves the JPEG inside the PDF
        # is the picture that was drawn, rather than something empty.
        function Get-PageBitmap([string]$pdf) {
            $bytes = [IO.File]::ReadAllBytes($pdf)
            $start = -1
            for ($i = 0; $i -lt $bytes.Length - 1; $i++) {
                if ($bytes[$i] -eq 0xFF -and $bytes[$i + 1] -eq 0xD8) { $start = $i; break }
            }
            $end = -1
            for ($i = $bytes.Length - 2; $i -gt $start; $i--) {
                if ($bytes[$i] -eq 0xFF -and $bytes[$i + 1] -eq 0xD9) { $end = $i + 1; break }
            }
            if ($start -lt 0 -or $end -lt 0) { throw 'no page image in the PDF' }
            $jpg = Join-Path $script:Sandbox ([IO.Path]::GetFileNameWithoutExtension($pdf) + '.jpg')
            [IO.File]::WriteAllBytes($jpg, $bytes[$start..$end])
            return (New-Object System.Drawing.Bitmap $jpg)
        }

        function Test-NearlyWhite($pixel) {
            return ($pixel.R -gt 245 -and $pixel.G -gt 245 -and $pixel.B -gt 245)
        }
    }

    # Every case in the table, because a wrap that only works for the one case
    # the author owns is how the other rows rot.
    Context 'for a <Name>' -ForEach $script:CaseFormats {

            BeforeAll {
                $script:Pdf = Join-Path $script:Sandbox "wrap-$Key.pdf"
                $null = New-CaseWrap -OutPdf $script:Pdf -Case $Key -Title 'Gothic' `
                                     -Subtitle 'GOG edition' -Contents @('Gothic', 'Gothic II')
                $script:Page = Get-PageBitmap $script:Pdf
            }

            AfterAll { if ($script:Page) { $script:Page.Dispose() } }

            It 'is a page of the size that page really is' {
                $text = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($script:Pdf))
                $text | Should -Match ([regex]::Escape("/MediaBox [0 0 $(ConvertTo-Points 297) $(ConvertTo-Points 210)]"))
            }

            It 'puts ink across the whole trim, and leaves the page margin clean' {
                $cx = $script:Page.Width / 2
                $cy = $script:Page.Height / 2
                # The middle of the sheet is the spine, which is always printed.
                Test-NearlyWhite $script:Page.GetPixel($cx, $cy) | Should -BeFalse
                # 2 mm in from the page edge is outside the bleed on every
                # format in the table, so it must still be paper.
                Test-NearlyWhite $script:Page.GetPixel((ConvertTo-Px 2.0), $cy) | Should -BeTrue
            }

            It 'bleeds past the trim, so a crooked cut does not leave a white edge' {
                $cy = $script:Page.Height / 2
                $trimLeft = ((ConvertTo-Px 297.0) - (ConvertTo-Px $WrapWidthMm)) / 2
                # A millimetre outside the trim is bleed, and it has to be inked.
                $outside = [int]($trimLeft - (ConvertTo-Px 1.0))
                Test-NearlyWhite $script:Page.GetPixel($outside, $cy) | Should -BeFalse
            }
    }

    It 'puts the front panel on the right, where the sheet wraps it to the front' {
        # Not a style choice: the sheet goes round the case from the back. The
        # front carries the big title over a dark scrim, so the foot of the
        # right-hand panel is darker than the foot of the left-hand one.
        $pdf = Join-Path $script:Sandbox 'wrap-sides.pdf'
        $null = New-CaseWrap -OutPdf $pdf -Title 'Gothic' -Subtitle 'GOG edition'
        $page = Get-PageBitmap $pdf
        try {
            $y = [int]($page.Height * 0.86)
            $left = $page.GetPixel([int]($page.Width * 0.22), $y)
            $right = $page.GetPixel([int]($page.Width * 0.78), $y)
            ($right.R + $right.G + $right.B) | Should -BeLessThan ($left.R + $left.G + $left.B)
        } finally { $page.Dispose() }
    }

    It 'marks where to cut, outside the bleed so the marks are cut away with it' {
        $pdf = Join-Path $script:Sandbox 'wrap-marks.pdf'
        $null = New-CaseWrap -OutPdf $pdf -Title 'Gothic' -BleedMm 3
        $page = Get-PageBitmap $pdf
        try {
            $trimLeft = ((ConvertTo-Px 297.0) - (ConvertTo-Px 273.0)) / 2
            $trimTop  = ((ConvertTo-Px 210.0) - (ConvertTo-Px 183.0)) / 2
            # A crop mark runs up from 4 mm above the trim corner: 3 mm of
            # bleed and a 1 mm gap. Scan a short band for dark pixels.
            $found = $false
            for ($dy = 5; $dy -le 9; $dy++) {
                for ($dx = -2; $dx -le 2; $dx++) {
                    $p = $page.GetPixel([int]($trimLeft + $dx), [int]($trimTop - (ConvertTo-Px ([double]$dy))))
                    if (-not (Test-NearlyWhite $p)) { $found = $true }
                }
            }
            $found | Should -BeTrue -Because 'there has to be a crop mark above the trim corner'
        } finally { $page.Dispose() }
    }

    It 'leaves the marks off when asked, for printing straight onto trimmed stock' {
        $withMarks = Join-Path $script:Sandbox 'wrap-with.pdf'
        $without = Join-Path $script:Sandbox 'wrap-without.pdf'
        $null = New-CaseWrap -OutPdf $withMarks -Title 'Gothic'
        $null = New-CaseWrap -OutPdf $without -Title 'Gothic' -NoCropMarks
        (Get-Item $without).Length | Should -BeLessThan (Get-Item $withMarks).Length
    }

    It 'refuses a bleed that will not fit, instead of cropping the artwork' {
        # 273 mm of wrap plus 13 mm of bleed each side is 299 mm, wider than A4.
        { New-CaseWrap -OutPdf (Join-Path $script:Sandbox 'wrap-toobig.pdf') `
                       -Title 'Gothic' -BleedMm 13 } |
            Should -Throw -ExpectedMessage '*does not fit*'
    }

    It 'takes no pictures any more: this is the plain label and nothing else' {
        $keys = (Get-Command New-CaseWrap).Parameters.Keys
        $keys | Should -Not -Contain 'CoverImage'
        $keys | Should -Not -Contain 'BackImage'
        (Get-Command New-WrapFromArtwork).Parameters.Keys | Should -Contain 'Artwork'
    }
}

Describe 'How many panels a case takes' -Tag 'Unit' {

    # This was found by rendering every row and looking at the result: the CD
    # insert came out as two half-width panels either side of a spine with no
    # width, with the title crushed into it. Nothing in the suite objected,
    # because nothing in the suite knew a sheet could have one panel.

    It 'is declared by every case, as one or two' {
        foreach ($c in $script:CaseFormats) {
            $c.ContainsKey('Panels') | Should -BeTrue -Because "$($c.Key) must say"
            $c.Panels | Should -BeIn @(1, 2)
        }
    }

    It 'agrees with the spine: two panels have one, a single panel has none' {
        foreach ($c in $script:CaseFormats) {
            if ($c.Panels -eq 2) {
                $c.SpineMm | Should -BeGreaterThan 0 -Because "$($c.Key) folds round a case"
            } else {
                $c.SpineMm | Should -Be 0 -Because "$($c.Key) is a flat insert"
            }
        }
    }

    It 'gives a single panel the whole width, not half of it' {
        $cd = Get-CaseFormat 'cd'
        Get-PanelWidthMm $cd | Should -Be $cd.WrapWidthMm
        # And a two-panel case still splits what the spine leaves.
        $dvd = Get-CaseFormat 'dvd'
        Get-PanelWidthMm $dvd | Should -Be (($dvd.WrapWidthMm - $dvd.SpineMm) / 2)
    }

    It 'says out loud that a contents list has nowhere to go on an insert' {
        # Dropping it silently is the failure mode this project keeps finding
        # and keeps refusing: say what was not done.
        $warnings = @()
        $null = New-CaseWrap -OutPdf (Join-Path $script:Sandbox 'wrap-cd-warn.pdf') -Case 'cd' `
                             -Title 'Gothic' -Contents @('Gothic', 'Gothic II') `
                             -WarningVariable warnings -WarningAction SilentlyContinue
        $warnings.Count | Should -BeGreaterThan 0
        "$warnings" | Should -Match 'single panel'
    }

    It 'says nothing when an insert is given nothing to drop' {
        $warnings = @()
        $null = New-CaseWrap -OutPdf (Join-Path $script:Sandbox 'wrap-cd-quiet.pdf') -Case 'cd' `
                             -Title 'Gothic' -WarningVariable warnings -WarningAction SilentlyContinue
        $warnings.Count | Should -Be 0
    }
}

Describe 'A file name made from a disc title' -Tag 'Unit' {

    It 'turns <Title> into <Stem>' -ForEach @(
        @{ Title = 'Gothic';                    Stem = 'Gothic' }
        @{ Title = 'Alan Wake';                 Stem = 'Alan-Wake' }
        # Colons and question marks are ordinary in game titles and illegal in
        # Windows file names, and finding that out after drawing everything is
        # a poor way to spend a render.
        @{ Title = 'Riddick: Butcher Bay';      Stem = 'Riddick-Butcher-Bay' }
        @{ Title = 'Where in the World?';       Stem = 'Where-in-the-World' }
        @{ Title = '  spaced   out  ';          Stem = 'spaced-out' }
        @{ Title = '';                          Stem = 'disc' }
        @{ Title = '???';                       Stem = 'disc' }
    ) {
        Get-SafeFileStem $Title | Should -Be $Stem
    }

    It 'keeps a very long title down to something a file system will take' {
        $stem = Get-SafeFileStem ('Very ' * 40 + 'Long')
        $stem.Length | Should -BeLessOrEqual 60
        $stem | Should -Not -Match '-$'
    }
}

Describe 'Artwork for a disc the app has already planned' -Tag 'Unit' {

    BeforeAll {
        Add-Type -AssemblyName System.Drawing

        # A project file as DiscWright writes it, with only the keys this reads.
        function New-TestProject {
            param([hashtable]$Overrides = @{}, [string]$Name = 'p')
            $dir = Join-Path $script:Sandbox "proj-$Name-$(Get-Random)"
            New-Item -ItemType Directory -Force -Path $dir | Out-Null
            $p = [ordered]@{
                Version = 9; AppVersion = '0.8.1'
                Label = 'GOTHIC'; TitleText = 'Gothic'
                ShowTitle = $false; BgPath = $null; IconPath = $null
                Games = @(
                    [ordered]@{ GameName = 'Gothic';     Kind = 'Game';  Source = 'GOG' }
                    [ordered]@{ GameName = 'Gothic II';  Kind = 'Game';  Source = 'GOG' }
                    [ordered]@{ GameName = 'Patch 1.1';  Kind = 'AddOn'; Source = 'GOG' }
                )
            }
            foreach ($k in $Overrides.Keys) { $p[$k] = $Overrides[$k] }
            $file = Join-Path $dir 'discproject.json'
            $p | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $file -Encoding UTF8
            return $file
        }

        function New-TestCover([string]$path, [int]$w = 600, [int]$h = 900) {
            $bmp = New-Object System.Drawing.Bitmap $w, $h
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            $g.Clear([System.Drawing.Color]::DarkRed)
            $g.Dispose()
            $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Jpeg)
            $bmp.Dispose()
            return $path
        }
    }

    It 'takes the title the person typed for the menu' {
        $r = New-ArtworkForProject -ProjectPath (New-TestProject)
        $r.Title | Should -Be 'Gothic'
    }

    It 'falls back to the volume label, and then to something rather than nothing' {
        $r = New-ArtworkForProject -ProjectPath (New-TestProject @{ TitleText = '' })
        $r.Title | Should -Be 'GOTHIC'
        $r2 = New-ArtworkForProject -ProjectPath (New-TestProject @{ TitleText = ''; Label = '' })
        $r2.Title | Should -Be 'Untitled disc'
    }

    It 'lists the games and counts the add-ons rather than listing them beside their game' {
        $r = New-ArtworkForProject -ProjectPath (New-TestProject)
        $r.Contents | Should -Contain 'Gothic'
        $r.Contents | Should -Contain 'Gothic II'
        $r.Contents | Should -Contain '1 add-on'
        $r.Contents | Should -Not -Contain 'Patch 1.1'
    }

    It 'says add-ons in the plural when there is more than one' {
        $games = @(
            [ordered]@{ GameName = 'Gothic';   Kind = 'Game' }
            [ordered]@{ GameName = 'Patch 1';  Kind = 'AddOn' }
            [ordered]@{ GameName = 'Patch 2';  Kind = 'AddOn' }
        )
        $r = New-ArtworkForProject -ProjectPath (New-TestProject @{ Games = $games })
        $r.Contents | Should -Contain '2 add-ons'
    }

    It 'uses the background image the person already chose as the cover' {
        $dir = Split-Path (New-TestProject) -Parent
        $cover = New-TestCover (Join-Path $dir 'bg.jpg')
        $r = New-ArtworkForProject -ProjectPath (New-TestProject @{ BgPath = $cover })
        $r.UsedCover | Should -BeTrue
        $r.CoverImage | Should -Be $cover
    }

    It 'carries on without the cover when the project has moved machines' {
        # Not an error. A project copied from another PC points at a path that
        # is not there, and a wrap in the accent colour is better than a throw.
        $r = New-ArtworkForProject -ProjectPath (New-TestProject @{ BgPath = 'Z:\gone\bg.jpg' })
        $r.UsedCover | Should -BeFalse
        Test-Path $r.Wrap | Should -BeTrue
    }

    It 'prints a picture untouched whatever the menu title setting says' {
        # That setting decided whether a title was drawn over cover art. Nothing
        # is drawn over a picture any more, so it cannot apply, and the same
        # cover has to come out identical either way.
        $dir = Split-Path (New-TestProject) -Parent
        $cover = New-TestCover (Join-Path $dir 'bg2.jpg')
        $off = New-ArtworkForProject -ProjectPath (New-TestProject @{ BgPath = $cover; ShowTitle = $false })
        $on = New-ArtworkForProject -ProjectPath (New-TestProject @{ BgPath = $cover; ShowTitle = $true })
        $off.WrapFromArtwork | Should -BeTrue
        $on.WrapFromArtwork  | Should -BeTrue
        (Get-Item $off.Wrap).Length | Should -Be (Get-Item $on.Wrap).Length
    }

    It 'still letters the cover when there is no cover art to fight with' {
        $r = New-ArtworkForProject -ProjectPath (New-TestProject @{ ShowTitle = $false })
        $r.TitleOnCover | Should -BeTrue -Because 'a plain colour panel with no title says nothing'
    }

    It 'writes both pieces, named after the disc' {
        $r = New-ArtworkForProject -ProjectPath (New-TestProject @{ TitleText = 'Riddick: Butcher Bay' })
        Test-Path $r.Wrap | Should -BeTrue
        Test-Path $r.DiscFace | Should -BeTrue
        (Split-Path $r.Wrap -Leaf) | Should -BeLike 'Riddick-Butcher-Bay-wrap-*'
        (Split-Path $r.DiscFace -Leaf) | Should -BeLike 'Riddick-Butcher-Bay-disc-face-*'
    }

    It 'takes the folder as well as the file, because that is what people have' {
        $file = New-TestProject
        $r = New-ArtworkForProject -ProjectPath (Split-Path $file -Parent)
        $r.ProjectFile | Should -Be $file
    }

    It 'says what is missing rather than failing obscurely' {
        { New-ArtworkForProject -ProjectPath 'Z:\nothing\here' } |
            Should -Throw -ExpectedMessage '*No project file*'

        $broken = Join-Path $script:Sandbox 'broken-project'
        New-Item -ItemType Directory -Force -Path $broken | Out-Null
        Set-Content -LiteralPath (Join-Path $broken 'discproject.json') -Value 'not json at all'
        { New-ArtworkForProject -ProjectPath $broken } |
            Should -Throw -ExpectedMessage '*not a readable project file*'
    }
}

Describe 'Printing artwork somebody else made' -Tag 'Unit' {

    # There is a community that makes these, and a collector who found a cover
    # for their game does not want a layout put on top of it. For them this is
    # not a designer: it is the part that gets the millimetres right.

    BeforeAll {
        Add-Type -AssemblyName System.Drawing

        function New-FlatArt([int]$w, [int]$h, [System.Drawing.Color]$colour, [string]$name) {
            $path = Join-Path $script:Sandbox "$name.png"
            $bmp = New-Object System.Drawing.Bitmap $w, $h
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            $g.Clear($colour); $g.Dispose()
            $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
            return $path
        }

        # Edges in a colour of their own, so a crop shows up as a missing band.
        function New-EdgedArt([int]$w, [int]$h, [string]$name) {
            $path = Join-Path $script:Sandbox "$name.png"
            $bmp = New-Object System.Drawing.Bitmap $w, $h
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            $g.Clear([System.Drawing.Color]::FromArgb(40, 40, 40))
            $edge = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::Yellow)
            $band = [int]([Math]::Max(8, $w * 0.02))
            $g.FillRectangle($edge, 0, 0, $band, $h)
            $g.FillRectangle($edge, $w - $band, 0, $band, $h)
            $g.FillRectangle($edge, 0, 0, $w, $band)
            $g.FillRectangle($edge, 0, $h - $band, $w, $band)
            $edge.Dispose(); $g.Dispose()
            $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
            return $path
        }

        function Get-PageBmp([string]$pdf) {
            $bytes = [IO.File]::ReadAllBytes($pdf)
            $s = -1; for ($i = 0; $i -lt $bytes.Length - 1; $i++) {
                if ($bytes[$i] -eq 0xFF -and $bytes[$i + 1] -eq 0xD8) { $s = $i; break } }
            $e = -1; for ($i = $bytes.Length - 2; $i -gt $s; $i--) {
                if ($bytes[$i] -eq 0xFF -and $bytes[$i + 1] -eq 0xD9) { $e = $i + 1; break } }
            $jpg = [IO.Path]::ChangeExtension($pdf, '.page.jpg')
            [IO.File]::WriteAllBytes($jpg, $bytes[$s..$e])
            return (New-Object System.Drawing.Bitmap $jpg)
        }

        function Test-Yellowish($p) { return ($p.R -gt 180 -and $p.G -gt 180 -and $p.B -lt 120) }
    }

    It 'knows a full wrap from a front panel by its shape' {
        $dvd = Get-CaseFormat 'dvd'
        (Get-ArtworkKind -Width 3224 -Height 2161 -Case $dvd).Kind | Should -Be 'wrap'
        (Get-ArtworkKind -Width 1530 -Height 2161 -Case $dvd).Kind | Should -Be 'front'
        # A 16:9 picture is nearer a wrap than a panel, and saying how far off it
        # is matters more than which it picked.
        $wide = Get-ArtworkKind -Width 1920 -Height 1080 -Case $dvd
        $wide.OffByPct | Should -BeGreaterThan 10
    }

    It 'says a correctly sized wrap is not off at all' {
        $dvd = Get-CaseFormat 'dvd'
        (Get-ArtworkKind -Width 3224 -Height 2161 -Case $dvd).OffByPct | Should -Be 0
    }

    It 'prints a full wrap at the full trim width' {
        $art = New-EdgedArt 3224 2161 'wrap-art'
        $r = New-WrapFromArtwork -OutPdf (Join-Path $script:Sandbox 'art-wrap.pdf') -Artwork $art
        $r.Kind | Should -Be 'wrap'
        $r.TrimWidthMm | Should -Be 273
        $r.TrimHeightMm | Should -Be 183
    }

    It 'prints a front-only cover at panel width, not stretched across the case' {
        $art = New-EdgedArt 1530 2161 'front-art'
        $r = New-WrapFromArtwork -OutPdf (Join-Path $script:Sandbox 'art-front.pdf') -Artwork $art
        $r.Kind | Should -Be 'front'
        $r.TrimWidthMm | Should -Be 129.5
    }

    It 'keeps all four edges of the artwork, because cropping somebody else`s cover is not ours to do' {
        $art = New-EdgedArt 3224 2161 'edges-art'
        $pdf = Join-Path $script:Sandbox 'art-edges.pdf'
        $null = New-WrapFromArtwork -OutPdf $pdf -Artwork $art
        $page = Get-PageBmp $pdf
        try {
            $trimX = [int](((ConvertTo-Px 297.0) - (ConvertTo-Px 273.0)) / 2)
            $trimY = [int](((ConvertTo-Px 210.0) - (ConvertTo-Px 183.0)) / 2)
            $trimW = ConvertTo-Px 273.0
            $trimH = ConvertTo-Px 183.0
            $midY = $trimY + [int]($trimH / 2)
            $midX = $trimX + [int]($trimW / 2)
            # Just inside each edge of the trim, the artwork's own yellow band.
            Test-Yellowish $page.GetPixel(($trimX + 10), $midY)              | Should -BeTrue -Because 'left edge'
            Test-Yellowish $page.GetPixel(($trimX + $trimW - 10), $midY)     | Should -BeTrue -Because 'right edge'
            Test-Yellowish $page.GetPixel($midX, ($trimY + 10))              | Should -BeTrue -Because 'top edge'
            Test-Yellowish $page.GetPixel($midX, ($trimY + $trimH - 10))     | Should -BeTrue -Because 'bottom edge'
        } finally { $page.Dispose() }
    }

    It 'invents the bleed the artwork has not got, by carrying its edge outwards' {
        $art = New-EdgedArt 3224 2161 'bleed-art'
        $pdf = Join-Path $script:Sandbox 'art-bleed.pdf'
        $null = New-WrapFromArtwork -OutPdf $pdf -Artwork $art -BleedMm 3
        $page = Get-PageBmp $pdf
        try {
            $trimX = [int](((ConvertTo-Px 297.0) - (ConvertTo-Px 273.0)) / 2)
            $midY = [int]((ConvertTo-Px 210.0) / 2)
            # 1.5 mm outside the trim is bleed, and it has to carry the yellow
            # rather than leave white for a crooked cut to find.
            $out = $trimX - [int](ConvertTo-Px 1.5)
            Test-Yellowish $page.GetPixel($out, $midY) | Should -BeTrue
        } finally { $page.Dispose() }
    }

    It 'refuses artwork that is not there, and a bleed that will not fit' {
        { New-WrapFromArtwork -OutPdf (Join-Path $script:Sandbox 'x.pdf') -Artwork 'Z:\none.png' } |
            Should -Throw -ExpectedMessage '*No artwork at*'
        $art = New-FlatArt 3224 2161 ([System.Drawing.Color]::Red) 'toobig-art'
        { New-WrapFromArtwork -OutPdf (Join-Path $script:Sandbox 'y.pdf') -Artwork $art -BleedMm 13 } |
            Should -Throw -ExpectedMessage '*does not fit*'
    }

    It 'puts nothing of its own on a finished disc face' {
        # The giveaway would be the title band this app draws on artwork it
        # composes itself. A solid red face has to come back solid red.
        $art = New-FlatArt 1400 1400 ([System.Drawing.Color]::FromArgb(200, 30, 30)) 'disc-art'
        $png = Join-Path $script:Sandbox 'art-face.png'
        $r = New-DiscFaceFromArtwork -OutPng $png -Artwork $art -Disc 'hub'
        $r.Png | Should -Exist
        $face = New-Object System.Drawing.Bitmap $png
        try {
            $c = ($face.Width - 1) / 2.0
            foreach ($mm in 15, 25, 35, 45, 55) {
                foreach ($deg in 0, 90, 180, 270) {
                    $rad = ConvertTo-Px ([double]$mm)
                    $x = [int][Math]::Round($c + ($rad * [Math]::Cos($deg * [Math]::PI / 180)))
                    $y = [int][Math]::Round($c + ($rad * [Math]::Sin($deg * [Math]::PI / 180)))
                    $p = $face.GetPixel($x, $y)
                    if ($mm -lt 11) { continue }
                    $p.R | Should -BeGreaterThan 150 -Because "at $mm mm, $deg degrees, the art is red"
                    $p.G | Should -BeLessThan 90
                }
            }
            # And the hub is still taken out.
            $face.GetPixel([int]$c, [int]$c).A | Should -Be 0
        } finally { $face.Dispose() }
    }
}

Describe 'Lettering that sits in the middle of the spine' -Tag 'Unit' {

    # xniwo: "The dvd spine label is slightly off centre to the right (not a
    # major issue)." Measured off a rendered wrap, it was 1.48 mm on a 14 mm
    # spine, which is eleven percent of the band.
    #
    # The cause is that centring a string in a rectangle centres the LINE box,
    # ascent plus descent, and a title in capitals uses none of the descent. The
    # ink therefore rides high in the box, and the box is rotated a quarter turn
    # on a spine, so high reads as right.
    #
    # Two things went wrong fixing it, and both are worth knowing. The spine
    # band was assumed to be where the arithmetic said rather than measured off
    # the page. And the ink was measured on a scratch bitmap left at the 96 dpi
    # a new Bitmap defaults to, while the page draws at 300: a font is sized in
    # points, so every offset came back a third of its real size.

    BeforeAll {
        Add-Type -AssemblyName System.Drawing
        $script:InkFont = New-Object System.Drawing.Font('Segoe UI', 18)
    }
    AfterAll { if ($script:InkFont) { $script:InkFont.Dispose() } }

    It 'measures where the ink is, not where the line box is' {
        $ink = Get-TextInk -Text 'HOLLOW KNIGHT' -Font $script:InkFont
        $ink.InkHeight | Should -BeGreaterThan 0
        # Capitals leave the descent empty, so the ink's middle sits above the
        # middle of the line box. That gap is the whole bug.
        $boxMiddle = $ink.InkTop + (($ink.InkBottom - $ink.InkTop) / 2.0)
        $boxMiddle | Should -BeLessThan ($ink.InkBottom)
    }

    It 'measures at the resolution the page prints at' {
        # The same string must come back bigger than it would on a 96 dpi
        # surface, or every offset taken from it is a third of what it should be.
        $ink = Get-TextInk -Text 'HOLLOW KNIGHT' -Font $script:InkFont
        $ink.InkHeight | Should -BeGreaterThan 40 -Because '18 pt at 300 dpi is about 75 px tall, not 24'
    }

    It 'finds a descender, so the measurement is of ink and not of a guess' {
        $caps = Get-TextInk -Text 'HOLLOW' -Font $script:InkFont
        $desc = Get-TextInk -Text 'happy' -Font $script:InkFont
        $desc.InkBottom | Should -BeGreaterThan $caps.InkBottom -Because 'p and y go below the baseline'
    }

    It 'says nothing for an empty title rather than throwing' {
        $ink = Get-TextInk -Text '' -Font $script:InkFont
        $ink.Width | Should -Be 0
        $ink.InkHeight | Should -Be 0
    }

    It 'places the spine title by its ink' {
        # Pinned in the source, because rendering a wrap and measuring the band
        # takes seconds and belongs in the hardware notes, not in every run.
        $src = Get-Content -Raw (Join-Path (Split-Path $PSScriptRoot -Parent) 'print\DiscWright.Print.ps1')
        $src | Should -Match ([regex]::Escape('$ink = Get-TextInk -Text $Title -Font $spineFont'))
        $src | Should -Match ([regex]::Escape('[single](-($ink.InkTop + $ink.InkBottom) / 2)'))
        $src | Should -Not -Match ([regex]::Escape('$g.DrawString($Title, $spineFont, $white, $spineBox, $middle)'))
    }

    It 'leaves the dialog to wrap its own sentences' {
        # xniwo again: a line break mid-sentence at "which sets the diameters".
        # A hand wrap is set to one width; the box is whatever width Windows
        # gives it.
        $app = Get-Content -Raw (Join-Path (Split-Path $PSScriptRoot -Parent) 'DiscWright.ps1')
        $app | Should -Match ([regex]::Escape('which sets the diameters and lines the tray up.'))
        $app | Should -Not -Match ([regex]::Escape('which sets the`r`n'))
    }
}

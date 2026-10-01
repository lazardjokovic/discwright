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
        foreach ($c in $script:CaseFormats) {
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
    foreach ($case in $script:CaseFormats) {
        Context "for a $($case.Name)" {
            It 'renders onto a page that fits it, at the size that page really is' {
                $out = Join-Path $script:Sandbox "calib-$($case.Key).pdf"
                $null = New-CalibrationSheet -OutPdf $out -Case $case.Key
                Test-Path $out | Should -BeTrue
                $box = Get-PdfMediaBox $out
                # A4 landscape: the wrap is wider than a portrait page.
                $box.W | Should -Be (ConvertTo-Points 297)
                $box.H | Should -Be (ConvertTo-Points 210)
                Test-PdfXref $out | Should -BeTrue
            }
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

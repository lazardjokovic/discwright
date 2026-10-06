<#
    Burning, as far as it can be tested without spending a disc.

    Everything here runs with no media in the drive and writes nothing. What
    cannot be tested this way is the write itself, and that is said plainly
    rather than faked: a mocked burn proves nothing about a drive.

    The arithmetic is worth pinning precisely because a mistake in it is only
    discovered when a burn stops partway, and a CD-R is written once.
#>

# Loaded here, not only in BeforeAll: a foreach that builds Context blocks runs
# during discovery, before BeforeAll has happened.
. (Join-Path (Split-Path $PSScriptRoot -Parent) 'burn\DiscWright.Burn.ps1')

BeforeAll {
    . (Join-Path (Split-Path $PSScriptRoot -Parent) 'burn\DiscWright.Burn.ps1')
    $script:Sandbox = Join-Path ([IO.Path]::GetTempPath()) ('dwburn_' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Force -Path $script:Sandbox | Out-Null
}

AfterAll {
    if ($script:Sandbox -and (Test-Path $script:Sandbox)) {
        Remove-Item -LiteralPath $script:Sandbox -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Describe 'Naming what the drive reports' -Tag 'Unit' {

    It 'turns media type <Code> into <Name>' -ForEach @(
        @{ Code = 0;  Name = 'no disc' }
        @{ Code = 2;  Name = 'CD-R' }
        @{ Code = 3;  Name = 'CD-RW' }
        @{ Code = 9;  Name = 'DVD-R' }
        @{ Code = 18; Name = 'BD-R' }
    ) {
        Get-MediaTypeName $Code | Should -Be $Name
    }

    It 'says so rather than inventing a name for a code it does not know' {
        Get-MediaTypeName 250 | Should -Be 'unknown media type 250'
    }

    It 'knows a pressed disc cannot be written to' {
        Test-MediaWritable 1  | Should -BeFalse -Because 'a pressed CD-ROM is not writable'
        Test-MediaWritable 4  | Should -BeFalse -Because 'a pressed DVD-ROM is not writable'
        Test-MediaWritable 17 | Should -BeFalse -Because 'a pressed BD-ROM is not writable'
        Test-MediaWritable 2  | Should -BeTrue  -Because 'a CD-R is what this project burns'
        Test-MediaWritable 9  | Should -BeTrue
    }

    It 'turns the profile numbers a drive lists into names a person can read' {
        $names = ConvertTo-ProfileNames @(9, 10, 17, 27, 65)
        $names | Should -Contain 'CD-R'
        $names | Should -Contain 'CD-RW'
        $names | Should -Contain 'DVD-R'
        $names | Should -Contain 'DVD+R'
        $names | Should -Contain 'BD-R'
    }

    It 'drops profile numbers that are not about writing, rather than guessing' {
        ConvertTo-ProfileNames @(1, 2, 8) | Should -BeNullOrEmpty
    }
}

Describe 'Whether an ISO will fit the disc that is in the drive' -Tag 'Unit' {

    # The real numbers: a 700 MB CD-R is 360,000 sectors of 2048 bytes, which is
    # 737,280,000 bytes, and a 4.7 GB DVD-R is 2,298,496 sectors.
    It 'fits a small ISO on a CD-R and says how much is left' {
        $fit = Test-IsoFitsMedia -IsoBytes 100MB -FreeSectors 360000
        $fit.Fits | Should -BeTrue
        $fit.NeededSectors | Should -Be 51200
        $fit.SpareSectors | Should -Be 308800
    }

    It 'refuses a DVD-sized ISO on a CD-R' {
        $fit = Test-IsoFitsMedia -IsoBytes 4GB -FreeSectors 360000
        $fit.Fits | Should -BeFalse
        $fit.SpareSectors | Should -BeLessThan 0
    }

    It 'counts in whole sectors, because a drive cannot write part of one' {
        # One byte past a sector boundary still costs a whole sector, and
        # rounding this the other way starts a burn that cannot finish.
        (Test-IsoFitsMedia -IsoBytes 2049 -FreeSectors 2).NeededSectors | Should -Be 2
        (Test-IsoFitsMedia -IsoBytes 2048 -FreeSectors 1).NeededSectors | Should -Be 1
        (Test-IsoFitsMedia -IsoBytes 1    -FreeSectors 1).NeededSectors | Should -Be 1
    }

    It 'allows an ISO that fills the disc exactly' {
        $exact = 360000 * 2048
        (Test-IsoFitsMedia -IsoBytes $exact -FreeSectors 360000).Fits | Should -BeTrue
        (Test-IsoFitsMedia -IsoBytes ($exact + 1) -FreeSectors 360000).Fits | Should -BeFalse
    }
}

Describe 'Checking a burned disc against what was built' -Tag 'Unit' {

    # Folders stand in for the disc here. The comparison does not care which
    # drive a path is on, which is the point: it is the same check whether it
    # reads D:\ or a staging folder.

    BeforeAll {
        function New-Tree([string]$name, [hashtable]$files) {
            $root = Join-Path $script:Sandbox $name
            foreach ($rel in $files.Keys) {
                $full = Join-Path $root $rel
                New-Item -ItemType Directory -Force -Path (Split-Path $full -Parent) | Out-Null
                Set-Content -LiteralPath $full -Value $files[$rel] -NoNewline
            }
            return $root
        }
        $script:Built = @{
            'autorun.inf'       = '[autorun]'
            'menu.hta'          = '<html>menu</html>'
            'games\game1\a.bin' = 'aaaa'
            'games\game2\b.bin' = 'bbbb'
        }
    }

    It 'passes when the disc holds exactly what was built' {
        $a = New-Tree 'ok-src' $script:Built
        $b = New-Tree 'ok-disc' $script:Built
        $r = Test-BurnedDisc -DiscRoot $b -StagingFolder $a
        $r.Ok | Should -BeTrue
        $r.FilesOnDisc | Should -Be 4
        $r.FilesExpected | Should -Be 4
    }

    It 'names a file that did not make it onto the disc' {
        $a = New-Tree 'miss-src' $script:Built
        $short = $script:Built.Clone(); $short.Remove('menu.hta')
        $b = New-Tree 'miss-disc' $short
        $r = Test-BurnedDisc -DiscRoot $b -StagingFolder $a
        $r.Ok | Should -BeFalse
        $r.Missing | Should -Contain 'menu.hta'
    }

    It 'names a file on the disc that was never built' {
        $a = New-Tree 'extra-src' $script:Built
        $more = $script:Built.Clone(); $more['stray.txt'] = 'x'
        $b = New-Tree 'extra-disc' $more
        $r = Test-BurnedDisc -DiscRoot $b -StagingFolder $a
        $r.Ok | Should -BeFalse
        $r.Unexpected | Should -Contain 'stray.txt'
    }

    It 'catches a file that is the right length and the wrong bytes' {
        # The one failure a size check cannot see, and the only reason to hash
        # every file rather than trust that the burn reported success.
        $a = New-Tree 'hash-src' $script:Built
        $bad = $script:Built.Clone(); $bad['games\game1\a.bin'] = 'aaab'
        $b = New-Tree 'hash-disc' $bad
        $r = Test-BurnedDisc -DiscRoot $b -StagingFolder $a
        $r.Ok | Should -BeFalse
        $r.WrongContent | Should -Contain 'games\game1\a.bin'
        $r.WrongSize | Should -BeNullOrEmpty -Because 'the lengths match, which is the trap'
    }

    It 'catches a truncated file by its length, without hashing it' {
        $a = New-Tree 'size-src' $script:Built
        $cut = $script:Built.Clone(); $cut['games\game2\b.bin'] = 'bb'
        $b = New-Tree 'size-disc' $cut
        $r = Test-BurnedDisc -DiscRoot $b -StagingFolder $a
        $r.Ok | Should -BeFalse
        $r.WrongSize.Count | Should -Be 1
        $r.WrongContent | Should -BeNullOrEmpty
    }

    It 'can skip the hashing, for a disc too big to read twice in a hurry' {
        $a = New-Tree 'skip-src' $script:Built
        $bad = $script:Built.Clone(); $bad['games\game1\a.bin'] = 'aaab'
        $b = New-Tree 'skip-disc' $bad
        $r = Test-BurnedDisc -DiscRoot $b -StagingFolder $a -SkipHashes
        $r.WrongContent | Should -BeNullOrEmpty -Because 'nothing was hashed'
        $r.Ok | Should -BeTrue -Because 'this is exactly what -SkipHashes gives up'
    }

    It 'says what is missing rather than failing obscurely' {
        { Test-BurnedDisc -DiscRoot 'Z:\no\disc' -StagingFolder $script:Sandbox } |
            Should -Throw -ExpectedMessage '*Nothing at*'
    }
}

Describe 'Refusing to write rather than guessing' -Tag 'Unit' {

    It 'will not burn an ISO that is not there' {
        { Write-IsoToDisc -IsoPath 'Z:\nothing.iso' -WhatIf } |
            Should -Throw -ExpectedMessage "*No ISO at*"
    }

    It 'will not burn an empty file' {
        $empty = Join-Path $script:Sandbox 'empty.iso'
        New-Item -ItemType File -Path $empty -Force | Out-Null
        { Write-IsoToDisc -IsoPath $empty -WhatIf } | Should -Throw -ExpectedMessage '*is empty*'
    }

    # Everything past this point needs a drive, and these run on machines that
    # have none, so they stop here rather than pretending. The checks that need
    # real hardware are listed in burn\README.md and done by hand, once, with a
    # disc that is meant to be spent.
}

Describe 'The Burn to disc button' -Tag 'Unit' {

    BeforeAll {
        $script:AppSrc = Get-Content -Raw -LiteralPath (Join-Path (Split-Path $PSScriptRoot -Parent) 'DiscWright.ps1')
        $script:Handler = [regex]::Match($script:AppSrc,
            '(?s)\$btnBurn\.Add_Click\(\{.*?\n\}\)').Value
        $script:Handler | Should -Not -BeNullOrEmpty
    }

    It 'asks before writing, because a write-once disc cannot be taken back' {
        $script:Handler | Should -Match 'MessageBox\]::Show'
        $script:Handler | Should -Match 'OKCancel'
        $script:Handler | Should -Match 'cannot be undone'
        # And it stops on anything but OK, rather than treating a closed dialog
        # as consent.
        $script:Handler | Should -Match '-ne \[System\.Windows\.Forms\.DialogResult\]::OK'
    }

    It 'names the file, the drive and the disc in the question' {
        foreach ($thing in '\$\(\$iso\.Name\)', '\$\(\$d\.Drive\)', '\$\(\$d\.MediaName\)') {
            $script:Handler | Should -Match $thing
        }
    }

    It 'refuses rather than starting a burn that cannot finish' {
        $script:Handler | Should -Match 'Test-IsoFitsMedia'
        $script:Handler | Should -Match 'Nothing was written'
    }

    It 'will not write to a drive that is not ready, and says why not' {
        $script:Handler | Should -Match '\$_\.Ready'
        $script:Handler | Should -Match '\$_\.Why'
    }

    It 'burns below the drive top speed, which is what cheap media wants' {
        # 16x on a CD and 8x on a DVD. The minute saved at 24x is not worth a
        # disc, and this is the whole reason the speed list is read at all.
        $script:Handler | Should -Match '\$cap\s*=\s*if \(\$d\.MediaType -in 1,2,3\) \{ 16 \} else \{ 8 \}'
        $script:Handler | Should -Match '\$_\.Multiple -le \$cap'
    }

    It 'checks the disc afterwards, which is the point of burning a test disc' {
        $script:Handler | Should -Match 'Test-BurnedDisc'
        $script:Handler | Should -Match 'Do not rely on this disc'
    }

    It 'names every kind of mismatch rather than just failing' {
        foreach ($kind in 'Missing', 'Unexpected', 'WrongSize', 'WrongContent') {
            $script:Handler | Should -Match "\`$v\.$kind"
        }
    }

    It 'says the burning files are missing rather than failing silently' {
        $script:Handler | Should -Match 'burn.DiscWright\.Burn\.ps1'
        $script:Handler | Should -Match 'are not installed'
    }

    It 'gives the window back whatever happens' {
        $script:Handler | Should -Match 'finally'
        $script:Handler | Should -Match 'Set-FormBusy \$false'
    }

    It 'is shipped by the installer, or the button would be a dead end' {
        $iss = Get-Content -Raw -LiteralPath (Join-Path (Split-Path $PSScriptRoot -Parent) 'packaging\DiscWright.iss')
        $iss | Should -Match ([regex]::Escape('..\burn\DiscWright.Burn.ps1'))
        $iss | Should -Match ([regex]::Escape('..\print\DiscWright.Print.ps1'))
    }

    It 'does not reach back into the app from the modules it loads' {
        # The whole reason print\ and burn\ are separate folders: the app may
        # use them, they may not use the app, so the ISO builder can still be
        # shipped without either.
        foreach ($mod in 'print\DiscWright.Print.ps1', 'burn\DiscWright.Burn.ps1') {
            $text = Get-Content -Raw -LiteralPath (Join-Path (Split-Path $PSScriptRoot -Parent) $mod)
            $text | Should -Not -Match 'Show-Warn'
            $text | Should -Not -Match 'Set-FormBusy'
            $text | Should -Not -Match 'Update-ActionButtons'
            $text | Should -Not -Match '\$state\.'
        }
    }
}

Describe 'Telling somebody how long a burn will take' -Tag 'Unit' {

    # The window is locked and silent for the whole write, because the drive
    # does it in one go and reports nothing until it ends. An expected wait is
    # tolerable; an unexplained one looks like a hang.

    It 'is in the right place against the only real burn there has been' {
        # 241.7 MB to a CD-R took 93 seconds all in, at the drive's own speed.
        $at24 = Get-BurnEstimateSeconds -Bytes 253458944 -SpeedKb 3599 -MediaType 2
        $at24 | Should -BeGreaterThan 93 -Because 'an estimate that runs under looks like a hang'
        $at24 | Should -BeLessThan 150   -Because 'and one that runs far over is not an estimate'
    }

    It 'takes longer at a slower speed, which is the whole point of choosing one' {
        $fast = Get-BurnEstimateSeconds -Bytes 253458944 -SpeedKb 3599 -MediaType 2
        $slow = Get-BurnEstimateSeconds -Bytes 253458944 -SpeedKb 1199 -MediaType 2
        $slow | Should -BeGreaterThan $fast
    }

    It 'still allows for finalising when there is almost nothing to write' {
        # Closing a disc writes a lead-in and lead-out whatever is on it, which
        # is why a tiny disc is not instant.
        Get-BurnEstimateSeconds -Bytes 1MB -SpeedKb 3599 -MediaType 2 | Should -BeGreaterThan 20
    }

    It 'says almost nothing is needed when the disc is left open' {
        $closed = Get-BurnEstimateSeconds -Bytes 1MB -SpeedKb 3599 -MediaType 2 -CloseMedia $true
        $open   = Get-BurnEstimateSeconds -Bytes 1MB -SpeedKb 3599 -MediaType 2 -CloseMedia $false
        $open | Should -BeLessThan $closed
    }

    It 'gives up rather than inventing a number when the speed is unknown' {
        Get-BurnEstimateSeconds -Bytes 1GB -SpeedKb 0 | Should -Be 0
        Format-BurnEstimate 0 | Should -Be 'unknown'
    }

    It 'reads it out as <Text> for <Seconds> seconds' -ForEach @(
        @{ Seconds = 45;  Text = 'about 45 seconds' }
        @{ Seconds = 100; Text = 'about 2 minutes' }
        @{ Seconds = 60;  Text = 'about 60 seconds' }
        @{ Seconds = 420; Text = 'about 7 minutes' }
    ) {
        Format-BurnEstimate $Seconds | Should -Be $Text
    }

    It 'reports progress while a disc is read back, since that loop is ours' {
        $a = Join-Path $script:Sandbox 'prog-src'
        $b = Join-Path $script:Sandbox 'prog-disc'
        foreach ($root in $a, $b) {
            New-Item -ItemType Directory -Force -Path $root | Out-Null
            foreach ($n in 1..5) { Set-Content -LiteralPath (Join-Path $root "f$n.bin") -Value "file $n" }
        }
        # An ArrayList, not @() with +=: the callback runs in a child scope, so
        # += there makes a local copy and the outer variable stays empty. The
        # app's own callback sets control properties, which does not have this
        # problem, but a test that collects values does.
        $seen = New-Object System.Collections.ArrayList
        $null = Test-BurnedDisc -DiscRoot $b -StagingFolder $a -OnProgress {
            param($done, $total, $file) $null = $seen.Add($done)
        }
        # Five files, so five reports, ending at five.
        $seen.Count | Should -Be 5
        $seen[-1] | Should -Be 5
    }
}

Describe 'Telling the truth about the disc in the drive' -Tag 'Unit' {

    # The burn confirmation said "This cannot be undone and the disc is not
    # rewritable" to everybody, whatever was in the drive. Found with a CD-RW
    # actually in the drive, which is the one case where it is flatly wrong, and
    # it is the sentence somebody reads immediately before committing a disc.
    #
    # Being wrong about the thing in their hand is how a dialog loses the
    # benefit of the doubt for everything else it says.

    It 'knows <Name> can be erased and written again' -ForEach @(
        @{ Code = 3;  Name = 'CD-RW' }
        @{ Code = 5;  Name = 'DVD-RAM' }
        @{ Code = 7;  Name = 'DVD+RW' }
        @{ Code = 10; Name = 'DVD-RW' }
        @{ Code = 13; Name = 'DVD+RW DL' }
        @{ Code = 19; Name = 'BD-RE' }
    ) {
        Test-MediaRewritable $Code | Should -BeTrue
    }

    It 'knows <Name> is written once and never again' -ForEach @(
        @{ Code = 2;  Name = 'CD-R' }
        @{ Code = 6;  Name = 'DVD+R' }
        @{ Code = 9;  Name = 'DVD-R' }
        @{ Code = 11; Name = 'DVD-R DL' }
        @{ Code = 18; Name = 'BD-R' }
    ) {
        Test-MediaRewritable $Code | Should -BeFalse
    }

    It 'does not call a pressed disc rewritable' {
        # It cannot be written at all, so it certainly cannot be rewritten.
        Test-MediaRewritable 1  | Should -BeFalse
        Test-MediaRewritable 17 | Should -BeFalse
    }

    It 'says nothing about a code it does not know' {
        Test-MediaRewritable 250 | Should -BeFalse
    }

    It 'every writable medium says one thing or the other, and never both' {
        # A new media type added to the table without a Rewritable flag would
        # quietly read as "not rewritable", which is how the original bug would
        # come back one disc type at a time.
        $src = Get-Content (Join-Path (Split-Path $PSScriptRoot -Parent) 'burn\DiscWright.Burn.ps1') -Raw
        $rows = [regex]::Matches($src, "=\s*@\{ Name = '[^']+';\s*Writable = \`$(true|false);\s*Rewritable = \`$(true|false)")
        $rows.Count | Should -BeGreaterThan 15 -Because 'every row in the media table carries both flags'
        foreach ($m in $rows) {
            if ($m.Groups[1].Value -eq 'false') {
                $m.Groups[2].Value | Should -Be 'false' -Because 'a disc that cannot be written cannot be rewritten'
            }
        }
    }

    It 'picks the sentence from the media, not from a constant' {
        $src = Get-Content (Join-Path (Split-Path $PSScriptRoot -Parent) 'burn\DiscWright.Burn.ps1') -Raw
        $src | Should -Match 'if \(Test-MediaRewritable \$target\.MediaType\)'
        $src | Should -Match 'The disc is rewritable, so it can be erased and written again'
        $src | Should -Match 'This cannot be undone: the disc is not rewritable'
        $src | Should -Not -Match 'This cannot be undone and the disc is not rewritable'
    }
}

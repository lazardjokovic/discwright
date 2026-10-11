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

    # 10 October 2026, on a real disc. A CD-RW was burned twice, once by the
    # Windows burner and once by this one, and both times the 728 MB installer
    # piece came back "Data error (cyclic redundancy check)". The check did not
    # report that: Get-FileHash wrote an error and returned nothing, .Hash on
    # nothing threw, and the run ended on a stack trace partway down the disc,
    # having named no file and said nothing about the rest.
    #
    # A lock stands in for the bad sector. What matters is the shape, a read
    # that throws, and not the reason it threw: a CRC error cannot be arranged
    # on demand and a test that needed one would never run.
    Context 'a disc that will not give a file back' {

        BeforeEach {
            $script:LockHandle = $null
        }
        AfterEach {
            if ($script:LockHandle) { $script:LockHandle.Dispose(); $script:LockHandle = $null }
        }

        It 'names the file instead of dying on it' {
            $a = New-Tree 'unread-src' $script:Built
            $b = New-Tree 'unread-disc' $script:Built
            $script:LockHandle = [IO.File]::Open((Join-Path $b 'games\game1\a.bin'), 'Open', 'Read', 'None')
            $r = Test-BurnedDisc -DiscRoot $b -StagingFolder $a
            $r.Ok | Should -BeFalse
            $r.Unreadable.Count | Should -Be 1
            $r.Unreadable[0] | Should -BeLike 'games\game1\a.bin*'
        }

        It 'keeps it apart from a file that merely came out wrong' {
            # Different facts. One file came back as the wrong bytes, the other
            # did not come back at all, and only one of those can be looked at.
            $a = New-Tree 'unread-apart-src' $script:Built
            $b = New-Tree 'unread-apart-disc' $script:Built
            $script:LockHandle = [IO.File]::Open((Join-Path $b 'games\game1\a.bin'), 'Open', 'Read', 'None')
            $r = Test-BurnedDisc -DiscRoot $b -StagingFolder $a
            $r.WrongContent | Should -BeNullOrEmpty -Because 'nothing came back to compare against'
        }

        It 'checks the rest of the disc afterwards' {
            # The whole reason the old behaviour was unacceptable: it stopped at
            # the first unreadable file, so a disc could be wrong in three more
            # places and nobody would ever hear about them.
            $a = New-Tree 'unread-rest-src' $script:Built
            $bad = $script:Built.Clone(); $bad['games\game2\b.bin'] = 'bbbz'
            $b = New-Tree 'unread-rest-disc' $bad
            $script:LockHandle = [IO.File]::Open((Join-Path $b 'games\game1\a.bin'), 'Open', 'Read', 'None')
            $r = Test-BurnedDisc -DiscRoot $b -StagingFolder $a
            $r.Unreadable.Count | Should -Be 1
            $r.WrongContent | Should -Contain 'games\game2\b.bin'
        }

        It 'says nothing about readability when it was told not to hash' {
            $a = New-Tree 'unread-skip-src' $script:Built
            $b = New-Tree 'unread-skip-disc' $script:Built
            $script:LockHandle = [IO.File]::Open((Join-Path $b 'games\game1\a.bin'), 'Open', 'Read', 'None')
            $r = Test-BurnedDisc -DiscRoot $b -StagingFolder $a -SkipHashes
            $r.Unreadable | Should -BeNullOrEmpty -Because 'nothing was read, so nothing failed to read'
            $r.Ok | Should -BeTrue
        }
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
        # The readiness rule itself moved into Get-BurnTarget, so that this
        # button and the set walk stop deciding it with the same line copied
        # out twice. What the button still owes is asking, and showing the
        # reason it is handed rather than a sentence of its own.
        $script:Handler | Should -Match 'Get-BurnTarget'
        $script:Handler | Should -Match '\$pick\.Why'
        $script:Handler | Should -Match 'Put a blank disc in and try again'
    }

    It 'asks which writer to use when more than one of them is ready' {
        $script:Handler | Should -Match '\$pick\.Ask'
        $script:Handler | Should -Match 'Select-BurnDrive'
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
        foreach ($kind in 'Missing', 'Unexpected', 'WrongSize', 'WrongContent', 'Unreadable') {
            $script:Handler | Should -Match "\`$v\.$kind"
        }
    }

    It 'puts a file that could not be read above the rest, because it is worse' {
        # A wrong file is a file you can look at. An unreadable one is not
        # there at all, and on a set it is the disc the next one joins onto.
        $script:Handler | Should -Match 'could not be read off the disc'
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

    # Still worth knowing before a burn starts, even now that the write reports
    # as it goes: the estimate is what the confirmation dialog shows, before
    # anybody has committed a disc. An expected wait is tolerable; an
    # unexplained one looks like a hang.

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
        # Five files and at least five reports, ending at five. Not exactly
        # five any more: a long file now reports as it is being read rather
        # than only once it is finished, so the count is no longer the number
        # of files and must not be asserted as though it were. What has to
        # hold is that every file is accounted for and the last report says
        # all of them.
        $seen.Count | Should -BeGreaterOrEqual 5
        $seen[-1] | Should -Be 5
        @($seen | Sort-Object -Unique) | Should -Be @(1, 2, 3, 4, 5)
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

Describe 'Which program writes the discs of a set' -Tag 'Unit' {

    # Ticking "disc set" used to offer the three programs Windows knows about
    # and not DiscWright, so a set never got the speed this burner picks or the
    # check it runs afterwards. On 10 October that cost a disc and an evening.

    BeforeAll {
        $script:Fake = @(
            @{ Name = 'Windows Disc Image Burning Tool'; What = 'writes it to a blank disc'; Exe = 'C:\isoburn.exe'; Args = '"%1"'; Verb = '' }
            @{ Name = 'Nero Burning ROM'; What = 'opens it, and burns it its own way'; Exe = 'C:\nero.exe'; Args = '"%1"'; Verb = '' }
            @{ Name = 'Windows Explorer'; What = 'mounts it as a drive, without burning anything'; Exe = ''; Args = ''; Verb = 'mount' }
        )
    }

    It 'puts DiscWright first' {
        Mock Get-IsoHandoffs { $script:Fake }
        $c = @(Get-SetBurnChoices)
        $c[0].Name | Should -Be 'DiscWright'
        $c[0].Own | Should -BeTrue
    }

    It 'says what it does that the others do not' {
        Mock Get-IsoHandoffs { $script:Fake }
        $c = @(Get-SetBurnChoices)
        $c[0].What | Should -Match 'below the drive top speed'
        $c[0].What | Should -Match 'checks every file'
    }

    It 'leaves out the one that burns nothing' {
        # "Mounts it as a drive, without burning anything" is an honest label
        # and a fine thing to offer somebody looking inside an ISO. It is not
        # an answer to "which program should write these discs", and a walk
        # that took it would march on to disc 2 having burned nothing at all.
        Mock Get-IsoHandoffs { $script:Fake }
        $names = @(Get-SetBurnChoices) | ForEach-Object { $_.Name }
        $names | Should -Not -Contain 'Windows Explorer'
    }

    It 'keeps the other burners, because this is a choice and not a redirection' {
        Mock Get-IsoHandoffs { $script:Fake }
        $names = @(Get-SetBurnChoices) | ForEach-Object { $_.Name }
        $names | Should -Contain 'Windows Disc Image Burning Tool'
        $names | Should -Contain 'Nero Burning ROM'
        @(Get-SetBurnChoices).Count | Should -Be 3
    }

    It 'offers itself even on a machine where nothing else is registered' {
        # The old list could come back empty and the walk would say nothing on
        # this machine opens an ISO. DiscWright opens its own ISOs.
        Mock Get-IsoHandoffs { @() }
        $c = @(Get-SetBurnChoices)
        $c.Count | Should -Be 1
        $c[0].Name | Should -Be 'DiscWright'
    }

    It 'marks only itself as its own' {
        Mock Get-IsoHandoffs { $script:Fake }
        @(@(Get-SetBurnChoices) | Where-Object { $_.Own }).Count | Should -Be 1
    }
}

Describe 'Watching a burn as it happens' -Tag 'Unit' {

    # The write itself cannot be tested without spending a disc. Everything
    # around it can: that the sink is declared the way the engine expects, that
    # the numbers it reads turn into the right percentage and the right words,
    # and that nothing it does is able to take a burn down with it.
    #
    # The two magic numbers are checked against imapi2.dll's own typelib rather
    # than written down twice, because the first pair this was built with were
    # both wrong, and a sink declaring the wrong dispid is advised perfectly
    # happily and then never called. That failure is indistinguishable from a
    # drive that does not report progress, which is the worst kind: it looks
    # like the hardware's fault.

    BeforeAll {
        Add-BurnProgressType

        if (-not ('DwTlb' -as [type])) {
            Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using CT = System.Runtime.InteropServices.ComTypes;

public static class DwTlb
{
    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void LoadTypeLibEx(string file, int regkind, out ITypeLib tlb);

    // "{iid}|dispid" for a named dispinterface, straight out of the typelib.
    public static string Describe(string file, string wantName, string member)
    {
        ITypeLib tlb;
        LoadTypeLibEx(file, 0, out tlb);
        int n = tlb.GetTypeInfoCount();
        for (int i = 0; i < n; i++)
        {
            string nm, doc, help; int ctx;
            tlb.GetDocumentation(i, out nm, out doc, out ctx, out help);
            if (nm != wantName) { continue; }
            ITypeInfo ti;
            tlb.GetTypeInfo(i, out ti);
            IntPtr pAttr;
            ti.GetTypeAttr(out pAttr);
            CT.TYPEATTR ta = (CT.TYPEATTR)Marshal.PtrToStructure(pAttr, typeof(CT.TYPEATTR));
            Guid iid = ta.guid;
            ti.ReleaseTypeAttr(pAttr);
            int[] ids = new int[1];
            ti.GetIDsOfNames(new string[] { member }, 1, ids);
            return iid.ToString("B").ToUpperInvariant() + "|" + ids[0];
        }
        return "";
    }
}
'@
        }

        # Stands in for the event argument the engine passes. A real CLR type
        # rather than a pscustomobject, because the sink reads properties by
        # late binding and a PowerShell object does not answer that way.
        if (-not ('DwFakeBurnArgs' -as [type])) {
            Add-Type -TypeDefinition @'
public class DwFakeBurnArgs {
    public int CurrentAction { get; set; }
    public int StartLba { get; set; }
    public int LastWrittenLba { get; set; }
    public int SectorCount { get; set; }
    public int ElapsedTime { get; set; }
    public int RemainingTime { get; set; }
    public int TotalTime { get; set; }
}
// A drive from the future, reporting something this build has never heard of.
public class DwUnknownBurnArgs { public int Nonsense { get; set; } }

// Calls the sink from a thread that has never heard of PowerShell, which is
// what the burn engine does. Reflection rather than a typed call, because the
// sink lives in an assembly Add-Type built a moment ago.
public static class DwOffThread {
    public static string Call(object sink, object sender, object args) {
        string err = null;
        System.Threading.Thread t = new System.Threading.Thread(delegate() {
            try { sink.GetType().GetMethod("Update").Invoke(sink, new object[] { sender, args }); }
            catch (System.Exception ex) { err = ex.ToString(); }
        });
        t.IsBackground = true;
        t.Start();
        t.Join();
        return err;
    }
}
'@
        }

        $script:Imapi2Dll = Join-Path $env:SystemRoot 'System32\imapi2.dll'
        $script:TlbFacts = [DwTlb]::Describe($script:Imapi2Dll, 'DDiscFormat2DataEvents', 'Update')

        function New-FakeArgs([int]$action, [int]$start, [int]$last, [int]$count,
                              [int]$elapsed = -1, [int]$remaining = -1, [int]$total = -1) {
            $a = New-Object DwFakeBurnArgs
            $a.CurrentAction = $action; $a.StartLba = $start; $a.LastWrittenLba = $last
            $a.SectorCount = $count
            $a.ElapsedTime = $elapsed; $a.RemainingTime = $remaining; $a.TotalTime = $total
            return $a
        }
    }

    Context 'declared the way the engine expects' {

        It 'reads the facts off the typelib at all, or the next two tests prove nothing' {
            $script:TlbFacts | Should -Not -BeNullOrEmpty -Because 'imapi2.dll ships with Windows and carries its own typelib'
        }

        It 'declares the interface id imapi2.dll names for burn events' {
            $iid = $script:TlbFacts.Split('|')[0]
            ('{' + [DiscWright.IBurnEvents].GUID.ToString().ToUpperInvariant() + '}') | Should -Be $iid
        }

        It 'declares the dispid the engine calls Update on' {
            # 0x200. Written as 0x100 first, which advises and never fires.
            $want = [int]$script:TlbFacts.Split('|')[1]
            $attr = [DiscWright.IBurnEvents].GetMethod('Update').GetCustomAttributes(
                        [System.Runtime.InteropServices.DispIdAttribute], $false)
            @($attr).Count | Should -Be 1 -Because 'without one the CCW numbers the method itself'
            $attr[0].Value | Should -Be $want
        }

        It 'is taken as a listener by the real burn engine' {
            # No disc and no drive needed: the connection point lives on the
            # format object, which exists on any Windows.
            $fmt = New-Object -ComObject 'IMAPI2.MsftDiscFormat2Data'
            try {
                $hook = Register-BurnProgress -Format $fmt -Sink (New-BurnProgressSink)
                $hook.Ok | Should -BeTrue -Because "it said: $($hook.Why)"
                $hook.Detach()
                $hook.Ok | Should -BeFalse -Because 'a detached hook is not still listening'
            } finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($fmt) }
        }

        It 'says why rather than throwing when it is given something that has no events' {
            $hook = Register-BurnProgress -Format (New-Object PSObject) -Sink (New-BurnProgressSink)
            $hook.Ok | Should -BeFalse
            $hook.Why | Should -Not -BeNullOrEmpty -Because 'a burn still has to run, and the log has to say why there is no bar'
        }
    }

    Context 'the numbers the drive sends' {

        It 'reads a report into the sink' {
            $hook = Register-BurnProgress -Format (New-Object PSObject) -Sink (New-BurnProgressSink)
            $hook.Sink.Update($null, (New-FakeArgs 4 0 178022 356045 134 160 294))
            $hook.Sink.ActionCode       | Should -Be 4
            $hook.Sink.SectorsDone      | Should -Be 178022
            $hook.Sink.SectorsTotal     | Should -Be 356045
            $hook.Sink.ElapsedSeconds   | Should -Be 134
            $hook.Sink.RemainingSeconds | Should -Be 160
            $hook.Sink.Updates          | Should -Be 1
            $hook.Sink.LastError        | Should -BeNullOrEmpty
        }

        It 'counts from where this write began, not from the start of the disc' {
            # An append starts partway in. Counting from sector nought would
            # report a disc most of the way through before anything was written.
            $hook = Register-BurnProgress -Format (New-Object PSObject) -Sink (New-BurnProgressSink)
            $hook.Sink.Update($null, (New-FakeArgs 4 100000 150000 100000))
            $hook.Sink.SectorsDone | Should -Be 50000
            (ConvertTo-BurnProgress $hook.Sink).Percent | Should -Be 50
        }

        It 'turns sectors into a percentage' {
            $hook = Register-BurnProgress -Format (New-Object PSObject) -Sink (New-BurnProgressSink)
            $hook.Sink.Update($null, (New-FakeArgs 4 0 89011 356045))
            (ConvertTo-BurnProgress $hook.Sink).Percent | Should -Be 25
        }

        It 'never reports more than all of it, whatever the drive claims' {
            $hook = Register-BurnProgress -Format (New-Object PSObject) -Sink (New-BurnProgressSink)
            $hook.Sink.Update($null, (New-FakeArgs 5 0 400000 356045))
            $hook.Sink.SectorsDone | Should -Be 356045
            (ConvertTo-BurnProgress $hook.Sink).Percent | Should -Be 100
        }

        It 'says "not saying" rather than nought when there is no total to divide by' {
            # The difference matters on screen: nought per cent is a bar at the
            # far left, which reads as a burn that has not started.
            $hook = Register-BurnProgress -Format (New-Object PSObject) -Sink (New-BurnProgressSink)
            $hook.Sink.Update($null, (New-FakeArgs 2 0 0 0))
            (ConvertTo-BurnProgress $hook.Sink).Percent | Should -Be -1
        }

        It 'turns sectors into megabytes at 2048 bytes apiece' {
            $hook = Register-BurnProgress -Format (New-Object PSObject) -Sink (New-BurnProgressSink)
            $hook.Sink.Update($null, (New-FakeArgs 4 0 178022 356045))
            $d = (ConvertTo-BurnProgress $hook.Sink).Detail
            $d.BytesDone  | Should -Be (178022 * 2048)
            $d.BytesTotal | Should -Be (356045 * 2048)
        }

        It 'survives a report with none of the properties it wanted' {
            $hook = Register-BurnProgress -Format (New-Object PSObject) -Sink (New-BurnProgressSink)
            { $hook.Sink.Update($null, (New-Object DwUnknownBurnArgs)) } | Should -Not -Throw
            $hook.Sink.ActionCode | Should -Be -1
            (ConvertTo-BurnProgress $hook.Sink).Phase | Should -Be 'step -1' -Because 'a phase it cannot name must not be reported as writing'
        }

        It 'survives being handed nothing at all' {
            $hook = Register-BurnProgress -Format (New-Object PSObject) -Sink (New-BurnProgressSink)
            { $hook.Sink.Update($null, $null) } | Should -Not -Throw
        }
    }

    Context 'progress that goes wrong while a disc is being written' {

        It 'keeps the latest report where the window can read it' {
            $hook = Register-BurnProgress -Format (New-Object PSObject) -Sink (New-BurnProgressSink)
            foreach ($lba in 1000, 2000, 3000) {
                $hook.Sink.Update($null, (New-FakeArgs 4 0 $lba 4000))
            }
            $hook.Sink.Updates | Should -Be 3
            $hook.Sink.Snapshot().SectorsDone | Should -Be 3000
        }

        It 'takes a report on a thread PowerShell could not run on' {
            # The bug this whole arrangement exists for, found on 11 October by
            # burning a disc and watching the bar sit at nought the whole way.
            #
            # The sink used to hand every report to a scriptblock. The engine
            # does not report on the thread that called Write: it reports from
            # one of its own, where there is no runspace and a scriptblock
            # cannot run at all. Every report died on "There is no Runspace
            # available to run scripts in this thread", the catch below
            # swallowed it so the disc would not suffer for a bar, and the
            # window showed nought per cent for ten minutes while the burn went
            # perfectly well.
            #
            # So this calls the sink the way the engine does: from a thread that
            # has never heard of PowerShell. The numbers still have to arrive.
            $sink = New-BurnProgressSink
            $err = [DwOffThread]::Call($sink, $null, (New-FakeArgs 4 0 3000 4000 10 20 30))
            $err | Should -BeNullOrEmpty -Because 'an exception here goes back into the engine mid-write'
            $sink.Updates     | Should -Be 1
            $sink.SectorsDone | Should -Be 3000
            $sink.LastError   | Should -BeNullOrEmpty
        }

        It 'records that the report came from somewhere else, for the log to say' {
            $sink = New-BurnProgressSink
            $null = [DwOffThread]::Call($sink, $null, (New-FakeArgs 4 0 10 100))
            $sink.OffThreadCalls | Should -Be 1
            $sink.LastCallThreadId | Should -Not -Be $sink.OwnerThreadId
        }

        It 'offers the engine no way to call back into PowerShell' {
            # A delegate or scriptblock on this object is the whole bug: the
            # only thread it could ever be invoked on is the one place
            # PowerShell cannot run. The sink records and the window reads.
            $t = [DiscWright.BurnProgressSink]
            $callable = @($t.GetFields() | Where-Object {
                              $_.FieldType.Name -like 'Action*' -or $_.FieldType.Name -like 'Func*' -or
                              $_.FieldType.Name -eq 'ScriptBlock' -or $_.FieldType.Name -eq 'Delegate' })
            $callable | Should -BeNullOrEmpty -Because 'this is the shape of the bug that shipped'
            $t.GetProperty('Report') | Should -BeNullOrEmpty
        }

        It 'puts whether it attached onto the sink, where the log can reach it' {
            # The thread that watches never holds the hook. It holds the sink.
            $sink = New-BurnProgressSink
            $hook = Register-BurnProgress -Format (New-Object PSObject) -Sink $sink
            $sink.Attached  | Should -BeFalse
            $sink.AttachWhy | Should -Be $hook.Why
            $sink.AttachWhy | Should -Not -BeNullOrEmpty
        }
    }

    Context 'the write runs somewhere the window is not' {

        It 'runs the script on a thread that is not this one' {
            $here = [System.Threading.Thread]::CurrentThread.ManagedThreadId
            $job = Start-BurnRunspace { [System.Threading.Thread]::CurrentThread.ManagedThreadId } @{}
            try { $there = @(Wait-BurnRunspace $job)[0] } finally { Close-BurnRunspace $job }
            $there | Should -Not -Be $here -Because 'a write on this thread is a window that cannot paint'
        }

        It 'runs it in an apartment IMAPI will talk to' {
            # COM, and a COM object made on one thread and used from another is
            # a worse problem than the one this solves.
            $job = Start-BurnRunspace { [System.Threading.Thread]::CurrentThread.GetApartmentState().ToString() } @{}
            try { @(Wait-BurnRunspace $job)[0] | Should -Be 'STA' } finally { Close-BurnRunspace $job }
        }

        It 'hands it the variables it was given, and nothing else at all' {
            $job = Start-BurnRunspace {
                       $leaked = $(if (Get-Command Get-BurnPhaseName -ErrorAction SilentlyContinue)
                                   { 'functions leaked' } else { 'clean' })
                       "$Given|$leaked"
                   } @{ Given = 'a value' }
            try { @(Wait-BurnRunspace $job)[0] | Should -Be 'a value|clean' }
            finally { Close-BurnRunspace $job }
        }

        It 'throws on this thread whatever the other one threw' {
            # A write that failed over there is still a failed write, and the
            # dialog that has to report it runs here. No wildcards: EndInvoke
            # wraps the real message inside a sentence about calling EndInvoke,
            # and that sentence was what the failure dialog ended up showing.
            $job = Start-BurnRunspace { throw 'the drive said no' } @{}
            try { { Wait-BurnRunspace $job } | Should -Throw -ExpectedMessage 'the drive said no' }
            finally { Close-BurnRunspace $job }
        }

        It 'repeats a warning from over there rather than losing it' {
            # The speed a drive refuses is a warning, and it is raised on a
            # thread nobody is listening to.
            $job = Start-BurnRunspace { Write-Warning 'the drive would not take 8x' } @{}
            try {
                $null = Wait-BurnRunspace $job -WarningVariable w -WarningAction SilentlyContinue
                ($w -join '|') | Should -Match 'would not take 8x'
            } finally { Close-BurnRunspace $job }
        }

        It 'attaches the listener over there and says so back here' {
            # Production's own path, and the one no other test covers: the type
            # crossing into the runspace, the format object being made on that
            # thread so the engine never has to reach across an apartment to
            # report, and the answer coming back on the sink because the
            # watching thread never holds the hook.
            $sink = New-BurnProgressSink
            $job = Start-BurnRunspace {
                      $fmt = New-Object -ComObject 'IMAPI2.MsftDiscFormat2Data'
                      $hook = $HookType::Attach($fmt, $Sink)
                      $hook.Ok
                      $hook.Detach()
                  } @{ HookType = [DiscWright.BurnProgressHook]; Sink = $sink }
            try { @(Wait-BurnRunspace $job)[0] | Should -BeTrue }
            finally { Close-BurnRunspace $job }
            $sink.Attached | Should -BeTrue
        }

        It 'can be closed twice' {
            $job = Start-BurnRunspace { 1 } @{}
            $null = Wait-BurnRunspace $job
            Close-BurnRunspace $job
            { Close-BurnRunspace $job } | Should -Not -Throw -Because 'it is called from a finally that may run after a throw'
        }
    }

    Context 'watching from the thread that owns the window' {

        It 'reports once per turn until the write is over' {
            $sink = New-BurnProgressSink
            $sink.Update($null, (New-FakeArgs 4 0 500 1000 10 10 20))
            $script:left = 3
            $script:seen = @()
            $n = Watch-BurnProgress -Sink $sink -IntervalMs 1 `
                     -Running { $script:left--; $script:left -ge 0 } `
                     -OnProgress { param($pct, $phase, $detail) $script:seen += $pct }
            $n | Should -Be 3
            $script:seen | Should -Be @(50, 50, 50)
        }

        It 'reports nothing at all when the write is already over' {
            $sink = New-BurnProgressSink
            $script:seen = @()
            $n = Watch-BurnProgress -Sink $sink -IntervalMs 1 -Running { $false } `
                     -OnProgress { param($pct, $phase, $detail) $script:seen += $pct }
            $n | Should -Be 0
            $script:seen.Count | Should -Be 0
        }

        It 'is content with no handler at all' {
            $sink = New-BurnProgressSink
            $script:left = 2
            { Watch-BurnProgress -Sink $sink -IntervalMs 1 -Running { $script:left--; $script:left -ge 0 } } |
                Should -Not -Throw
        }

        It 'counts the seconds itself while the drive says nothing' {
            # Some drives report nothing for the whole write. The window still
            # has to look alive, so the elapsed time is ours and only the
            # estimate of what is left is the drive's.
            $sink = New-BurnProgressSink
            $script:left = 1
            $script:detail = $null
            $null = Watch-BurnProgress -Sink $sink -IntervalMs 1 -Started (Get-Date).AddSeconds(-95) `
                        -Running { $script:left--; $script:left -ge 0 } `
                        -OnProgress { param($pct, $phase, $detail) $script:detail = $detail }
            $script:detail.ElapsedSeconds   | Should -BeGreaterOrEqual 95
            $script:detail.RemainingSeconds | Should -Be -1 -Because 'only the drive can say how much longer'
        }

        It 'says the drive is being got ready rather than naming a phase it has not been told' {
            # Before the first report there is no phase. "step -1" is what that
            # came out as, which is worse on screen than saying nothing.
            $sink = New-BurnProgressSink
            $p = ConvertTo-BurnProgress $sink
            $p.Phase   | Should -Be 'getting the drive ready'
            $p.Percent | Should -Be -1
        }

        It 'prefers the clock the drive keeps to the one we keep' {
            $sink = New-BurnProgressSink
            $sink.Update($null, (New-FakeArgs 4 0 500 1000 42 60 102))
            (ConvertTo-BurnProgress $sink 95).Detail.ElapsedSeconds | Should -Be 42
        }
    }

    Context 'what it all says on screen' {

        It 'names phase <Code> as <Words>' -ForEach @(
            @{ Code = 0; Words = 'checking the disc' }
            @{ Code = 1; Words = 'formatting the disc' }
            @{ Code = 2; Words = 'getting the drive ready' }
            @{ Code = 3; Words = 'setting the laser power' }
            @{ Code = 4; Words = 'writing' }
            @{ Code = 5; Words = 'closing the disc' }
            @{ Code = 6; Words = 'finished' }
            @{ Code = 7; Words = 'the drive is reading back what it wrote' }
        ) {
            Get-BurnPhaseName $Code | Should -Be $Words
        }

        It 'reports a phase it has never heard of as itself' {
            Get-BurnPhaseName 11 | Should -Be 'step 11'
        }

        It 'writes <Seconds> seconds on the clock as <Text>' -ForEach @(
            @{ Seconds = 0;    Text = '00:00' }
            @{ Seconds = 9;    Text = '00:09' }
            @{ Seconds = 95;   Text = '01:35' }
            @{ Seconds = 599;  Text = '09:59' }
            @{ Seconds = 3725; Text = '1:02:05' }
        ) {
            Format-BurnClock $Seconds | Should -Be $Text
        }

        It 'floors the clock rather than rounding it, so it never reads ahead of itself' {
            # The same trap Format-Elapsed carries in the app: a cast rounds, so
            # 95 seconds would show as 02:35 on a stopwatch that has not got there.
            Format-BurnClock 95 | Should -Be '01:35'
        }

        It 'says nothing about a clock with nothing on it' {
            Format-BurnClock -1 | Should -Be ''
        }

        It 'puts the elapsed time and the drive own estimate on one line' {
            Format-BurnProgressLine 134 160 | Should -Be 'Elapsed 02:14, about 02:40 left'
        }

        It 'leaves out an estimate the drive has not made' {
            Format-BurnProgressLine 134 -1 | Should -Be 'Elapsed 02:14'
            Format-BurnProgressLine 134 0  | Should -Be 'Elapsed 02:14'
        }

        It 'admits it when the drive says nothing at all' {
            Format-BurnProgressLine -1 -1 | Should -Match 'not saying'
        }
    }
}

Describe 'How far along reading a disc back is' -Tag 'Unit' {

    # The file count was the only thing reported until now, and on a set it is
    # close to useless: eight small files and one 728 MB part file means the bar
    # jumps to 89 per cent and then sits there for four minutes.

    BeforeAll {
        function New-SizedTree([string]$root, [hashtable]$files) {
            New-Item -ItemType Directory -Force -Path $root | Out-Null
            foreach ($name in $files.Keys) {
                $fs = [IO.File]::Create((Join-Path $root $name))
                $fs.SetLength([long]$files[$name])
                $fs.Close()
            }
            return $root
        }
    }

    It 'counts bytes as well as files, and ends on all of both' {
        $sizes = @{ 'small.bin' = 1KB; 'big.bin' = 64KB; 'middling.bin' = 8KB }
        $a = New-SizedTree (Join-Path $script:Sandbox 'bytes-src') $sizes
        $b = New-SizedTree (Join-Path $script:Sandbox 'bytes-disc') $sizes
        $last = New-Object System.Collections.ArrayList
        $null = Test-BurnedDisc -DiscRoot $b -StagingFolder $a -OnProgress {
            param($done, $total, $rel, $bytesDone, $bytesTotal)
            $null = $last.Add(('{0}/{1} {2}/{3}' -f $done, $total, $bytesDone, $bytesTotal))
        }
        # Three files and at least three reports. A file bigger than one read
        # now reports while it is being read, so counting reports stopped
        # being the same as counting files. The end is what this is about.
        $last.Count | Should -BeGreaterOrEqual 3
        $last[-1] | Should -Be ('3/3 {0}/{0}' -f (1KB + 64KB + 8KB))
        @($last | Where-Object { $_ -like '3/3 *' }) | Should -Not -BeNullOrEmpty
    }

    It 'reports the big file as most of the disc, not as one file in three' {
        $sizes = @{ 'a.bin' = 1KB; 'b.bin' = 1KB; 'huge.bin' = 1MB }
        $a = New-SizedTree (Join-Path $script:Sandbox 'weight-src') $sizes
        $b = New-SizedTree (Join-Path $script:Sandbox 'weight-disc') $sizes
        $seen = New-Object System.Collections.ArrayList
        $null = Test-BurnedDisc -DiscRoot $b -StagingFolder $a -OnProgress {
            param($done, $total, $rel, $bytesDone, $bytesTotal)
            if ($rel -eq 'b.bin') {
                $null = $seen.Add([int](100.0 * $bytesDone / $bytesTotal))
            }
        }
        # Two files of three are done, and nothing worth watching has happened.
        $seen[0] | Should -BeLessThan 5 -Because 'by files it would read as 67 per cent'
    }

    It 'still moves for a file that came out wrong' {
        # It did not. The count was raised after the checks, so a wrong file
        # skipped it, and the bar stopped short of the end on exactly the discs
        # anybody would be watching.
        $a = New-SizedTree (Join-Path $script:Sandbox 'short-src') @{ 'one.bin' = 4KB; 'two.bin' = 4KB }
        $b = New-SizedTree (Join-Path $script:Sandbox 'short-disc') @{ 'one.bin' = 4KB; 'two.bin' = 2KB }
        $last = New-Object System.Collections.ArrayList
        $v = Test-BurnedDisc -DiscRoot $b -StagingFolder $a -OnProgress {
            param($done, $total, $rel, $bytesDone, $bytesTotal) $null = $last.Add($done)
        }
        $v.WrongSize.Count | Should -Be 1 -Because 'the fixture is a short file on the disc'
        $last[-1] | Should -Be 2 -Because 'both files were looked at, whatever the verdict on them'
    }
}

Describe 'Choosing which writer to use, on a machine that has two' {

    # Asked for on 11 October, with a Blu-ray writer about to be plugged in to
    # find out whether the old drive is the thing ruining discs. Until then
    # both burn paths made this decision with the same line copied out twice,
    # take the first drive with a usable blank in it, which is right on a
    # machine with one writer and silently wrong on a machine with two.

    BeforeAll {
        function New-Burner([string]$drive, [bool]$ready, [string]$why = 'ready',
                            [string]$product = 'DRW-24D5MT', [string]$media = 'CD-RW') {
            return @{ Drive = $drive; Ready = $ready; Why = $why; Vendor = 'ASUS'; Product = $product
                      MediaName = $media; MediaType = 3; FreeBytes = [double]736966656
                      FreeSectors = 359847; Speeds = @() }
        }
    }

    Context 'when there is nothing to ask about' {

        It 'refuses when the machine has no writer at all' {
            (Get-BurnTarget @()).Why | Should -Match 'No disc recorder'
        }

        It 'refuses the same way when it is handed nothing rather than an empty list' {
            # A PowerShell function returning @() hands back nothing at all,
            # so the caller's variable is $null, and piping $null yields one
            # $null rather than nothing. Without allowing for that, a machine
            # with no recorder reports that no drive is ready, about a drive
            # that does not exist.
            (Get-BurnTarget $null).Why | Should -Match 'No disc recorder'
        }

        It 'takes the only drive without asking anything' {
            $r = Get-BurnTarget @((New-Burner 'D:\' $true))
            $r.Target.Drive | Should -Be 'D:\'
            $r.Ask | Should -BeNullOrEmpty -Because 'being asked to choose between one thing is a dialog in the way'
        }

        It 'takes the only READY drive without asking, even with two writers' {
            # The common case once a second writer is plugged in: a blank in
            # one of them and nothing in the other. There is no question here.
            $r = Get-BurnTarget @((New-Burner 'D:\' $false 'no disc'), (New-Burner 'E:\' $true))
            $r.Target.Drive | Should -Be 'E:\'
            $r.Ask | Should -BeNullOrEmpty
        }

        It 'takes the list of drives however it is handed over' {
            # Get-BurnerInfo ends with "return , $out" so that a machine with
            # one recorder still hands back a list. That makes
            # @(Get-BurnerInfo) an array holding an array, which two call
            # sites wrote and got away with for a year, because the next thing
            # they did was pipe it and a pipe unrolls.
            #
            # The first code to ask it for .Count instead saw one drive, chose
            # it, and handed a burner that was really two burners on to
            # Test-IsoFitsMedia. It only shows on a machine with a second
            # writer plugged in, which is where it was found.
            $two = @((New-Burner 'D:\' $false 'no disc'), (New-Burner 'E:\' $true))
            $wrapped = @(, $two)
            $wrapped.Count | Should -Be 1 -Because 'this is the shape the trap arrives in'
            $r = Get-BurnTarget $wrapped
            $r.Target.Drive | Should -Be 'E:\'
            @($r.Target.FreeSectors).Count | Should -Be 1 -Because 'two drives in one burner fails later and worse'
        }

        It 'says which drives it looked at when none of them is ready' {
            $r = Get-BurnTarget @((New-Burner 'D:\' $false 'no disc'), (New-Burner 'E:\' $false 'the disc is not blank'))
            $r.Why | Should -Match 'D:'
            $r.Why | Should -Match 'the disc is not blank'
            $r.Target | Should -BeNullOrEmpty
        }
    }

    Context 'when there really is a choice' {

        It 'asks, rather than taking whichever came back first' {
            $r = Get-BurnTarget @((New-Burner 'D:\' $true), (New-Burner 'E:\' $true))
            $r.Target | Should -BeNullOrEmpty -Because 'guessing between two blanks is how a disc ends up in the wrong drive'
            @($r.Ask).Count | Should -Be 2
        }

        It 'offers only the drives that could actually take the disc' {
            $r = Get-BurnTarget @((New-Burner 'D:\' $true), (New-Burner 'E:\' $false 'no disc'), (New-Burner 'F:\' $true))
            @($r.Ask | ForEach-Object { $_.Drive }) | Should -Be @('D:\', 'F:\')
        }

        It 'names the drive, the writer and the disc on each line of the question' {
            $row = Format-BurnTargetRow (New-Burner 'E:\' $true 'ready' 'BW-16D1HT' 'BD-R')
            $row | Should -Be 'E:  ASUS BW-16D1HT  (BD-R, 703 MB free)'
        }
    }

    Context 'once a drive has been chosen' {

        It 'uses the one it was given, not the first one' {
            $r = Get-BurnTarget @((New-Burner 'D:\' $true), (New-Burner 'E:\' $true)) 'E:'
            $r.Target.Drive | Should -Be 'E:\'
        }

        It 'takes the drive written any of the ways somebody writes a drive' {
            foreach ($spelling in 'E', 'E:', 'E:\') {
                (Get-BurnTarget @((New-Burner 'D:\' $true), (New-Burner 'E:\' $true)) $spelling).Target.Drive |
                    Should -Be 'E:\' -Because "'$spelling' names a drive"
            }
        }

        It 'refuses rather than quietly using the other drive' {
            # Disc 2 of a set, with the blank put in the wrong writer. Writing
            # it to the other drive would leave the pair split across two
            # machines' worth of discs and nobody told.
            $r = Get-BurnTarget @((New-Burner 'D:\' $true), (New-Burner 'E:\' $false 'no disc')) 'E:'
            $r.Target | Should -BeNullOrEmpty
            $r.Why | Should -Match 'E:'
            $r.Why | Should -Match 'not ready'
            $r.Why | Should -Match 'no disc'
        }

        It 'says so when the drive it was told to use has gone' {
            $r = Get-BurnTarget @((New-Burner 'D:\' $true)) 'E:'
            $r.Why | Should -Match 'not there any more'
        }
    }
}

Describe 'Watching a long file be read, not just the gaps between files' {

    # Noticed on 11 October while watching a real check happen: the window sat
    # at nought per cent for four and a half minutes and then jumped to a
    # hundred. Weighting the bar by bytes had already fixed the easier version
    # of this complaint, a bar that counts files, and could not help here. A
    # cut disc is one enormous part file beside a handful of small ones, so a
    # report sent only when a file FINISHES has exactly two positions on it.

    BeforeAll {
        $script:WDir = Join-Path ([IO.Path]::GetTempPath()) ('dww_' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
        $script:WSrc = Join-Path $script:WDir 'src'
        $script:WDisc = Join-Path $script:WDir 'disc'
        foreach ($d in $script:WSrc, $script:WDisc) { New-Item -ItemType Directory -Force -Path $d | Out-Null }
        # One file several chunks long and one that is not, which is the shape
        # of every cut disc this writes.
        $big = New-Object byte[] (9MB)
        foreach ($d in $script:WSrc, $script:WDisc) {
            [IO.File]::WriteAllBytes((Join-Path $d 'big.bin'), $big)
            Set-Content -LiteralPath (Join-Path $d 'small.txt') -Value 'small' -Encoding Ascii -NoNewline
        }
        $script:WBig = Join-Path $script:WSrc 'big.bin'
    }

    AfterAll {
        if ($script:WDir -and (Test-Path $script:WDir)) {
            Remove-Item $script:WDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    Context 'the hash itself' {

        It 'answers exactly what Get-FileHash answers' {
            # It replaces Get-FileHash in the check, so a different answer
            # would not be a slower bar, it would be every disc reported bad.
            (Get-FileSha256Watched $script:WBig) |
                Should -Be (Get-FileHash -LiteralPath $script:WBig -Algorithm SHA256).Hash
        }

        It 'reports more than once while reading one file' {
            $script:Reads = @()
            $null = Get-FileSha256Watched $script:WBig { param($r) $script:Reads += $r }
            $script:Reads.Count | Should -BeGreaterThan 1
            $script:Reads[-1] | Should -Be (Get-Item $script:WBig).Length
        }

        It 'counts up and never back' {
            $script:Reads = @()
            $null = Get-FileSha256Watched $script:WBig { param($r) $script:Reads += $r }
            for ($i = 1; $i -lt $script:Reads.Count; $i++) {
                $script:Reads[$i] | Should -BeGreaterThan $script:Reads[$i - 1]
            }
        }

        It 'is content with nobody listening' {
            { $null = Get-FileSha256Watched $script:WBig } | Should -Not -Throw
        }

        It 'names the file it could not read' {
            # This wording goes on the dialog somebody reads when their disc
            # has failed, so it has to keep naming the file.
            # Beside the staging folder, not inside it. Dropped in there it
            # becomes a file the later tests expect to find on the disc, and
            # they then fail for a reason that has nothing to do with them.
            # The fail-watch caught exactly that.
            $p = Join-Path $script:WDir 'locked.bin'
            Set-Content -LiteralPath $p -Value 'x' -Encoding Ascii -NoNewline
            $h = [IO.File]::Open($p, 'Open', 'Read', 'None')
            try { { Get-FileSha256Watched $p } | Should -Throw -ExpectedMessage '*locked.bin*cannot be read*' }
            finally { $h.Dispose() }
        }
    }

    Context 'and what the check does with it' {

        It 'moves while the big file is being read, not only once it is over' {
            # The bug itself. Two files meant two reports, and the one that
            # mattered arrived when the long wait was already finished.
            $script:Seen = @()
            $null = Test-BurnedDisc -DiscRoot $script:WDisc -StagingFolder $script:WSrc -OnProgress {
                param($done, $total, $rel, $bytesDone, $bytesTotal)
                $script:Seen += , @{ Rel = $rel; Bytes = $bytesDone; Total = $bytesTotal }
            }
            @($script:Seen).Count | Should -BeGreaterThan 2 -Because 'two files used to mean exactly two reports'
            $mid = @($script:Seen | Where-Object { $_.Rel -eq 'big.bin' -and $_.Bytes -gt 0 -and $_.Bytes -lt $_.Total })
            $mid.Count | Should -BeGreaterThan 0 -Because 'a report from inside the long read is the whole point'
        }

        It 'still finishes with the whole disc counted' {
            $script:Seen = @()
            $v = Test-BurnedDisc -DiscRoot $script:WDisc -StagingFolder $script:WSrc -OnProgress {
                param($done, $total, $rel, $bytesDone, $bytesTotal)
                $script:Seen += , @{ Bytes = $bytesDone; Total = $bytesTotal }
            }
            $v.Ok | Should -BeTrue
            $script:Seen[-1].Bytes | Should -Be $script:Seen[-1].Total
        }

        It 'still catches a file that came back as different bytes' {
            # The reports are new. The verdict must be exactly what it was.
            $other = Join-Path $script:WDir 'wrong'
            New-Item -ItemType Directory -Force -Path $other | Out-Null
            Copy-Item (Join-Path $script:WDisc 'small.txt') $other
            $b = New-Object byte[] (9MB)
            $b[4MB] = 7
            [IO.File]::WriteAllBytes((Join-Path $other 'big.bin'), $b)
            $v = Test-BurnedDisc -DiscRoot $other -StagingFolder $script:WSrc
            $v.Ok | Should -BeFalse
            $v.WrongContent | Should -Contain 'big.bin'
        }
    }
}

Describe 'Burning one disc out of a set from the Burn to disc button' {

    # Found on 11 October, burning disc 2 of Hollow Knight by hand. The button
    # looked for the staging folder in one fixed place, "disc" under the output
    # folder, which is where a SINGLE disc stages. A set stages into "disc D1"
    # and "disc D2", so the button wrote a disc of a set and then said it had
    # nothing to compare it against. Writing a disc and being unable to check
    # it is the one thing that button exists not to do.
    #
    # And it always took the newest image, which on a set is always the last
    # disc, so disc 1 could not be reached from there at all.

    BeforeAll {
        $script:SbDir = Join-Path ([IO.Path]::GetTempPath()) ('dwsb_' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
        $script:SbSet = Join-Path $script:SbDir 'set'
        $script:SbOne = Join-Path $script:SbDir 'one'
        $script:SbBare = Join-Path $script:SbDir 'bare'
        foreach ($d in $script:SbSet, $script:SbOne, $script:SbBare) {
            New-Item -ItemType Directory -Force -Path $d | Out-Null
        }
        foreach ($d in 'disc D1', 'disc D2') {
            New-Item -ItemType Directory -Force -Path (Join-Path $script:SbSet $d) | Out-Null
        }
        New-Item -ItemType Directory -Force -Path (Join-Path $script:SbOne 'disc') | Out-Null

        function New-Iso([string]$dir, [string]$name, [int]$mb, [datetime]$when) {
            # Megabytes, because the row this feeds prints megabytes and a
            # test that pins the wording has to pin a real number with it.
            $p = Join-Path $dir $name
            [IO.File]::WriteAllBytes($p, (New-Object byte[] ($mb * 1MB)))
            (Get-Item -LiteralPath $p).LastWriteTime = $when
            return $p
        }
        # Disc 2 written after disc 1, which is what a set build does and what
        # made "newest" the wrong answer.
        $script:SbD1 = New-Iso $script:SbSet 'Big Game D1.iso' 8 ([datetime]'2026-10-11 20:00')
        $script:SbD2 = New-Iso $script:SbSet 'Big Game D2.iso' 4 ([datetime]'2026-10-11 20:05')
        $script:SbSingle = New-Iso $script:SbOne 'Big Game.iso' 2 ([datetime]'2026-10-11 20:00')
        # A single disc whose label just happens to end the way a set disc does.
        $script:SbOdd = New-Iso $script:SbOne 'Quake D2.iso' 2 ([datetime]'2026-10-11 20:01')
        $script:SbLost = New-Iso $script:SbBare 'Orphan.iso' 1 ([datetime]'2026-10-11 20:00')
    }

    AfterAll {
        if ($script:SbDir -and (Test-Path $script:SbDir)) {
            Remove-Item $script:SbDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    Context 'the folder an image was built from' {

        It 'finds the numbered folder for a disc of a set' {
            (Split-Path (Get-StagingFolderFor $script:SbD1 $script:SbSet) -Leaf) | Should -Be 'disc D1'
            (Split-Path (Get-StagingFolderFor $script:SbD2 $script:SbSet) -Leaf) | Should -Be 'disc D2'
        }

        It 'finds the plain folder for a single disc' {
            (Split-Path (Get-StagingFolderFor $script:SbSingle $script:SbOne) -Leaf) | Should -Be 'disc'
        }

        It 'falls back to the plain folder for a label that merely ends like a set' {
            # "Quake D2" is a name somebody could give one disc. There is no
            # "disc D2" beside it, so the only folder there is is the answer.
            (Split-Path (Get-StagingFolderFor $script:SbOdd $script:SbOne) -Leaf) | Should -Be 'disc'
        }

        It 'answers with nothing when there is no folder at all' {
            Get-StagingFolderFor $script:SbLost $script:SbBare | Should -BeNullOrEmpty
        }

        It 'answers with nothing rather than throwing when asked about nothing' {
            Get-StagingFolderFor '' '' | Should -BeNullOrEmpty
            Get-StagingFolderFor $null $null | Should -BeNullOrEmpty
        }
    }

    Context 'which images there are to choose from' {

        It 'offers disc 1 first, although disc 2 is the newer file' {
            # The whole reason disc 1 could not be burned from this button.
            $i = @(Get-BurnableIsos $script:SbSet)
            @($i | ForEach-Object { $_.Name }) | Should -Be @('Big Game D1.iso', 'Big Game D2.iso')
            (Get-Item $script:SbD2).LastWriteTime |
                Should -BeGreaterThan (Get-Item $script:SbD1).LastWriteTime -Because 'newest really is the other one'
        }

        It 'gives each image its own staging folder' {
            $i = @(Get-BurnableIsos $script:SbSet)
            (Split-Path $i[0].Stage -Leaf) | Should -Be 'disc D1'
            (Split-Path $i[1].Stage -Leaf) | Should -Be 'disc D2'
            $i[0].HasStage | Should -BeTrue
        }

        It 'still offers an image with nothing to check it against, and says so' {
            # Refusing to burn it would be worse: somebody may have put an ISO
            # in there on purpose. Being told it cannot be checked is the point.
            $i = @(Get-BurnableIsos $script:SbBare)
            @($i).Count | Should -Be 1
            $i[0].HasStage | Should -BeFalse
            (Format-BurnIsoRow $i[0]) | Should -Match 'nothing to check it against'
        }

        It 'says which folder an image will be checked against' {
            $i = @(Get-BurnableIsos $script:SbSet)
            (Format-BurnIsoRow $i[0]) | Should -Be 'Big Game D1.iso   8.0 MB   (checks against disc D1)'
        }

        It 'finds nothing in a folder with no images, and does not throw' {
            $empty = Join-Path $script:SbDir 'empty'
            New-Item -ItemType Directory -Force -Path $empty | Out-Null
            @(Get-BurnableIsos $empty).Count | Should -Be 0
            @(Get-BurnableIsos (Join-Path $script:SbDir 'not-there')).Count | Should -Be 0
        }
    }

    Context 'and what the button does with them' {

        BeforeAll {
            $src = Get-Content (Join-Path (Split-Path $PSScriptRoot -Parent) 'DiscWright.ps1') -Raw
            $i = $src.IndexOf('$btnBurn.Add_Click({')
            $script:SbHandler = $src.Substring($i, 6000)
        }

        It 'asks the images, rather than taking the newest file in the folder' {
            $script:SbHandler | Should -Match 'Get-BurnableIsos'
            $script:SbHandler | Should -Not -Match "Filter '\*\.iso'[\s\S]{0,120}LastWriteTime -Descending"
        }

        It 'asks which one only when there is more than one' {
            $script:SbHandler | Should -Match '\$isos\.Count -gt 1'
            $script:SbHandler | Should -Match 'Select-BurnIso'
        }

        It 'checks against the folder that image came from, not a fixed name' {
            $script:SbHandler | Should -Match '\$stage = \$want\.Stage'
            $script:SbHandler | Should -Not -Match "Join-Path \`$t 'disc'"
        }
    }
}

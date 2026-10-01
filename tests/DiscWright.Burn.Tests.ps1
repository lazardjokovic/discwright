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

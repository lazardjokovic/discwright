<#
    A real disc, in a real drive, written and read back.

    Nothing here runs unless somebody names a drive, because writing spends a
    disc and erasing destroys whatever is on one.

      DISCWRIGHT_SPEND_A_DISC    a drive letter. Erases what is in that drive
                                 if it has to, writes to it, and reads every
                                 file back off it.
      DISCWRIGHT_CHECK_A_DISC    a drive letter, read only, spends nothing.
      DISCWRIGHT_CHECK_AGAINST   the staged folder to compare that disc with.

    Modelled on the rewrite's BurningARealDisc, which puts the reason for
    existing better than this could: it is a test rather than a script because
    it is the only way to find out whether any of the burning works, and
    something nobody can run is something nobody runs. Everything else in this
    suite runs with no media in the drive and writes nothing, which leaves the
    write itself - the one part a CD-RW can prove and a mock cannot - with no
    coverage at all.

    One difference from the rewrite worth knowing. The rewrite asks a drive
    whether it can shut its own tray, by telling it to shut a tray that is
    already shut: a drive that cannot refuses with 0x80070001, incorrect
    function, which is how a drive says it does not do that. The PowerShell app
    has no such probe. It ejects after a write and asks the person to close the
    tray, every time, whatever the drive. So its own burn flow cannot run
    unattended, and this closes the tray itself: by telling the drive to, and
    failing that, by making a noise and waiting for hands.
#>

BeforeDiscovery {
    # Read here as well as in the blocks, because -Skip: is decided during
    # discovery and a variable set in a setup block is still empty then.
    $script:SpendOn = $env:DISCWRIGHT_SPEND_A_DISC
    $script:CheckOn = $env:DISCWRIGHT_CHECK_A_DISC
    $script:Against = $env:DISCWRIGHT_CHECK_AGAINST
}

BeforeAll {
    $root = Split-Path $PSScriptRoot -Parent
    . (Join-Path $root 'burn\DiscWright.Burn.ps1')

    # The application's own functions, lifted out rather than dot-sourced,
    # which is how the other suites reach New-Iso without starting a window.
    $appScript = Join-Path $root 'DiscWright.ps1'
    $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($appScript, [ref]$null, [ref]$parseErrors)
    if ($parseErrors -and $parseErrors.Count) { throw "DiscWright.ps1 has $($parseErrors.Count) parse errors" }
    foreach ($f in $ast.FindAll({ param($n)
            $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false)) {
        . ([scriptblock]::Create($f.Extent.Text))
    }

    # A drive letter, however it was written: D, D:, or D: and a separator.
    # Reduced to the letter itself rather than trimmed, because a trim needs the
    # separator spelled out and a separator in a test file is the one character
    # this project has learned not to type.
    function Get-DriveLetter([string]$given) {
        $only = ($given -replace '[^A-Za-z]', '')
        if ($only.Length -ne 1) { throw "'$given' is not one drive letter" }
        return $only.ToUpperInvariant()
    }

    function Get-DriveRoot([string]$letter) {
        return ((Get-DriveLetter $letter) + ':' + [string][char]92)
    }

    # Exactly one, or it says so. Returning whatever matched let two recorders
    # through as an array, and the first thing that touched it failed with
    # "[System.Object[]] does not contain a method named 'op_Division'", which
    # says nothing about drives at all.
    function Get-Recorder([string]$letter) {
        $want = (Get-DriveLetter $letter) + ':'
        # Assigned, never piped. Get-BurnerInfo ends with "return , $out", which
        # wraps its list so the array is not enumerated, and the application's
        # own code says "Not @(Get-BurnerInfo)" for the same reason. Piped, this
        # hands Where-Object the whole array as ONE item: the filter matches the
        # array, one recorder comes back, and every property on it is an array
        # of two drives. The first arithmetic then fails with "[System.Object[]]
        # does not contain a method named 'op_Division'", which names neither
        # drives nor the pipe that caused it.
        $all = Get-BurnerInfo
        $hits = @($all | Where-Object { $_.Drive -like ($want + '*') })
        if ($hits.Count -ne 1) {
            $saw = (@($all | ForEach-Object { $_.Drive }) -join ', ')
            throw "expected one recorder at $want, found $($hits.Count). Drives seen: $saw"
        }
        if ($hits[0].Drive -isnot [string]) {
            throw ("the recorder at $want came back with its properties as " +
                   "$($hits[0].Drive.GetType().Name), which means the list was piped rather than assigned")
        }
        return $hits[0]
    }

    # True when the drive shut its own tray. The rewrite calls this
    # CanShutItsOwnTray and keeps the answer; here the answer and the act are
    # the same call, because the only reason to ask is to do it.
    function Close-DiscTray($id) {
        $rec = $null
        try {
            $rec = New-Object -ComObject IMAPI2.MsftDiscRecorder2
            $rec.InitializeDiscRecorder($id)
            $rec.CloseTray()
            return $true
        } catch {
            return $false
        } finally {
            if ($rec) { try { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($rec) } catch {} }
        }
    }

    function Request-Hands([string]$what) {
        Write-Host ''
        Write-Host "  HANDS NEEDED: $what" -ForegroundColor Yellow
        [console]::beep(880, 250); Start-Sleep -Milliseconds 120
        [console]::beep(660, 450); Start-Sleep -Milliseconds 120
        [console]::beep(880, 250)
    }

    # A mounted volume, not merely a closed tray. Windows takes a few seconds
    # after the tray shuts to read what is in it, and a check that starts too
    # early reports a disc that is not there.
    function Wait-ForDisc([string]$discRoot, [int]$seconds = 240) {
        $deadline = (Get-Date).AddSeconds($seconds)
        $beeped = $false
        while ((Get-Date) -lt $deadline) {
            try {
                $null = @(Get-ChildItem -LiteralPath $discRoot -Force -ErrorAction Stop)
                return $true
            } catch {
                Start-Sleep -Seconds 2
                # One reminder, halfway, in case the first beep was missed.
                $left = ($deadline - (Get-Date)).TotalSeconds
                if (-not $beeped -and $left -lt ($seconds / 2)) {
                    $beeped = $true
                    Request-Hands "still waiting for $discRoot to come back"
                }
            }
        }
        return $false
    }

    # Small, random, and made here, so what is on the disc is known exactly
    # rather than borrowed from whatever happened to be lying about.
    function New-SmallPayload([string]$into) {
        New-Item -ItemType Directory -Force -Path $into | Out-Null
        $rng = [System.Random]::new(20261104)
        foreach ($f in @(
            @{ Rel = 'setup_a_game.exe'; Bytes = 400000 },
            @{ Rel = (Join-Path 'AUTORUN' 'menu.hta'); Bytes = 8000 },
            @{ Rel = 'READ ME.txt'; Bytes = 300 })) {
            $full = Join-Path $into $f.Rel
            New-Item -ItemType Directory -Force -Path (Split-Path $full -Parent) | Out-Null
            $bytes = New-Object byte[] $f.Bytes
            $rng.NextBytes($bytes)
            [IO.File]::WriteAllBytes($full, $bytes)
        }
        return @(Get-ChildItem -LiteralPath $into -Recurse -File).Count
    }
}

Describe 'A disc already in a drive, against the folder it was built from' -Tag 'Hardware' -Skip:(-not ($script:CheckOn -and $script:Against)) {

    It 'has every file that folder has, byte for byte' {
        $discRoot = Get-DriveRoot $env:DISCWRIGHT_CHECK_A_DISC
        $staged = $env:DISCWRIGHT_CHECK_AGAINST
        Write-Host "  reading $discRoot against $staged"

        Test-Path -LiteralPath $discRoot | Should -BeTrue -Because 'the disc has to be readable to be checked'
        Test-Path -LiteralPath $staged   | Should -BeTrue -Because 'there is nothing to compare it with otherwise'

        $v = Test-BurnedDisc -DiscRoot $discRoot -StagingFolder $staged -OnProgress {
            param($done, $total, $rel, $bytesDone, $bytesTotal)
            if ($total -gt 0 -and ($done % 25 -eq 0 -or $done -eq $total)) {
                Write-Host ("    file {0} of {1}" -f $done, $total)
            }
        }

        Write-Host ("    on disc: {0} files" -f $v.FilesOnDisc)
        foreach ($k in 'Missing', 'Unexpected', 'WrongSize', 'WrongContent', 'Unreadable') {
            if ($v.$k.Count) { Write-Host ("    $k : " + ($v.$k -join ', ')) -ForegroundColor Red }
        }
        $v.Ok | Should -BeTrue -Because 'every file on the disc should match what built it'
    }
}

Describe 'Writing a real disc, which spends it' -Tag 'Hardware' -Skip:(-not $script:SpendOn) {

    It 'erases what is in the way, writes, and reads every file back' {
        $letter = $env:DISCWRIGHT_SPEND_A_DISC
        $discRoot = Get-DriveRoot $letter
        $d = Get-Recorder $letter
        $d | Should -Not -BeNullOrEmpty -Because "there should be a recorder at $letter"

        Write-Host ("  {0} {1} {2}, holding {3}" -f $d.Drive, $d.Vendor, $d.Product, $d.MediaName)
        Write-Host ("    blank={0} rewritable={1} erasable={2} free={3:N2} GB" -f `
            $d.Blank, $d.Rewritable, $d.Erasable, ($d.FreeBytes / 1GB))

        $d.MediaWritable | Should -BeTrue -Because 'there is no point writing to a disc the drive will not write'

        # Erased only because somebody named this drive. Whatever is on it goes.
        if (Test-DiscErasable $d.MediaType $d.Blank) {
            Write-Host '    erasing it, because it is not blank and it can be'
            $e = Clear-DiscMedia -Target $d -OnProgress { param($s) if ($s % 10 -eq 0) { Write-Host "      ${s}s" } }
            Write-Host ("    erase: {0} after {1}s, {2}" -f $e.Ok, $e.Seconds, $e.Why)
            $e.Ok | Should -BeTrue -Because 'an erase that fails leaves nothing to write to'
            # The drive ejects after an erase on some firmware, so make sure it
            # is readable again before asking it anything else.
            if (-not (Close-DiscTray $d.Id)) { Request-Hands "close the tray on $($d.Drive) after the erase" }
            $null = Wait-ForDisc $discRoot
            $d = Get-Recorder $letter
            Write-Host ("    now holding {0}, blank={1}, {2:N0} sectors free" -f $d.MediaName, $d.Blank, $d.FreeSectors)
        }

        $box = Join-Path ([IO.Path]::GetTempPath()) ('dwhw_' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
        $staged = Join-Path $box 'disc'
        $iso = Join-Path $box 'A REAL BURN.iso'
        try {
            $made = New-SmallPayload $staged
            Write-Host "    staged $made files"
            New-Iso $staged $iso 'A REAL BURN'
            Test-Path -LiteralPath $iso | Should -BeTrue -Because 'there is nothing to burn without an image'
            $isoSize = (Get-Item -LiteralPath $iso).Length
            Write-Host ("    image {0:N2} MB" -f ($isoSize / 1MB))
            Test-IsoFitsMedia -IsoBytes $isoSize -FreeSectors $d.FreeSectors |
                Should -BeTrue -Because 'the image has to fit the blank in the drive'

            # Script scope, and a name of its own, for a reason worth writing
            # down. A scriptblock invoked with & runs in the INVOKER's scope,
            # and Watch-BurnProgress keeps a local called $reports, an Int32
            # counting how many times it has called back. A callback of this
            # one's own that said $reports got the module's counter instead and
            # died with "[System.Int32] does not contain a method named 'Add'",
            # mid-burn, pointing at this file. The application's own callback is
            # immune because it only ever touches $script: variables.
            $script:HwBurnReports = New-Object System.Collections.ArrayList
            $r = Write-IsoToDisc -IsoPath $iso -Drive $d.Drive -Confirm:$false -OnProgress {
                param($pct, $phase, $detail)
                $null = $script:HwBurnReports.Add(@{ Pct = $pct; Phase = $phase })
                if ($script:HwBurnReports.Count % 10 -eq 1) {
                    Write-Host ("      {0}% {1}" -f $pct, $phase)
                }
            }

            Write-Host ("    written in {0}s, watched={1}, updates={2}" -f $r.Seconds, $r.Watched, $r.Updates)
            if ($r.AttachWhy) { Write-Host ("    attach: {0}" -f $r.AttachWhy) }
            if ($r.SinkError) { Write-Host ("    sink error: {0}" -f $r.SinkError) -ForegroundColor Red }

            # 0.11.0's progress window exists because the engine reports from a
            # thread with no runspace in it and nothing was listening. On real
            # hardware that either works or it does not, and a mock cannot say.
            $r.Watched | Should -BeTrue -Because 'the drive should accept the progress listener'
            $r.Updates | Should -BeGreaterThan 0 -Because 'a listener that is attached and silent is the other failure'
            $r.SinkError | Should -BeNullOrEmpty
            @($script:HwBurnReports | Where-Object { $_.Phase -eq 'writing' }).Count |
                Should -BeGreaterThan 0 -Because 'the drive should say when it is writing, by name'

            # Written is not the same as readable, which is the whole point.
            if (Close-DiscTray $d.Id) {
                Write-Host '    the drive shut its own tray'
            } else {
                Request-Hands "push the tray shut on $($d.Drive), the disc is written and waiting to be read"
            }

            (Wait-ForDisc $discRoot) | Should -BeTrue -Because "$discRoot has to come back before anything can be read off it"
            Write-Host "    $discRoot is readable again, checking every file"

            $v = Test-BurnedDisc -DiscRoot $discRoot -StagingFolder $staged -OnProgress {
                param($done, $total, $rel, $bytesDone, $bytesTotal)
                Write-Host ("      {0} of {1}  {2}" -f $done, $total, $rel)
            }
            foreach ($k in 'Missing', 'Unexpected', 'WrongSize', 'WrongContent', 'Unreadable') {
                if ($v.$k.Count) { Write-Host ("    $k : " + ($v.$k -join ', ')) -ForegroundColor Red }
            }
            $v.FilesOnDisc | Should -Be $made -Because 'every file staged should be on the disc and nothing else'
            $v.Ok | Should -BeTrue -Because 'this is the only check that proves a drive wrote what it was given'
            Write-Host "    all $($v.FilesOnDisc) files match" -ForegroundColor Green
            [console]::beep(1175, 250)
        } finally {
            if (Test-Path -LiteralPath $box) {
                Remove-Item -LiteralPath $box -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }
}

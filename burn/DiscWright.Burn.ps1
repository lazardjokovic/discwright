<#
  Writing an ISO to a disc, and proving afterwards that what is on the disc is
  what was supposed to be on it.

  WHY THIS IS SEPARATE

  Same rule as print\: the app may reach into this, this never reaches into the
  app. The ISO builder has to stay able to ship on its own.

  WHY IT CHECKS SO MUCH BEFORE IT WRITES

  A CD-R is written once. Every mistake that can be caught by asking the drive
  a question costs nothing, and every mistake that is not caught costs a disc.
  So everything answerable is answered first, and the write itself refuses
  rather than guesses.

  WHAT IT DOES NOT DO

  It does not close or append to a multi-session disc. One ISO, one disc,
  written once.

  It will erase a rewritable disc, and only when asked. Nothing here erases on
  its own initiative: Get-EraseOffer answers whether there is a disc an erase
  would make usable, the caller asks the person holding it, and Clear-DiscMedia
  does it. A quick erase, which blanks the table of contents in well under a
  minute, not a full one, which rewrites the whole surface and takes twenty
  minutes on a CD-RW.

  That split is deliberate and it is the same split as everything else here:
  the decision is a pure function the tests can drive with invented drives, and
  the act needs a drive and a disc that is meant to be spent, so it is on the
  hand-check list in README.md instead.
#>

# IMAPI_MEDIA_PHYSICAL_TYPE. The numbers come back from the drive and mean
# nothing on their own, and a report that says "media type 9" is a report
# nobody can act on.
$script:MediaTypes = @{
    0  = @{ Name = 'no disc';                 Writable = $false; Rewritable = $false }
    1  = @{ Name = 'CD-ROM, pressed';         Writable = $false; Rewritable = $false }
    2  = @{ Name = 'CD-R';                    Writable = $true; Rewritable = $false }
    3  = @{ Name = 'CD-RW';                   Writable = $true; Rewritable = $true  }
    4  = @{ Name = 'DVD-ROM, pressed';        Writable = $false; Rewritable = $false }
    5  = @{ Name = 'DVD-RAM';                 Writable = $true; Rewritable = $true  }
    6  = @{ Name = 'DVD+R';                   Writable = $true; Rewritable = $false }
    7  = @{ Name = 'DVD+RW';                  Writable = $true; Rewritable = $true  }
    8  = @{ Name = 'DVD+R DL';                Writable = $true; Rewritable = $false }
    9  = @{ Name = 'DVD-R';                   Writable = $true; Rewritable = $false }
    10 = @{ Name = 'DVD-RW';                  Writable = $true; Rewritable = $true  }
    11 = @{ Name = 'DVD-R DL';                Writable = $true; Rewritable = $false }
    12 = @{ Name = 'hard disk';               Writable = $false; Rewritable = $false }
    13 = @{ Name = 'DVD+RW DL';               Writable = $true; Rewritable = $true  }
    14 = @{ Name = 'HD DVD-ROM';              Writable = $false; Rewritable = $false }
    15 = @{ Name = 'HD DVD-R';                Writable = $true; Rewritable = $false }
    16 = @{ Name = 'HD DVD-RAM';              Writable = $true; Rewritable = $true  }
    17 = @{ Name = 'BD-ROM, pressed';         Writable = $false; Rewritable = $false }
    18 = @{ Name = 'BD-R';                    Writable = $true; Rewritable = $false }
    19 = @{ Name = 'BD-RE';                   Writable = $true; Rewritable = $true  }
}

# The profiles a drive reports it can write, which is about the hardware rather
# than about whatever disc is in it at the moment.
$script:WriteProfiles = @{
    9 = 'CD-R'; 10 = 'CD-RW'; 17 = 'DVD-R'; 18 = 'DVD-R DL'; 19 = 'DVD-RW'
    26 = 'DVD+RW'; 27 = 'DVD+R'; 42 = 'DVD+RW DL'; 43 = 'DVD+R DL'
    65 = 'BD-R'; 66 = 'BD-RE'
}

# Optical media is addressed in 2048 byte sectors, so a free space figure in
# sectors has to be multiplied before it can be compared with a file size.
$script:SectorBytes = 2048

<#
    A speed in KB/s means a different multiple depending on what is loaded: a CD
    1x is 150 KB/s and a DVD 1x is 1385 KB/s. Reporting raw KB/s to somebody
    deciding whether to burn at 8x or 24x is not an answer.
#>
<#
    What 1x is, in the unit IMAPI speaks.

    Every speed in this module comes from or goes to IMAPI, and IMAPI measures
    speed in SECTORS PER SECOND, not in KB/s. It says so at both ends:

        IWriteSpeedDescriptor::get_WriteSpeed
            "Write speed of the current media, measured in sectors per second."
        IDiscFormat2Data::SetWriteSpeed(LONG RequestedSectorsPerSecond, ...)
            "Requested write speed measured in disc sectors per second."

    These are Microsoft's own numbers, out of imapi2.idl in the Windows SDK,
    where they have names of their own:

        const ULONG IMAPI_SECTOR_SIZE = 2048;
        #define IMAPI_SECTORS_PER_SECOND_AT_1X_CD      75
        #define IMAPI_SECTORS_PER_SECOND_AT_1X_DVD     680
        #define IMAPI_SECTORS_PER_SECOND_AT_1X_BD      2195
        #define IMAPI_SECTORS_PER_SECOND_AT_1X_HD_DVD  4568

    This used to divide by 150, the KB/s of a 1x CD, which is exactly twice the
    75 it should be. So every speed shown was half the truth and every speed
    asked for was double what was meant. Found on 12 October when a drive
    offered 750 for a High Speed CD-RW and the application called it 5x, which
    is not a speed any CD-RW is made in. It is 10x, and the real burns say so:
    690,814,976 bytes went to a disc in 496 seconds, which is 1.5 MB a second.
    At the 5x it claimed, the write alone could not have finished in under 900.
#>
$script:SectorsAt1x = @{ CD = 75.0; DVD = 680.0; BD = 2195.0; HDDVD = 4568.0 }

function Get-SpeedBaseSectors([int]$mediaType) {
    # The media type codes are the table at the top of this file.
    if ($mediaType -in 1, 2, 3)      { return $script:SectorsAt1x.CD }
    if ($mediaType -in 14, 15, 16)   { return $script:SectorsAt1x.HDDVD }
    if ($mediaType -in 17, 18, 19)   { return $script:SectorsAt1x.BD }
    # Everything left is a DVD of some kind, and so is code 12, which is a hard
    # disk and can never be written to here anyway.
    return $script:SectorsAt1x.DVD
}

function ConvertTo-SpeedMultiple([int]$sectors, [int]$mediaType) {
    return [Math]::Round($sectors / (Get-SpeedBaseSectors $mediaType), 1)
}

<#
    Turn "8x" or "600" into the sectors per second the drive wants, for
    whatever is loaded. A multiple is what the disc is labelled with, so that is
    what a person has in their hand when they choose.

    A bare number is passed through untouched, because that is the unit IMAPI
    takes and somebody who typed one meant it.
#>
function ConvertTo-WriteSpeedSectors {
    param(
        [Parameter(Mandatory)][string]$Speed,
        [Parameter(Mandatory)][int]$MediaType
    )
    $t = $Speed.Trim()
    if ($t -match '^(?<n>[0-9]+(\.[0-9]+)?)\s*[xX]$') {
        return [int][Math]::Round([double]$Matches.n * (Get-SpeedBaseSectors $MediaType))
    }
    if ($t -match '^[0-9]+$') { return [int]$t }
    throw "Cannot read '$Speed' as a write speed. Use a multiple like 8x, or sectors per second like 600."
}

<#
    Roughly how long a burn will take, so a locked window is an expected wait
    rather than a hang.

    Writing is bytes divided by speed, which is the easy part. Finalising is
    not: closing a disc writes the lead-in and lead-out, takes most of a minute
    on a CD regardless of how little was written, and is why a 50 MB disc still
    takes longer than the arithmetic says.
#>
function Get-BurnEstimateSeconds {
    param(
        [Parameter(Mandatory)][long]$Bytes,
        [Parameter(Mandatory)][int]$SpeedSectors,
        [int]$MediaType = 2,
        [bool]$CloseMedia = $true
    )
    if ($SpeedSectors -le 0) { return 0 }
    # Drives rarely hold their rated speed for the whole write, so this is
    # deliberately a little pessimistic. An estimate that runs under is worse
    # than one that runs over: the first looks like a hang.
    # Sectors per second times 2048 bytes a sector, which is IMAPI_SECTOR_SIZE.
    # This used to be times 1024, against a number that was already twice what
    # it should have been, so the two mistakes cancelled and the estimate came
    # out right by luck. They stopped cancelling the moment somebody typed a
    # speed, because that path built its number the other way round.
    $write = $Bytes / ($SpeedSectors * 2048.0) * 1.15
    # Calibrated against the one real burn there has been: 241.7 MB to a CD-R
    # on an ASUS DRW-24D5MT took 93 seconds all in, of which roughly 69 was the
    # write itself. One measurement is not a curve, so this is deliberately a
    # little over rather than a little under.
    $finalise = if (-not $CloseMedia) { 5 }
                elseif ($MediaType -in 1, 2, 3) { 25 }
                else { 20 }
    return [int][Math]::Ceiling($write + $finalise)
}

function Format-BurnEstimate([int]$seconds) {
    if ($seconds -le 0) { return 'unknown' }
    if ($seconds -lt 90) { return "about $seconds seconds" }
    $m = [int][Math]::Round($seconds / 60.0)
    return "about $m minute$(if ($m -ne 1) { 's' })"
}

function Get-WriteSpeeds($fmt, [int]$mediaType) {
    $out = @()
    try {
        foreach ($d in $fmt.SupportedWriteSpeedDescriptors) {
            $out += , [pscustomobject]@{
                Sectors  = [int]$d.WriteSpeed
                Multiple = ConvertTo-SpeedMultiple ([int]$d.WriteSpeed) $mediaType
                PureCav  = [bool]$d.RotationTypeIsPureCAV
            }
        }
    } catch {
        # Some drives only answer with a blank disc loaded. An empty list is an
        # honest answer; a guessed one is not.
        Write-Verbose "The drive would not list its write speeds: $($_.Exception.Message)"
    }
    return , @($out | Sort-Object Sectors -Descending)
}

function Get-MediaTypeName([int]$code) {
    if ($script:MediaTypes.ContainsKey($code)) { return $script:MediaTypes[$code].Name }
    return "unknown media type $code"
}

function Test-MediaWritable([int]$code) {
    if ($script:MediaTypes.ContainsKey($code)) { return [bool]$script:MediaTypes[$code].Writable }
    return $false
}

# Can this disc be erased and used again? Not the same question as whether it
# can be written: a CD-R can be written once and never again, a CD-RW can be
# written, erased and written again. The burn confirmation used to tell
# everybody their disc was not rewritable, including people holding a CD-RW,
# which is the sort of thing that makes somebody doubt the rest of the dialog.
function Test-MediaRewritable([int]$code) {
    if ($script:MediaTypes.ContainsKey($code)) { return [bool]$script:MediaTypes[$code].Rewritable }
    return $false
}

<#
    Would erasing this disc make it usable?

    Three things have to be true and they are three different questions. The
    drive has to be able to write this kind of disc at all; the disc has to be
    the rewritable kind, which a CD-R is not however many times somebody wishes
    it were; and there has to be something on it, because erasing a blank is a
    minute spent to achieve nothing.

    Pure, and separate from the erase itself, because this is the half that can
    be tested. The act needs a drive and a disc that is meant to be spent.
#>
function Test-DiscErasable([int]$mediaType, [bool]$blank) {
    if (-not (Test-MediaWritable $mediaType))   { return $false }
    if (-not (Test-MediaRewritable $mediaType)) { return $false }
    return (-not $blank)
}

<#
    The one disc on this machine that an erase would make usable, or why there
    is no such offer to make.

    One, never a list. "Erase one of these two discs" is not a question anybody
    can answer safely, and this is the same rule Get-BurnTarget already follows
    when two drives hold blanks: ask only where there is something to ask, and
    refuse rather than guess.

    Returns @{ Target } or @{ Why }, so a caller reads it exactly like
    Get-BurnTarget.
#>
function Get-EraseOffer($burners) {
    # Flattened the same way Get-BurnTarget flattens it, and for the same
    # reason: Get-BurnerInfo returns its list wrapped so that one recorder
    # still comes back as a list, and a bare $null piped yields one $null.
    $all = @($burners | ForEach-Object { $_ } | Where-Object { $_ })
    if (-not $all.Count) { return @{ Why = 'No disc recorder was found on this machine.' } }

    # Asked of Test-DiscErasable rather than read off the report's own Erasable
    # field, so that one function decides this and a caller handing in a drive
    # report it built itself cannot answer the question differently.
    # [bool] rather than handing .Blank straight over. Get-BurnerInfo always
    # sets it, but a caller building a drive report of its own may not, and an
    # absent field arrives as "" which a [bool] parameter refuses outright.
    # Nobody wants an erase offer to fail with a cast error.
    $can = @($all | Where-Object { Test-DiscErasable $_.MediaType ([bool]$_.Blank) })
    if (-not $can.Count) {
        # Said drive by drive, because "nothing to erase" is unhelpful when the
        # reason is a CD-R in one drive and an empty tray in the other.
        return @{ Why = ('No disc here can be erased: ' +
                         (($all | ForEach-Object { "$($_.Drive) $($_.MediaName)" }) -join '; ')) }
    }
    if ($can.Count -gt 1) {
        return @{ Why = ('More than one disc here could be erased, so none was: ' +
                         (($can | ForEach-Object { "$($_.Drive) $($_.MediaName)" }) -join '; ')) }
    }
    return @{ Target = $can[0] }
}

function ConvertTo-ProfileNames([int[]]$codes) {
    return @($codes | Where-Object { $script:WriteProfiles.ContainsKey([int]$_) } |
             ForEach-Object { $script:WriteProfiles[[int]$_] } | Sort-Object -Unique)
}

<#
    Will this ISO go on this disc?

    Answered in sectors, because that is what the drive counts in and because
    comparing bytes against a rounded capacity is how a burn gets started that
    cannot finish. An ISO is always a whole number of sectors.
#>
function Test-IsoFitsMedia {
    param(
        [Parameter(Mandatory)][long]$IsoBytes,
        [Parameter(Mandatory)][long]$FreeSectors
    )
    $needed = [long][Math]::Ceiling($IsoBytes / [double]$script:SectorBytes)
    return [pscustomobject]@{
        IsoBytes      = $IsoBytes
        NeededSectors = $needed
        FreeSectors   = $FreeSectors
        FreeBytes     = $FreeSectors * $script:SectorBytes
        Fits          = ($needed -le $FreeSectors)
        SpareSectors  = $FreeSectors - $needed
    }
}

<#
    Every recorder on the machine, with what it can write and what is in it.

    Asking costs nothing and consumes no media, which is the whole point of
    doing it before anything is written.
#>
function Get-BurnerInfo {
    $out = @()
    $master = $null
    try { $master = New-Object -ComObject 'IMAPI2.MsftDiscMaster2' }
    catch { throw "IMAPI2 is not available on this machine: $($_.Exception.Message)" }

    if (-not $master.IsSupportedEnvironment) {
        throw 'IMAPI2 reports this environment cannot burn. A session without a desktop cannot.'
    }

    for ($i = 0; $i -lt $master.Count; $i++) {
        $rec = New-Object -ComObject 'IMAPI2.MsftDiscRecorder2'
        $rec.InitializeDiscRecorder($master.Item($i))

        $profiles = @()
        foreach ($p in $rec.SupportedProfiles) { $profiles += [int]$p }

        $info = [ordered]@{
            Index         = $i
            Id            = $master.Item($i)
            Drive         = @($rec.VolumePathNames)[0]
            Vendor        = "$($rec.VendorId)".Trim()
            Product       = "$($rec.ProductId)".Trim()
            Writes        = ConvertTo-ProfileNames $profiles
            MediaType     = 0
            MediaName     = 'no disc'
            MediaWritable = $false
            Blank         = $false
            Rewritable    = $false
            Erasable      = $false
            FreeSectors   = [long]0
            FreeBytes     = [long]0
            Speeds        = @()
            Ready         = $false
            Why           = ''
        }

        try {
            $fmt = New-Object -ComObject 'IMAPI2.MsftDiscFormat2Data'
            if ($fmt.IsRecorderSupported($rec)) {
                $fmt.Recorder = $rec
                $info.MediaType     = [int]$fmt.CurrentPhysicalMediaType
                $info.MediaName     = Get-MediaTypeName $info.MediaType
                $info.MediaWritable = Test-MediaWritable $info.MediaType
                $info.Blank         = [bool]$fmt.MediaHeuristicallyBlank
                $info.Rewritable    = Test-MediaRewritable $info.MediaType
                $info.Erasable      = Test-DiscErasable $info.MediaType $info.Blank
                $info.FreeSectors   = [long]$fmt.FreeSectorsOnMedia
                $info.FreeBytes     = $info.FreeSectors * $script:SectorBytes
                # Burning cheap media at the drive's top speed is a known way to
                # make a coaster, so the choice has to be visible rather than
                # left to whatever the drive picks.
                $info.Speeds        = Get-WriteSpeeds $fmt $info.MediaType
            } else {
                $info.Why = 'the data burner does not support this recorder'
            }
        } catch {
            # No disc, or a disc the drive has not finished reading. Neither is
            # an error worth throwing over: the report says what it found.
            $info.Why = $_.Exception.Message.Split([char]10)[0].Trim()
        }

        if (-not $info.MediaWritable) {
            if (-not $info.Why) {
                $info.Why = if ($info.MediaType -eq 0) { 'there is no disc in the drive' }
                            elseif ($info.MediaType -in 1, 4, 17) {
                                # A CD-R closed after burning reports itself as
                                # CD-ROM from then on, so this is what a disc
                                # this project just wrote looks like on the way
                                # back in. Measured, not assumed.
                                "the disc in it reports as $($info.MediaName), which is also what a " +
                                'written and closed disc reports, so either way it cannot be written again'
                            }
                            else { "the disc in it is $($info.MediaName), which cannot be written" }
            }
        } elseif (-not $info.Blank) {
            # "the disc is not blank" is the phrase callers match on, so it
            # stays first. What follows it used to be "and this never erases or
            # appends", which was true and is now only half true, and the half
            # that changed is the half somebody holding a CD-RW cares about.
            $info.Why = $(if ($info.Erasable) {
                              'the disc is not blank, and it is rewritable, so it can be erased first'
                          } else {
                              'the disc is not blank, and this never appends to a disc'
                          })
        } elseif ($info.FreeSectors -le 0) {
            $info.Why = 'the drive reports no free space'
        } else {
            $info.Ready = $true
            $info.Why = 'ready'
        }

        $out += , [pscustomobject]$info
    }
    return , $out
}

# --- what the drive says while it is writing ----------------------------------
#
# IMAPI has reported progress all along and nothing here listened. A burn was
# one blocking call that said nothing for ten minutes, so every dialog in the
# application ended up warning that the window would stop responding and that
# this was normal. It was not normal. It was unobserved.
#
# The engine fires DDiscFormat2DataEvents::Update roughly once a second with the
# sector it has reached, the phase it is in and its own estimate of the time
# left. The interface id and the dispid below are read back off imapi2.dll's own
# typelib by the test suite rather than trusted, because both of the values this
# was first written with were wrong: the interface is 2735413C, not 2735412A,
# and Update is dispid 0x200, not 0x100. A sink declaring the wrong dispid is
# advised quite happily and then never called, which looks exactly like a drive
# that does not report progress at all.
#
# PowerShell's own Register-ObjectEvent cannot do this job. Its handlers run
# when the runspace is free, and the runspace is the very thing blocked inside
# Write, so every update would arrive in a heap once the burn had finished.
#
# Nor can a managed sink hand each report to a scriptblock, which is how this
# was built first and why the bar did not move on 11 October. The engine does
# not report on the thread that called Write. It reports from a thread of its
# own, and PowerShell cannot run a scriptblock on a thread that has no runspace:
# every report died on "There is no Runspace available to run scripts in this
# thread", the sink caught it so the disc would not suffer for a bar, and the
# window sat at nought per cent for the whole write. Posting the report to the
# interface thread instead would not have helped either, because a thread
# sitting inside Write stops pumping, which is exactly why the main window used
# to freeze.
#
# So the sink only records, in C#, where no thread owns anything, and the write
# runs on a runspace of its own while the interface thread reads those numbers
# and paints. See Start-BurnRunspace and Watch-BurnProgress.

<#
    Which drive to write to, when a machine may have more than one.

    One answer for both paths, because until now they each made this decision
    with the same line copied out twice: take the first drive with a usable
    blank in it. That is right on a machine with one writer and silently wrong
    the moment it has two, which is the day somebody plugs in a second writer
    to find out whether the first one is the thing ruining their discs.

    Exactly one of three comes back. Target: get on with it, no question worth
    asking. Ask: the caller has to put it to somebody, and these are the drives
    worth offering. Why: refuse, and this is the sentence to show.

    No question is asked when only one drive could take the disc. Being asked
    to choose between one thing is not a choice, it is a dialog in the way.
#>
function Get-BurnTarget($burners, [string]$want = '') {
    # Flattened, not trusted. Get-BurnerInfo ends with "return , $out" so that
    # a machine with one recorder still hands back a list, which means
    # @(Get-BurnerInfo) is an array holding an array. Two call sites wrote it
    # that way and got away with it for a year, because the next thing they
    # did was pipe it and a pipe unrolls. The first code to ask it for .Count
    # instead saw one drive, picked it, and handed a burner that was really
    # two burners to Test-IsoFitsMedia. Found on a machine with a second
    # writer plugged in, which is the only machine where it shows.
    # The Where-Object is not decoration either: a function that returns an
    # empty list hands back nothing at all, $burners is then $null, and piping
    # $null yields one $null rather than nothing. Without this, a machine with
    # no recorder reports "no drive is ready" about a drive that is not there.
    $all = @($burners | ForEach-Object { $_ } | Where-Object { $_ })
    if (-not $all.Count) { return @{ Why = 'No disc recorder was found on this machine.' } }
    $ready = @($all | Where-Object { $_.Ready })

    if ($want) {
        # Compared on both sides rather than by building a drive letter out of
        # what was asked for. "E", "E:" and "E:\" all name the same drive, and
        # a recorder does not have to be called a letter at all: the tests
        # stand a folder in for a drive root, because Join-Path asks the
        # provider about a drive and throws for one this machine does not have.
        $trim = { param($x) ([string]$x).TrimEnd([char]92).TrimEnd(':') }
        $wn = & $trim $want
        $hit = @($ready | Where-Object { (& $trim $_.Drive) -eq $wn }) | Select-Object -First 1
        if ($hit) { return @{ Target = $hit } }
        # Named and not ready. Saying so beats quietly writing to the other
        # drive: on a set that would put disc 2 somewhere disc 1 is not, and
        # the person would find out when they went looking for the pair.
        $known = @($all | Where-Object { (& $trim $_.Drive) -eq $wn }) | Select-Object -First 1
        if ($known) {
            return @{ Why = "$($known.Drive) is the drive this burn was started on, and it is not ready: $($known.Why)." }
        }
        return @{ Why = "$want is the drive this burn was started on, and it is not there any more." }
    }

    if (-not $ready.Count) {
        return @{ Why = 'No drive is ready: ' + (($all | ForEach-Object { "$($_.Drive) $($_.Why)" }) -join '; ') }
    }
    if ($ready.Count -eq 1) { return @{ Target = $ready[0] } }
    return @{ Ask = $ready }
}

# One line of that question. The drive letter first, because that is what the
# person is about to go and put a disc into.
function Format-BurnTargetRow($b) {
    return ('{0}  {1} {2}  ({3}, {4:N0} MB free)' -f
            ([string]$b.Drive).TrimEnd([char]92), $b.Vendor, $b.Product, $b.MediaName, ($b.FreeBytes / 1MB))
}

<#
    The folder a given image was built from.

    Burn to disc used to look in one place, "disc" under the output folder,
    which is where a single disc stages. A set stages into "disc D1" and
    "disc D2", so the button wrote the disc and then said it had nothing to
    compare it against: it burned a disc of a set and could not check it,
    which is the one thing that button is for.

    The image carries the answer in its own name. A set disc is built with the
    label plus " D2", and the image is named after the label, so the number on
    the end of the image is the number of the staging folder.
#>
function Get-StagingFolderFor([string]$isoPath, [string]$outDir) {
    if (-not $isoPath -or -not $outDir) { return '' }
    $base = [IO.Path]::GetFileNameWithoutExtension($isoPath)
    $tries = @()
    if ($base -match '\sD(\d+)$') { $tries += ('disc D{0}' -f [int]$Matches[1]) }
    # "disc" second for a set image and first for anything else. A label that
    # happens to end in " D2" on a single disc still finds its own folder.
    $tries += 'disc'
    foreach ($t in $tries) {
        $p = (([string]$outDir).TrimEnd([char]92) + [char]92 + $t)
        if (Test-Path -LiteralPath $p) { return $p }
    }
    return ''
}

<#
    Every image in an output folder, in the order somebody would burn them.

    Newest first was right while a build made one image. A set makes several,
    and newest first means the button silently offers the LAST disc of the set
    every time, which is never the one to start with.
#>
function Get-BurnableIsos([string]$outDir) {
    if (-not $outDir -or -not (Test-Path -LiteralPath $outDir)) { return @() }
    $out = @()
    foreach ($f in @(Get-ChildItem -LiteralPath $outDir -Filter '*.iso' -File -ErrorAction SilentlyContinue)) {
        $base = [IO.Path]::GetFileNameWithoutExtension($f.Name)
        $n = $(if ($base -match '\sD(\d+)$') { [int]$Matches[1] } else { 0 })
        $stage = Get-StagingFolderFor $f.FullName $outDir
        $out += , [pscustomobject]@{
            Path = $f.FullName; Name = $f.Name; Bytes = [long]$f.Length
            Disc = $n; Stage = $stage; HasStage = [bool]$stage
        }
    }
    # Emitted plainly, NOT as ", $out". Wrapping is what Get-BurnerInfo does to
    # keep a single result a list, and it is why @(Get-BurnerInfo) turned out
    # to be an array holding an array and cost a bug on the one machine with
    # two writers. Written this way a caller wraps with @() and gets what it
    # expects for none, one or many, which is the shape that composes.
    return @($out | Sort-Object @{ e = { $_.Disc } }, @{ e = { $_.Name } })
}

# One line of the question, when there is more than one image to choose from.
function Format-BurnIsoRow($i) {
    $what = $(if ($i.HasStage) { 'checks against ' + (Split-Path $i.Stage -Leaf) }
              else { 'nothing to check it against' })
    return ('{0}   {1:N1} MB   ({2})' -f $i.Name, ($i.Bytes / 1MB), $what)
}

# One sector, on every optical format DiscWright writes to. Named rather than
# repeated, because this number is what turns a sector count into megabytes.
$script:BurnSectorBytes = 2048

function Add-BurnProgressType {
    if ('DiscWright.BurnProgressHook' -as [type]) { return }
    Add-Type -TypeDefinition @'
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;

namespace DiscWright
{
    // The dispinterface the burn engine calls back on. Declared by hand because
    // a stock Windows carries no interop assembly for IMAPI2, and generating one
    // at runtime is a great deal of machinery for one method.
    [ComImport, Guid("2735413C-7F64-5B0F-8F00-5D77AFBE261E"),
     InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    public interface IBurnEvents
    {
        [DispId(0x200)]
        void Update([In, MarshalAs(UnmanagedType.IDispatch)] object sender,
                    [In, MarshalAs(UnmanagedType.IDispatch)] object progress);
    }

    // One report, copied out of the sink in a single lock so the window never
    // draws a frame that mixes this second's sectors with last second's clock.
    public class BurnProgressSnapshot
    {
        public int ActionCode = -1;
        public long SectorsDone = 0;
        public long SectorsTotal = 0;
        public int ElapsedSeconds = -1;
        public int RemainingSeconds = -1;
        public int TotalSeconds = -1;
        public int Updates = 0;
        public int OffThreadCalls = 0;
        public string LastError = "";
    }

    // The listener the engine talks to. It records and it does nothing else.
    //
    // It used to hand each report straight to a scriptblock, which looked
    // obvious and could never have worked: the engine reports from a thread of
    // its own, and PowerShell cannot run a scriptblock on a thread that has no
    // runspace. Every report died on "There is no Runspace available to run
    // scripts in this thread", the catch below swallowed it so the burn would
    // not suffer for a bar, and the window sat at nought per cent for the whole
    // write. So nothing here calls out. The window reads these fields from its
    // own thread, which is free because the write happens on another one.
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public class BurnProgressSink : IBurnEvents
    {
        readonly object gate = new object();

        // Which thread built this, so a report arriving on a different one is a
        // fact in the log rather than a guess. On the machine this was written
        // for, every single report arrives on another thread.
        public readonly int OwnerThreadId;
        public int LastCallThreadId = 0;
        public int OffThreadCalls = 0;

        // Set by the hook, read after the write: whether there was ever going
        // to be a bar, and why not.
        public bool Attached = false;
        public string AttachWhy = "";

        public int ActionCode = -1;
        public long SectorsDone = 0;
        public long SectorsTotal = 0;
        public int ElapsedSeconds = -1;
        public int RemainingSeconds = -1;
        public int TotalSeconds = -1;
        public int Updates = 0;
        public string LastError = "";

        public BurnProgressSink()
        {
            OwnerThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public void Update(object sender, object progress)
        {
            // A disc is being written while this runs. Nothing in here may
            // throw back into the engine, and nothing in here may need a
            // particular thread to be on.
            try
            {
                int action = ToInt(Read(progress, "CurrentAction"), -1);
                long start = ToLong(Read(progress, "StartLba"), 0);
                long last = ToLong(Read(progress, "LastWrittenLba"), 0);
                long total = ToLong(Read(progress, "SectorCount"), 0);
                // Counted from where this write began rather than from the
                // start of the disc: a write that appends starts partway in.
                long done = last - start;
                if (done < 0) { done = 0; }
                if (total > 0 && done > total) { done = total; }
                int elapsed = ToInt(Read(progress, "ElapsedTime"), -1);
                int remaining = ToInt(Read(progress, "RemainingTime"), -1);
                int whole = ToInt(Read(progress, "TotalTime"), -1);
                int tid = Thread.CurrentThread.ManagedThreadId;
                lock (gate)
                {
                    ActionCode = action;
                    SectorsDone = done;
                    SectorsTotal = total;
                    ElapsedSeconds = elapsed;
                    RemainingSeconds = remaining;
                    TotalSeconds = whole;
                    LastCallThreadId = tid;
                    if (tid != OwnerThreadId) { OffThreadCalls++; }
                    Updates++;
                }
            }
            catch (Exception ex)
            {
                lock (gate) { LastError = "reading progress: " + ex.Message; }
            }
        }

        public BurnProgressSnapshot Snapshot()
        {
            BurnProgressSnapshot s = new BurnProgressSnapshot();
            lock (gate)
            {
                s.ActionCode = ActionCode;
                s.SectorsDone = SectorsDone;
                s.SectorsTotal = SectorsTotal;
                s.ElapsedSeconds = ElapsedSeconds;
                s.RemainingSeconds = RemainingSeconds;
                s.TotalSeconds = TotalSeconds;
                s.Updates = Updates;
                s.OffThreadCalls = OffThreadCalls;
                s.LastError = LastError;
            }
            return s;
        }

        // Late bound on purpose. The event argument is an IDispatch, and naming
        // its interface would mean declaring every member of it; asking for one
        // property by name costs nothing, and a property this build has never
        // heard of comes back null instead of throwing.
        static object Read(object o, string name)
        {
            if (o == null) { return null; }
            try { return o.GetType().InvokeMember(name, BindingFlags.GetProperty, null, o, null); }
            catch { return null; }
        }
        static int ToInt(object v, int fallback)
        { try { return v == null ? fallback : Convert.ToInt32(v); } catch { return fallback; } }
        static long ToLong(object v, long fallback)
        { try { return v == null ? fallback : Convert.ToInt64(v); } catch { return fallback; } }
    }

    public class BurnProgressHook
    {
        IConnectionPoint point;
        int cookie;
        public BurnProgressSink Sink;
        public string Why = "";
        public bool Ok { get { return cookie != 0; } }

        // The sink is made by the caller and passed in, because it outlives
        // this: the window reads it from one thread while the engine fills it
        // from another, and the hook itself lives and dies with the write.
        public static BurnProgressHook Attach(object format, BurnProgressSink sink)
        {
            BurnProgressHook h = new BurnProgressHook();
            h.Sink = sink;
            try
            {
                if (sink == null) { h.Why = "no listener was made"; return h; }
                IConnectionPointContainer c = format as IConnectionPointContainer;
                if (c == null) { h.Why = "the burn engine offers no events at all"; return h; }
                Guid iid = typeof(IBurnEvents).GUID;
                IConnectionPoint cp;
                c.FindConnectionPoint(ref iid, out cp);
                if (cp == null) { h.Why = "no connection point for " + iid.ToString("B"); return h; }
                int ck;
                cp.Advise(sink, out ck);
                if (ck == 0) { h.Why = "the engine took the listener and handed back no cookie"; return h; }
                h.point = cp;
                h.cookie = ck;
            }
            catch (Exception ex) { h.Why = ex.GetType().Name + ": " + ex.Message; }
            finally
            {
                if (sink != null) { sink.Attached = h.Ok; sink.AttachWhy = h.Why; }
            }
            return h;
        }

        // Called from a finally, so it says nothing and refuses to throw. The
        // engine holds the sink until it is dropped, and a sink left advised on
        // a format object that gets reused would report a second burn into the
        // first burn's window.
        public void Detach()
        {
            try { if (point != null && cookie != 0) { point.Unadvise(cookie); } }
            catch { }
            cookie = 0;
            point = null;
        }
    }
}
'@
}

<#
    A listener for one burn, ready to be attached.

    Made on the thread that will watch, not on the thread that will write: it
    remembers which one that was, so the log can say afterwards whether the
    engine reported from somewhere else.
#>
function New-BurnProgressSink {
    Add-BurnProgressType
    return New-Object DiscWright.BurnProgressSink
}

<#
    Start listening to a burn.

    Returns the hook whether or not it worked: .Ok says which and .Why says why
    not, and both are copied onto the sink so the watching thread can read them
    without holding the hook. A drive that will not report progress is a worse
    burn to sit through, never a reason to refuse to burn.
#>
function Register-BurnProgress {
    param(
        [Parameter(Mandatory)]$Format,
        [Parameter(Mandatory)]$Sink
    )
    Add-BurnProgressType
    return [DiscWright.BurnProgressHook]::Attach($Format, $Sink)
}

<#
    Run a scriptblock on a thread of its own, with named variables set on it.

    STA because everything it is used for here is COM, and IMAPI in particular
    wants an apartment it can call home. ReuseThread because the whole script
    has to run on ONE thread: a COM object made on one thread and used from
    another is a different and much worse problem than the one this solves.

    The runspace starts empty. Nothing from this file is in it, which is the
    point: the script it runs can only use what it is handed.
#>
function Start-BurnRunspace([scriptblock]$Script, [hashtable]$Variables) {
    $rs = [runspacefactory]::CreateRunspace()
    $rs.ApartmentState = 'STA'
    $rs.ThreadOptions = 'ReuseThread'
    $rs.Open()
    if ($Variables) {
        foreach ($k in $Variables.Keys) { $rs.SessionStateProxy.SetVariable($k, $Variables[$k]) }
    }
    $ps = [powershell]::Create()
    $ps.Runspace = $rs
    $null = $ps.AddScript($Script.ToString())
    return [pscustomobject]@{ Shell = $ps; Runspace = $rs; Async = $ps.BeginInvoke() }
}

<#
    Wait for one of those to finish, and answer for it.

    Whatever it threw is thrown again here, on the thread that asked, because a
    write that failed on another thread is still a write that failed. Warnings
    are repeated rather than lost: the speed the drive refused is said there.
#>
function Wait-BurnRunspace {
    [CmdletBinding()]
    param($Job)
    if (-not $Job) { return @() }
    try { return $Job.Shell.EndInvoke($Job.Async) }
    catch {
        # EndInvoke wraps whatever the other thread threw inside a sentence of
        # its own about calling EndInvoke. That sentence ends up in the dialog
        # that tells somebody their disc failed, so it is unwrapped back to
        # what actually went wrong before it goes any further.
        $real = $_.Exception
        while ($real.InnerException) { $real = $real.InnerException }
        throw $real
    }
    finally {
        foreach ($w in @($Job.Shell.Streams.Warning)) { Write-Warning $w.Message }
    }
}

# Idempotent, because it is called from a finally that may run after a throw.
function Close-BurnRunspace($Job) {
    if (-not $Job) { return }
    try { $Job.Shell.Dispose() } catch { Write-Verbose "The burn shell would not close: $($_.Exception.Message)" }
    try { $Job.Runspace.Dispose() } catch { Write-Verbose "The burn runspace would not close: $($_.Exception.Message)" }
}

<#
    Watch a sink being filled, and report what is in it, until the write ends.

    This runs on the thread that owns the window, which is free the whole time
    because the write is somewhere else. Every report therefore happens where
    the controls can actually be touched, and the sleep between them is what
    keeps the bar from costing the burn anything.

    Started is passed in so the window has an elapsed time to show even before
    the drive says anything, which on some drives is never.
#>
function Watch-BurnProgress {
    param(
        [Parameter(Mandatory)]$Sink,
        [Parameter(Mandatory)][scriptblock]$Running,
        [scriptblock]$OnProgress,
        [int]$IntervalMs = 250,
        [datetime]$Started = (Get-Date)
    )
    $reports = 0
    while (& $Running) {
        if ($OnProgress) {
            $snap = $Sink.Snapshot()
            $mine = [int]((Get-Date) - $Started).TotalSeconds
            $p = ConvertTo-BurnProgress $snap $mine
            & $OnProgress $p.Percent $p.Phase $p.Detail
            $reports++
        }
        Start-Sleep -Milliseconds $IntervalMs
    }
    return $reports
}

# What runs on the other thread. Kept here as one piece rather than inline in
# Write-IsoToDisc so that all of it can be read at once, and so it is obvious
# that it closes over nothing: the runspace it lands in has none of these
# functions and none of these variables except the ones handed to it by name.
$script:BurnWriteScript = {
    $recorder = New-Object -ComObject 'IMAPI2.MsftDiscRecorder2'
    $recorder.InitializeDiscRecorder($RecorderId)
    $fmt = New-Object -ComObject 'IMAPI2.MsftDiscFormat2Data'
    $fmt.Recorder = $recorder
    $fmt.ClientName = 'DiscWright'
    $fmt.ForceMediaToBeClosed = $CloseMedia
    if ($SpeedSectors -gt 0) {
        try { $fmt.SetWriteSpeed($SpeedSectors, $false) }
        catch {
            Write-Warning ("The drive would not take $SpeedText ($SpeedSectors sectors/s), so its own " +
                           "choice is used. It offers: $Speeds")
        }
    }

    # Attached from in here, on the thread that will do the writing, so the
    # engine never has to reach across an apartment to deliver a report.
    $hook = $HookType::Attach($fmt, $Sink)

    # ADODB.Stream is the way to hand an existing file to IMAPI as an IStream
    # from PowerShell. Type 1 is binary; without it the ISO is read as text and
    # arrives corrupted.
    $stream = New-Object -ComObject ADODB.Stream
    $stream.Open()
    $stream.Type = 1
    $stream.LoadFromFile($IsoPath)
    try {
        $fmt.Write($stream)
    } finally {
        $hook.Detach()
        $stream.Close()
        # Eject so the next step reads a freshly mounted volume rather than
        # whatever Windows cached about the blank that used to be in there.
        try { $recorder.EjectMedia() }
        catch { Write-Warning "The drive would not eject: $($_.Exception.Message)" }
    }
}

# The eight phases a data burn reports, in the words this application uses.
# Values 0 to 7 of _IMAPI_FORMAT2_DATA_WRITE_ACTION, read off the typelib rather
# than remembered, and an unknown code is reported as itself: a drive that
# invents a ninth phase must not make the window claim it is writing.
function Get-BurnPhaseName([int]$code) {
    switch ($code) {
        0 { 'checking the disc' }
        1 { 'formatting the disc' }
        2 { 'getting the drive ready' }
        3 { 'setting the laser power' }
        4 { 'writing' }
        5 { 'closing the disc' }
        6 { 'finished' }
        7 { 'the drive is reading back what it wrote' }
        default { "step $code" }
    }
}

# mm:ss, or h:mm:ss once a burn runs past the hour, which a BD-R XL does.
# Floor rather than a cast, for the reason Format-Elapsed carries in the app: a
# cast rounds, and a clock that rounds up reads a second ahead of itself.
function Format-BurnClock([int]$seconds) {
    if ($seconds -lt 0) { return '' }
    $t = [TimeSpan]::FromSeconds($seconds)
    if ($t.TotalHours -ge 1) {
        return '{0}:{1:00}:{2:00}' -f [int][Math]::Floor($t.TotalHours), $t.Minutes, $t.Seconds
    }
    return '{0:00}:{1:00}' -f [int][Math]::Floor($t.TotalMinutes), $t.Seconds
}

<#
    One report from the drive, turned into what a window needs.

    Named and returned rather than computed inside the sink's own callback,
    because the callback only runs during a real burn and this is the part worth
    testing: a percentage that counts from the wrong place, or goes over a
    hundred, or reads as nought when the drive has not said anything, is a bug
    nobody would see until they were watching a disc being written.

    Percent is -1 for "the drive is not saying", which is not the same fact as
    nought per cent and must not be drawn the same way.
#>
function ConvertTo-BurnProgress($sink, [int]$fallbackElapsedSeconds = -1) {
    $pct = -1
    if ($sink.SectorsTotal -gt 0) {
        $pct = [int](100.0 * $sink.SectorsDone / $sink.SectorsTotal)
        if ($pct -gt 100) { $pct = 100 }
        if ($pct -lt 0) { $pct = 0 }
    }
    return [pscustomobject]@{
        Percent = $pct
        # Before the first report there is no phase to name. Naming it anyway
        # put "step -1" on the window for the first quarter second of a burn.
        Phase   = $(if ($sink.Updates -gt 0) { Get-BurnPhaseName $sink.ActionCode }
                    else { Get-BurnPhaseName 2 })
        Detail  = [pscustomobject]@{
            ActionCode       = $sink.ActionCode
            SectorsDone      = $sink.SectorsDone
            SectorsTotal     = $sink.SectorsTotal
            BytesDone        = [long]$sink.SectorsDone * $script:BurnSectorBytes
            BytesTotal       = [long]$sink.SectorsTotal * $script:BurnSectorBytes
            ElapsedSeconds   = $(if ($sink.ElapsedSeconds -ge 0) { $sink.ElapsedSeconds }
                                 else { $fallbackElapsedSeconds })
            RemainingSeconds = $sink.RemainingSeconds
            TotalSeconds     = $sink.TotalSeconds
            Updates          = $sink.Updates
        }
    }
}

<#
    The clock line that goes under a progress bar.

    Kept apart from the drawing so the wording can be tested without a window,
    and so both halves of a burn, the write and the read back, say it the same
    way. The drive's own estimate is used wherever there is one: it knows about
    its buffer and the speed it actually settled on, which no arithmetic here
    does.
#>
function Format-BurnProgressLine([int]$elapsedSeconds, [int]$remainingSeconds) {
    $parts = @()
    if ($elapsedSeconds -ge 0) { $parts += 'Elapsed ' + (Format-BurnClock $elapsedSeconds) }
    if ($remainingSeconds -gt 0) { $parts += 'about ' + (Format-BurnClock $remainingSeconds) + ' left' }
    if (-not $parts.Count) { return 'The drive is not saying how far along it is.' }
    return ($parts -join ', ')
}

<#
    Write an ISO to the disc in a recorder.

    SupportsShouldProcess, and it means it: -WhatIf runs every check and writes
    nothing, which is how this gets exercised without spending a disc. A CD-R is
    written once, so the checks come first and any of them refuses outright.

    The ISO is handed over as a stream rather than rebuilt, because DiscWright
    has already built it and burning a different image than the one that was
    tested would make the test worthless.
#>
function Write-IsoToDisc {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
    param(
        [Parameter(Mandatory)][string]$IsoPath,
        # Which recorder, by drive letter. Named rather than assumed: a machine
        # with two drives would otherwise burn to whichever came back first.
        [string]$Drive,
        # Either a multiple from the disc's own label, like '8x', or KB/s. Left
        # alone, the drive picks, which is usually its fastest.
        [string]$Speed,
        # Close the disc so it reads everywhere. A disc left open reads on the
        # machine that wrote it and can confuse older drives, which is exactly
        # the audience for a game on a CD.
        [bool]$CloseMedia = $true,
        # Called with (percent, phase, detail) as the drive reports in, about
        # once a second. Percent is -1 when the drive will not say how far along
        # it is, which is a different thing from nought per cent and has to look
        # different on screen. Detail carries the sector counts and the drive's
        # own time estimates, and is $null for the two bookend calls.
        [scriptblock]$OnProgress
    )
    if (-not (Test-Path -LiteralPath $IsoPath)) { throw "No ISO at '$IsoPath'" }
    $iso = Get-Item -LiteralPath $IsoPath
    if ($iso.Length -le 0) { throw "'$IsoPath' is empty" }

    $burners = Get-BurnerInfo
    if (-not $burners.Count) { throw 'No disc recorder on this machine.' }

    $target = if ($Drive) {
        $want = $Drive.TrimEnd('\', ':') + ':'
        $burners | Where-Object { $_.Drive -like "$want*" } | Select-Object -First 1
    } else {
        $burners | Select-Object -First 1
    }
    if (-not $target) {
        throw ("No recorder on $Drive. This machine has: " +
               (($burners | ForEach-Object { $_.Drive }) -join ', '))
    }

    if (-not $target.Ready) {
        throw "The disc in $($target.Drive) cannot be written: $($target.Why)."
    }

    $fit = Test-IsoFitsMedia -IsoBytes $iso.Length -FreeSectors $target.FreeSectors
    if (-not $fit.Fits) {
        throw ("The ISO needs $([Math]::Round($fit.IsoBytes / 1MB, 1)) MB and the " +
               "$($target.MediaName) in $($target.Drive) has " +
               "$([Math]::Round($fit.FreeBytes / 1MB, 1)) MB. Nothing was written.")
    }

    $what = "$($iso.Name), $([Math]::Round($iso.Length / 1MB, 1)) MB, onto the $($target.MediaName)"
    # "elsewhere" was true when nothing here could erase. It is not any more.
    $undo = $(if (Test-MediaRewritable $target.MediaType) {
                  'The disc is rewritable, so it can be erased and written again.'
              } else { 'This cannot be undone: the disc is not rewritable.' })
    if (-not $PSCmdlet.ShouldProcess("$($target.Drive) ($($target.Vendor) $($target.Product))",
                                     "Burn $what. $undo")) {
        return [pscustomobject]@{
            Burned = $false; WhatIf = $true; Drive = $target.Drive
            MediaName = $target.MediaName; IsoPath = $iso.FullName
            IsoBytes = $iso.Length; Fit = $fit
        }
    }

    # From here on the write belongs to another thread, and this one does
    # nothing but watch. That is not a nicety: the engine reports progress from
    # a thread of its own, PowerShell cannot run a scriptblock on a thread that
    # has no runspace, and a thread sitting inside Write stops pumping messages
    # so nothing posted to it gets painted either. Writing over there and
    # watching from here is the only arrangement in which the bar can move at
    # all, and it is why the main window no longer freezes for ten minutes.
    $speedSectors = 0
    if ($Speed) { $speedSectors = ConvertTo-WriteSpeedSectors -Speed $Speed -MediaType $target.MediaType }

    $sink = New-BurnProgressSink
    $started = Get-Date
    $job = Start-BurnRunspace $script:BurnWriteScript @{
        RecorderId = $target.Id
        IsoPath    = $iso.FullName
        SpeedSectors = $speedSectors
        CloseMedia = $CloseMedia
        Sink       = $sink
        HookType   = [DiscWright.BurnProgressHook]
        SpeedText  = $(if ($Speed) { $Speed } else { "the drive's own choice" })
        Speeds     = (($target.Speeds | ForEach-Object { "$($_.Multiple)x" }) -join ', ')
    }
    try {
        if ($OnProgress) { & $OnProgress 0 (Get-BurnPhaseName 2) $null }
        $null = Watch-BurnProgress -Sink $sink -OnProgress $OnProgress -Started $started `
                                   -Running { -not $job.Async.IsCompleted }
        # Throws here if the write threw over there, which is the whole reason
        # this is not fire and forget.
        $null = Wait-BurnRunspace $job
        if ($OnProgress) { & $OnProgress 100 (Get-BurnPhaseName 6) $null }
    } finally {
        Close-BurnRunspace $job
    }

    return [pscustomobject]@{
        Burned    = $true
        WhatIf    = $false
        Drive     = $target.Drive
        MediaName = $target.MediaName
        IsoPath   = $iso.FullName
        IsoBytes  = $iso.Length
        Fit       = $fit
        Seconds   = [int]((Get-Date) - $started).TotalSeconds
        # Whether this burn could be watched, how many times the drive said
        # anything, and whether it said it from somewhere else. A burn that
        # reports nothing is worth knowing about after the fact, because the
        # alternative is guessing why the window sat still.
        Watched   = [bool]$sink.Attached
        Updates   = $sink.Updates
        OffThread = $sink.OffThreadCalls
        AttachWhy = $sink.AttachWhy
        SinkError = $sink.LastError
    }
}

# What runs on the other thread. Same shape as BurnWriteScript and for the same
# reason: EraseMedia blocks until the drive has finished, and on the UI thread
# that is a window Windows paints over with "not responding" for most of a
# minute.
#
# No eject afterwards, deliberately. The write path ejects so that the check
# that follows reads a freshly mounted volume; nothing reads this disc through
# the filesystem before it is written, and IMAPI asks the drive rather than
# Windows whether the disc is blank. Ejecting here would only mean the tray is
# open when the burn wants it shut.
$script:BurnEraseScript = {
    $recorder = New-Object -ComObject 'IMAPI2.MsftDiscRecorder2'
    $recorder.InitializeDiscRecorder($RecorderId)
    $e = New-Object -ComObject 'IMAPI2.MsftDiscFormat2Erase'
    if (-not $e.IsRecorderSupported($recorder)) { throw 'This drive cannot erase a disc.' }
    $e.Recorder = $recorder
    $e.ClientName = 'DiscWright'
    # Quick, not full. Full rewrites the whole surface and takes twenty minutes
    # on a CD-RW; quick blanks the table of contents and the drive will happily
    # write the whole disc afterwards. Full is for a disc being given away with
    # something private on it, which is not what this button is for.
    $e.FullErase = $Full
    $e.EraseMedia()
}

<#
    Erase a rewritable disc, having been told to.

    Nothing here decides that it should happen. Get-EraseOffer answers whether
    there is a disc worth offering to erase, the caller asks, and this does it.

    Returns @{ Ok; Seconds; Why } and never throws, because every caller is in
    the middle of a sequence of dialogs and an exception there is a sequence
    that stops with nothing said.

    $OnProgress is called with the elapsed seconds while it runs. There is no
    percentage: IDiscFormat2Erase reports through an event interface of its own
    and the sink here is built for the data writer, so rather than pretend to a
    bar, the window counts the seconds. A quick erase is under a minute.
#>
function Clear-DiscMedia {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Target,
        [switch]$Full,
        [scriptblock]$OnProgress
    )
    if (-not $Target -or -not $Target.Id) {
        return @{ Ok = $false; Seconds = 0; Why = 'No drive was given to erase.' }
    }
    $started = Get-Date
    $job = $null
    try {
        $job = Start-BurnRunspace $script:BurnEraseScript @{
            RecorderId = $Target.Id
            Full       = [bool]$Full
        }
        while (-not $job.Async.IsCompleted) {
            if ($OnProgress) { & $OnProgress ([int]((Get-Date) - $started).TotalSeconds) }
            Start-Sleep -Milliseconds 400
        }
        $null = Wait-BurnRunspace $job
        return @{ Ok = $true; Seconds = [int]((Get-Date) - $started).TotalSeconds; Why = 'erased' }
    } catch {
        return @{ Ok = $false
                  Seconds = [int]((Get-Date) - $started).TotalSeconds
                  Why = $_.Exception.Message }
    } finally {
        if ($job) { Close-BurnRunspace $job }
    }
}

<#
    The SHA-256 of a file, reported on as it is read.

    Get-FileHash reads the whole file and says nothing until it has finished.
    On a cut set that is four and a half minutes of silence: the disc is one
    enormous part file beside a handful of small ones, so a bar fed only when a
    file completes has exactly two positions, nought and a hundred. Noticed on
    11 October while watching a real check, after the byte weighting had
    already fixed the easier version of the same complaint. Weighting by bytes
    moves the bar between files. It cannot help when one file IS the disc.

    Reads in chunks and hands back how far it has got, so the window has
    something to draw every second or so.

    The message on a read failure is built here and matches what Get-FileHash
    said, because that wording is what is printed on the dialog somebody reads
    when their disc has failed, and it names the file.
#>
function Get-FileSha256Watched {
    param(
        [Parameter(Mandatory)][string]$Path,
        [scriptblock]$OnRead,
        [int]$ChunkBytes = 4MB
    )
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $fs = $null
    try {
        $fs = [System.IO.File]::Open($Path, 'Open', 'Read', 'Read')
        $buf = New-Object byte[] $ChunkBytes
        $read = [long]0
        while ($true) {
            $n = $fs.Read($buf, 0, $buf.Length)
            if ($n -le 0) { break }
            $null = $sha.TransformBlock($buf, 0, $n, $null, 0)
            $read += $n
            if ($OnRead) { & $OnRead $read }
        }
        $null = $sha.TransformFinalBlock((New-Object byte[] 0), 0, 0)
        return ((($sha.Hash | ForEach-Object { $_.ToString('x2') }) -join '').ToUpperInvariant())
    }
    catch {
        throw "The file '$Path' cannot be read: $($_.Exception.Message)"
    }
    finally {
        if ($fs) { $fs.Dispose() }
        $sha.Dispose()
    }
}

<#
    Compare what is on a disc against the folder it was built from.

    Every file hashed on both sides, because a burn that finishes without an
    error is not the same thing as a disc that holds the right bytes. This is
    the only check that would catch a drive writing something subtly wrong, and
    it is the whole reason for burning a test disc at all.
#>
function Test-BurnedDisc {
    param(
        [Parameter(Mandatory)][string]$DiscRoot,
        [Parameter(Mandatory)][string]$StagingFolder,
        [switch]$SkipHashes,
        [scriptblock]$OnProgress
    )
    foreach ($p in $DiscRoot, $StagingFolder) {
        if (-not (Test-Path -LiteralPath $p)) { throw "Nothing at '$p'" }
    }

    function Get-RelativeMap([string]$root) {
        $map = @{}
        $full = (Resolve-Path -LiteralPath $root).Path.TrimEnd('\')
        foreach ($f in Get-ChildItem -LiteralPath $full -Recurse -File -Force) {
            $rel = $f.FullName.Substring($full.Length).TrimStart('\')
            $map[$rel] = $f
        }
        return $map
    }

    $onDisc = Get-RelativeMap $DiscRoot
    $source = Get-RelativeMap $StagingFolder

    # A callback, because reading a disc back is slow and silence for a minute
    # looks like a hang. This used to add that the burn itself could not do the
    # same, being one blocking call inside the drive. It can: the drive reports
    # through an event sink, which is what Register-BurnProgress is for. Both
    # halves of a burn can be watched now, so both of them report.
    $done = 0
    $total = $source.Count
    # Bytes as well as files, because a set is one enormous part file beside a
    # handful of small ones: four files out of nine can be 2 per cent of the
    # disc, and a bar counting files sits at 44 per cent for four minutes.
    $bytesDone = [long]0
    $bytesTotal = [long]0
    foreach ($f in $source.Values) { $bytesTotal += $f.Length }
    $missing = @($source.Keys | Where-Object { -not $onDisc.ContainsKey($_) } | Sort-Object)
    $extra   = @($onDisc.Keys  | Where-Object { -not $source.ContainsKey($_) } | Sort-Object)
    $sizeOff = @()
    $hashOff = @()
    $unread  = @()

    foreach ($rel in ($source.Keys | Where-Object { $onDisc.ContainsKey($_) } | Sort-Object)) {
        # Counted before the checks rather than after them, so a file that comes
        # out wrong still moves the bar. Counting only the good ones left a bar
        # that stopped short of the end on exactly the discs worth watching.
        $done++
        # What was finished before this file started, so the reports coming out
        # of the hash below can be added to it rather than guessed at.
        $before = $bytesDone
        $bytesDone += $source[$rel].Length
        if ($source[$rel].Length -ne $onDisc[$rel].Length) {
            $sizeOff += "$rel ($($source[$rel].Length) built, $($onDisc[$rel].Length) on disc)"
            if ($OnProgress) { & $OnProgress $done $total $rel $bytesDone $bytesTotal }
            continue
        }
        if (-not $SkipHashes) {
            # A disc that will not read is the thing this check exists to catch,
            # so it must not be the thing that kills the check. A CD-RW burned on
            # 10 October held a 728 MB file the drive answered with "Data error
            # (cyclic redundancy check)". Get-FileHash writes an error and
            # returns nothing, .Hash on nothing throws, and the whole run ended
            # on a stack trace partway through the disc, having said not one word
            # about which file or whether the rest were any good.
            #
            # Unreadable is a verdict, not an accident. It is kept apart from
            # WrongContent because they are different facts: one file came back
            # as different bytes, the other did not come back at all.
            $a = $null; $b = $null
            # The staged side is on a hard disc and takes seconds, so it is not
            # worth reporting on. The disc side is the one somebody is watching.
            try { $a = Get-FileSha256Watched $source[$rel].FullName }
            catch { $unread += "$rel (as built: $($_.Exception.Message))" }
            if ($null -ne $a) {
                try {
                    $b = Get-FileSha256Watched $onDisc[$rel].FullName {
                             param($read)
                             if ($OnProgress) { & $OnProgress $done $total $rel ($before + $read) $bytesTotal }
                         }
                }
                catch { $unread += "$rel ($($_.Exception.Message))" }
            }
            if ($null -ne $a -and $null -ne $b -and $a -ne $b) { $hashOff += $rel }
        }
        if ($OnProgress) { & $OnProgress $done $total $rel $bytesDone $bytesTotal }
    }

    return [pscustomobject]@{
        FilesOnDisc   = $onDisc.Count
        FilesExpected = $source.Count
        Missing       = $missing
        Unexpected    = $extra
        WrongSize     = $sizeOff
        WrongContent  = $hashOff
        Unreadable    = $unread
        Ok            = (-not $missing.Count -and -not $extra.Count -and
                         -not $sizeOff.Count -and -not $hashOff.Count -and
                         -not $unread.Count)
    }
}

# --- handing the ISO to something else ----------------------------------------
#
# Asked for as "being able to link your software to ImgBurn or similar software
# for an instant burn". Not ImgBurn in particular, and nothing to configure:
# Windows already knows what can open an ISO, and the answer differs per machine.
# On the machine this was written on it is Nero, which is a decent argument
# against hardcoding anybody's favourite.
#
# Windows.IsoFile always carries burn and mount, whatever else is installed, so
# there is always something here even on a machine with nothing added.

function Get-ExeFriendlyName([string]$exe) {
    # What the program calls itself, which is better than a path and better than
    # a registry key: "Nero Burning ROM" rather than Nero.BurningROM.2023.iso.1.
    if (-not $exe -or -not (Test-Path -LiteralPath $exe)) { return '' }
    try {
        $v = (Get-Item -LiteralPath $exe).VersionInfo
        foreach ($n in @($v.FileDescription, $v.ProductName)) {
            if ($n -and $n.Trim()) { return $n.Trim() }
        }
    } catch { }
    return [IO.Path]::GetFileNameWithoutExtension($exe)
}

function Get-ShellCommand([string]$key) {
    try { return (Get-ItemProperty -LiteralPath $key -EA Stop).'(default)' } catch { return '' }
}

function Split-ShellCommand([string]$cmd) {
    # A shell command is an exe and its arguments, with %1 where the file goes.
    # Quoted path first, bare path otherwise.
    if (-not $cmd) { return $null }
    $m = [regex]::Match($cmd, '^\s*"([^"]+)"\s*(.*)$')
    if (-not $m.Success) { $m = [regex]::Match($cmd, '^\s*(\S+)\s*(.*)$') }
    if (-not $m.Success) { return $null }
    return @{ Exe = $m.Groups[1].Value; Args = $m.Groups[2].Value.Trim() }
}

function Get-IsoHandoffs {
    <#  .SYNOPSIS
        The programs on this machine that can take an ISO, as Windows records
        them. Nothing is configured and nothing is guessed: each entry comes
        from a registry key that exists.  #>
    $out = @()

    # Windows' own, which is on every machine since 7.
    $burn = Split-ShellCommand (Get-ShellCommand 'HKLM:\SOFTWARE\Classes\Windows.IsoFile\shell\burn\command')
    if ($burn -and (Test-Path -LiteralPath $burn.Exe)) {
        $out += @{ Name = (Get-ExeFriendlyName $burn.Exe)
                   What = 'writes it to a blank disc'
                   Exe = $burn.Exe; Args = $burn.Args; Verb = '' }
    }

    # Whatever the machine has registered for .iso, when it is something else.
    $progid = ''
    foreach ($k in @('HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.iso\UserChoice',
                     'HKLM:\SOFTWARE\Classes\.iso')) {
        try {
            $p = Get-ItemProperty -LiteralPath $k -EA Stop
            $v = if ($p.PSObject.Properties.Name -contains 'ProgId') { $p.ProgId } else { $p.'(default)' }
            if ($v) { $progid = $v; break }
        } catch { }
    }
    if ($progid -and $progid -ne 'Windows.IsoFile') {
        $open = Split-ShellCommand (Get-ShellCommand "HKLM:\SOFTWARE\Classes\$progid\shell\Open\command")
        if (-not $open) { $open = Split-ShellCommand (Get-ShellCommand "HKLM:\SOFTWARE\Classes\$progid\shell\open\command") }
        if ($open -and (Test-Path -LiteralPath $open.Exe) -and
            ($out | Where-Object { $_.Exe -ieq $open.Exe }).Count -eq 0) {
            $out += @{ Name = (Get-ExeFriendlyName $open.Exe)
                       What = 'opens it, and burns it its own way'
                       Exe = $open.Exe; Args = $open.Args; Verb = '' }
        }
    }

    # Not burning, and worth having anyway: look inside the disc without
    # spending one. Explorer's own, so it is listed last and labelled honestly.
    if (Get-ShellCommand 'HKLM:\SOFTWARE\Classes\Windows.IsoFile\shell\mount\command') {
        $out += @{ Name = 'Windows Explorer'
                   What = 'mounts it as a drive, without burning anything'
                   Exe = ''; Args = ''; Verb = 'mount' }
    }
    # Returned plain, not as ,@($out). Wrapping it hands the caller one
    # element that is itself the list, and the chooser then offers a single
    # row of nonsense. Callers wrap with @() themselves.
    return $out
}

<#
    The same list, for a question that is specifically "which burner".

    On 10 October a two disc set was burned by handing both ISOs to the Windows
    Disc Image Burning Tool, because that is what ticking "disc set" offered.
    The disc came back unreadable in its outer third and the only thing that
    tool could say about it was 0x80004005. DiscWright had a burner of its own
    the whole time, with a speed it picks below the drive's maximum and a check
    that hashes every file afterwards, and it never put itself on the list.

    So it goes first, and the mount entry comes off. "Mounts it as a drive,
    without burning anything" is an honest label and a fine thing to offer
    somebody looking inside an ISO, but it is not an answer to "which program
    should burn these two discs", and a walk that accepted it would march on
    asking for disc 2 having burned nothing at all.
#>
function Get-SetBurnChoices {
    $out = @(@{ Name = 'DiscWright'
                What = 'writes it here, below the drive top speed, and checks every file afterwards'
                Exe  = ''; Args = ''; Verb = ''; Own = $true })
    foreach ($h in @(Get-IsoHandoffs)) {
        if ($h.Verb -eq 'mount') { continue }
        $out += $h
    }
    return $out
}

function Start-IsoHandoff([hashtable]$handoff, [string]$isoPath) {
    <#  .SYNOPSIS
        Hand the ISO over and return. The other program takes it from there:
        this does not drive somebody else's command line, because every burner
        spells its switches differently and the failure is a wasted disc.  #>
    if (-not (Test-Path -LiteralPath $isoPath)) { throw "There is no ISO at $isoPath" }
    if ($handoff.Verb) {
        Start-Process -FilePath $isoPath -Verb $handoff.Verb
        return
    }
    # %1 is where the shell puts the file. A command with no %1 at all still
    # gets the path, because that is what every one of them expects.
    if ($handoff.Args -and $handoff.Args -match '%1') {
        $argLine = $handoff.Args -replace '%1', $isoPath
        Start-Process -FilePath $handoff.Exe -ArgumentList $argLine
    } else {
        Start-Process -FilePath $handoff.Exe -ArgumentList @("`"$isoPath`"")
    }
}

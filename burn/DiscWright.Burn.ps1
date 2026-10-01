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

  It does not erase rewritable media, and it does not close or append to a
  multi-session disc. One ISO, one disc, written once.
#>

# IMAPI_MEDIA_PHYSICAL_TYPE. The numbers come back from the drive and mean
# nothing on their own, and a report that says "media type 9" is a report
# nobody can act on.
$script:MediaTypes = @{
    0  = @{ Name = 'no disc';                 Writable = $false }
    1  = @{ Name = 'CD-ROM, pressed';         Writable = $false }
    2  = @{ Name = 'CD-R';                    Writable = $true  }
    3  = @{ Name = 'CD-RW';                   Writable = $true  }
    4  = @{ Name = 'DVD-ROM, pressed';        Writable = $false }
    5  = @{ Name = 'DVD-RAM';                 Writable = $true  }
    6  = @{ Name = 'DVD+R';                   Writable = $true  }
    7  = @{ Name = 'DVD+RW';                  Writable = $true  }
    8  = @{ Name = 'DVD+R DL';                Writable = $true  }
    9  = @{ Name = 'DVD-R';                   Writable = $true  }
    10 = @{ Name = 'DVD-RW';                  Writable = $true  }
    11 = @{ Name = 'DVD-R DL';                Writable = $true  }
    12 = @{ Name = 'hard disk';               Writable = $false }
    13 = @{ Name = 'DVD+RW DL';               Writable = $true  }
    14 = @{ Name = 'HD DVD-ROM';              Writable = $false }
    15 = @{ Name = 'HD DVD-R';                Writable = $true  }
    16 = @{ Name = 'HD DVD-RAM';              Writable = $true  }
    17 = @{ Name = 'BD-ROM, pressed';         Writable = $false }
    18 = @{ Name = 'BD-R';                    Writable = $true  }
    19 = @{ Name = 'BD-RE';                   Writable = $true  }
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
function Get-SpeedBaseKb([int]$mediaType) {
    # CD-ROM, CD-R and CD-RW. Everything else in this table is a DVD or better.
    if ($mediaType -in 1, 2, 3) { return 150.0 }
    return 1385.0
}

function ConvertTo-SpeedMultiple([int]$kb, [int]$mediaType) {
    return [Math]::Round($kb / (Get-SpeedBaseKb $mediaType), 1)
}

<#
    Turn "8x" or "1199" into the KB/s the drive wants, for whatever is loaded.
    A multiple is what the disc is labelled with, so that is what a person has
    in their hand when they choose.
#>
function ConvertTo-WriteSpeedKb {
    param(
        [Parameter(Mandatory)][string]$Speed,
        [Parameter(Mandatory)][int]$MediaType
    )
    $t = $Speed.Trim()
    if ($t -match '^(?<n>[0-9]+(\.[0-9]+)?)\s*[xX]$') {
        return [int][Math]::Round([double]$Matches.n * (Get-SpeedBaseKb $MediaType))
    }
    if ($t -match '^[0-9]+$') { return [int]$t }
    throw "Cannot read '$Speed' as a write speed. Use a multiple like 8x, or KB/s like 1199."
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
        [Parameter(Mandatory)][int]$SpeedKb,
        [int]$MediaType = 2,
        [bool]$CloseMedia = $true
    )
    if ($SpeedKb -le 0) { return 0 }
    # Drives rarely hold their rated speed for the whole write, so this is
    # deliberately a little pessimistic. An estimate that runs under is worse
    # than one that runs over: the first looks like a hang.
    $write = $Bytes / ($SpeedKb * 1024.0) * 1.15
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
                Kb       = [int]$d.WriteSpeed
                Multiple = ConvertTo-SpeedMultiple ([int]$d.WriteSpeed) $mediaType
                PureCav  = [bool]$d.RotationTypeIsPureCAV
            }
        }
    } catch {
        # Some drives only answer with a blank disc loaded. An empty list is an
        # honest answer; a guessed one is not.
        Write-Verbose "The drive would not list its write speeds: $($_.Exception.Message)"
    }
    return , @($out | Sort-Object Kb -Descending)
}

function Get-MediaTypeName([int]$code) {
    if ($script:MediaTypes.ContainsKey($code)) { return $script:MediaTypes[$code].Name }
    return "unknown media type $code"
}

function Test-MediaWritable([int]$code) {
    if ($script:MediaTypes.ContainsKey($code)) { return [bool]$script:MediaTypes[$code].Writable }
    return $false
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
            $info.Why = 'the disc is not blank, and this never erases or appends'
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
    if (-not $PSCmdlet.ShouldProcess("$($target.Drive) ($($target.Vendor) $($target.Product))",
                                     "Burn $what. This cannot be undone and the disc is not rewritable")) {
        return [pscustomobject]@{
            Burned = $false; WhatIf = $true; Drive = $target.Drive
            MediaName = $target.MediaName; IsoPath = $iso.FullName
            IsoBytes = $iso.Length; Fit = $fit
        }
    }

    $recorder = New-Object -ComObject 'IMAPI2.MsftDiscRecorder2'
    $recorder.InitializeDiscRecorder($target.Id)
    $fmt = New-Object -ComObject 'IMAPI2.MsftDiscFormat2Data'
    $fmt.Recorder = $recorder
    $fmt.ClientName = 'DiscWright'
    $fmt.ForceMediaToBeClosed = $CloseMedia
    if ($Speed) {
        $kb = ConvertTo-WriteSpeedKb -Speed $Speed -MediaType $target.MediaType
        try { $fmt.SetWriteSpeed($kb, $false) }
        catch {
            Write-Warning ("The drive would not take $Speed ($kb KB/s), so its own choice is used. " +
                           "It offers: " + (($target.Speeds | ForEach-Object { "$($_.Multiple)x" }) -join ', '))
        }
    }

    # ADODB.Stream is the way to hand an existing file to IMAPI as an IStream
    # from PowerShell. Type 1 is binary; without it the ISO is read as text and
    # arrives corrupted.
    $stream = New-Object -ComObject ADODB.Stream
    $stream.Open()
    $stream.Type = 1
    $stream.LoadFromFile($iso.FullName)

    $started = Get-Date
    try {
        if ($OnProgress) { & $OnProgress 0 'starting' }
        $fmt.Write($stream)
        if ($OnProgress) { & $OnProgress 100 'done' }
    } finally {
        $stream.Close()
        # Eject so the next step reads a freshly mounted volume rather than
        # whatever Windows cached about the blank that used to be in there.
        try { $recorder.EjectMedia() }
        catch { Write-Verbose "The drive would not eject: $($_.Exception.Message)" }
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
    # looks like a hang. The burn itself cannot do this: it is one blocking call
    # inside the drive, and this loop is ours.
    $done = 0
    $total = $source.Count
    $missing = @($source.Keys | Where-Object { -not $onDisc.ContainsKey($_) } | Sort-Object)
    $extra   = @($onDisc.Keys  | Where-Object { -not $source.ContainsKey($_) } | Sort-Object)
    $sizeOff = @()
    $hashOff = @()

    foreach ($rel in ($source.Keys | Where-Object { $onDisc.ContainsKey($_) } | Sort-Object)) {
        if ($source[$rel].Length -ne $onDisc[$rel].Length) {
            $sizeOff += "$rel ($($source[$rel].Length) built, $($onDisc[$rel].Length) on disc)"
            continue
        }
        if (-not $SkipHashes) {
            $a = (Get-FileHash -LiteralPath $source[$rel].FullName -Algorithm SHA256).Hash
            $b = (Get-FileHash -LiteralPath $onDisc[$rel].FullName -Algorithm SHA256).Hash
            if ($a -ne $b) { $hashOff += $rel }
        }
        $done++
        if ($OnProgress) { & $OnProgress $done $total $rel }
    }

    return [pscustomobject]@{
        FilesOnDisc   = $onDisc.Count
        FilesExpected = $source.Count
        Missing       = $missing
        Unexpected    = $extra
        WrongSize     = $sizeOff
        WrongContent  = $hashOff
        Ok            = (-not $missing.Count -and -not $extra.Count -and
                         -not $sizeOff.Count -and -not $hashOff.Count)
    }
}

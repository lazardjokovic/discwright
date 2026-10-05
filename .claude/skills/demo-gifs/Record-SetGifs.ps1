<#
.SYNOPSIS
    Records the disc-set demonstrations by driving DiscWright, and writes the
    frames for Assemble.

.DESCRIPTION
    Three films, from one run of the application:

      refused  Hollow Knight at a CD. Its installer is 1.16 GB on its own and a
               CD leaves 0.68 GB, so the set is refused and the file is named.
               No build, so it is quick.

      build    Dead Space at a DVD5. 8.13 GB, which first fit lays onto two
               discs - the fewest possible, since the payload is 1.86 times
               what one disc holds. A real build, about three minutes, and the
               long part of the run.

      restore  The set that build just wrote, put back together: each disc's
               own menu copies its share into one folder, the status counts up,
               and the last disc completes the set and offers to install it.

    The restore film stops at the Install button rather than pressing it. That
    would start GOG's real installer, which is not what this demonstrates and
    would leave a half-installed game behind. It deletes the copies instead,
    which is the other half of the story.

    LEAVE THE MACHINE ALONE while it runs. It drives the real pointer and the
    real foreground window, and anything else that takes the foreground both
    stops the run and gets photographed.

.PARAMETER Only
    refused, build, restore, or all. restore needs a set on disk, so on its own
    it uses whatever the output folder already holds and does not clear it.
#>
param(
    [string]$DemoRoot  = 'F:\DWdemo',
    [string]$OutDir    = (Join-Path $env:TEMP 'discwright-gifs'),
    [string]$PrimeFrom = '',
    [ValidateSet('refused', 'build', 'restore', 'all')][string]$Only = 'all',
    [string]$AppPath = (Join-Path (Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent) 'DiscWright.ps1')
)
$ErrorActionPreference = 'Stop'
$SC   = $PSScriptRoot
$APP  = $AppPath
$D    = $DemoRoot
$repo = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
Import-Module (Join-Path $repo 'tests\ui\UiDriver.psm1') -Force
. "$SC\demolib.ps1"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# Step counts into the demo folder from its own node, measured with treemap.ps1.
# Re-measure after anything is added to or removed from the demo root: the tree
# is navigated by counting rows, so one new folder shifts every count below it.
$DOWN_HOLLOWKNIGHT = 4
$DOWN_DEADSPACE    = 3
# Primed with a game in neither film AND not already in the prime project, which
# holds Alan Wake. Picking one the form already has adds nothing, the count does
# not rise, and the run stops saying the step count missed it.
$DOWN_PRIME        = 9          # The Witcher
$PrimeGame         = 'The Witcher'

# Where the menu puts the game back. NOT chosen here: the menu proposes it
# itself, as the drive with the most room free plus the game's name, and on this
# machine that is the demo drive. So nothing on camera carries a username and no
# folder picker has to be opened, which matters because its tree shows a real
# name and every one of them is cut from the film.
$RESTORE = 'F:\DiscWright restore'
$out     = Join-Path $D 'out'

if (-not $PrimeFrom) {
    foreach ($candidate in "$D-prime", (Join-Path $D 'out-previous')) {
        if (Test-Path -LiteralPath (Join-Path $candidate 'discproject.json')) { $PrimeFrom = $candidate; break }
    }
}

# An output folder with a previous build in it makes the button read REBUILD
# ISO. Left alone when only the restore is being recorded, because that film
# needs the set already sitting in there.
if ($Only -ne 'restore') {
    if (Test-Path $out) {
        $kept = @(Get-ChildItem $out -Force -EA SilentlyContinue)
        if ($kept.Count) {
            $park = Join-Path "$D-previous-runs" (Get-Date -Format 'yyyyMMdd-HHmmss')
            New-Item -ItemType Directory -Force -Path $park | Out-Null
            $kept | ForEach-Object { Move-Item $_.FullName $park -Force }
            Write-Host ('  parked the previous build in ' + $park)
        }
    } else { New-Item -ItemType Directory -Force -Path $out | Out-Null }
}
if (Test-Path $RESTORE) { Remove-Item $RESTORE -Recurse -Force }

function Start-Capture([string]$Tag, $Region = $null) {
    $frames = Join-Path $OutDir "frames-$Tag"
    Remove-Item $frames -Recurse -Force -EA SilentlyContinue
    New-Item -ItemType Directory -Force -Path $frames | Out-Null
    $stop = Join-Path $OutDir "stop-$Tag"
    Remove-Item $stop -Force -EA SilentlyContinue
    # The assembler reads cuts-<tag>.txt from the folder ABOVE the frames. Put
    # it anywhere else and the cut is recorded and silently never applied, and
    # what is being cut is the folder dialog, whose tree carries a real name.
    Start-CutLog (Join-Path $OutDir "cuts-$Tag.txt")
    $pargs = @('-NoProfile','-ExecutionPolicy','Bypass','-File',
              "`"$SC\capture.ps1`"",'-OutDir',"`"$frames`"",'-StopFile',"`"$stop`"",'-IntervalMs','100')
    if ($Region) {
        $pargs += @('-RegionX', "$($Region.X)", '-RegionY', "$($Region.Y)",
                   '-RegionW', "$($Region.W)", '-RegionH', "$($Region.H)")
    }
    # Cleared before launching, or the check below reads the log the LAST run
    # left and passes on it - which it did, reporting a capture that was never
    # running.
    Remove-Item (Join-Path $OutDir "frames-$Tag.log") -Force -EA SilentlyContinue
    Start-Process 'powershell.exe' -ArgumentList $pargs -WindowStyle Hidden
    Start-Sleep -Seconds 2
    # Proof it is actually running. A capture that never starts leaves a film
    # of nothing, and every other number in the run still looks right.
    $log = Join-Path $OutDir "frames-$Tag.log"
    $ok = $false
    for ($i = 0; $i -lt 20; $i++) {
        if ((Test-Path $log) -and ((Get-Content $log -Raw) -match 'region ')) { $ok = $true; break }
        Start-Sleep -Milliseconds 500
    }
    if (-not $ok) { throw "the capture for $Tag never started - see $log" }
    return [pscustomobject]@{ Tag = $Tag; Frames = $frames; Stop = $stop }
}
function Stop-Capture($Cap) {
    if (-not $Cap) { return }
    New-Item -ItemType File -Path $Cap.Stop -Force | Out-Null
    Start-Sleep -Seconds 2
    $n = (Get-ChildItem $Cap.Frames -Filter *.png -EA SilentlyContinue | Measure-Object).Count
    Write-Host ('  [{0}] {1} frames' -f $Cap.Tag, $n)
}

$app = Start-DiscWright -AppPath $APP
$win = $app.Window
$wr  = $win.Current.BoundingRectangle
Beat 1.5

# The desktop has to be free, and asking for it is not the same as checking.
# A browser left in front took a whole run: the menu opened, the click meant
# for Copy went into the page behind it, and the wait below span for sixteen
# minutes because nothing was ever copied.
if (-not (Test-DrivingOurWindow)) {
    throw ('something else owns the foreground, so every click here would land in it. ' +
           'Nothing has been recorded. Close whatever is in front and run this again.')
}

# The dialog IS the point of two of these films, and Read-MessageBox dismisses
# it the moment it finds it. Held on camera first, long enough to be read.
function Show-Dialog([string]$TitleLike = 'DiscWright', [double]$Hold = 4.5, [int]$TimeoutSec = 90) {
    $dlg = Find-Ctl -Root $win -NameLike $TitleLike -TimeoutSec $TimeoutSec
    if (-not $dlg) { throw "no '$TitleLike' dialog appeared" }
    Beat $Hold
    return (Read-MessageBox -Win $win -TitleLike $TitleLike -TimeoutSec 15)
}

# Somewhere nothing pops a tooltip. A pointer left resting on a checkbox after a
# dialog is dismissed sits there for the rest of the film with its hover text
# open across the form.
function Move-PointerAway {
    Move-To -X ([int]($wr.X + 320)) -Y ([int]($wr.Y + 16))
    Beat 0.4
}

# The box has no toggle state UI Automation can read - it arrives as a bare Pane
# with no patterns at all - so every click on it is blind. Tracked here instead,
# and every film puts it back the way it found it. Films 1 and 2 both tick it,
# and the second one turned it OFF again until this existed.
$script:SetOn = $false
function Set-DiscSetBox([bool]$On) {
    if ($script:SetOn -eq $On) { return }
    Invoke-Glide -Ctl (Find-Ctl -Root $win -NameLike 'disc set' -TimeoutSec 5) -SettleMs 1200
    $script:SetOn = $On
}

function Add-Game([int]$Down, [string]$Expect) {
    Invoke-Glide -Ctl (Find-Ctl -Root $win -NameLike 'Add game*' -TimeoutSec 5)
    $c0 = Get-Date
    Complete-FolderDialogShown -Win $win -Expand 1 -Down $Down
    Add-Cut $c0 (Get-Date).AddMilliseconds(250)
    Beat 2.5
    $status = Get-StatusText $win
    if ($status -notmatch [regex]::Escape($Expect)) {
        throw "the driven pick did not land on $Expect - the status reads: $status"
    }
}

# Centred on the window being filmed. Without coordinates mshta leaves the menu
# in the middle of the screen, which is not where the recording is looking: a
# whole take came back showing nothing but its left edge clipped into a corner.
function Set-MenuInFrame([IntPtr]$Menu) {
    Move-MenuTo -Menu $Menu -X ([int]($wr.X + (700 - 760) / 2)) -Y ([int]($wr.Y + 250))
}

try {
    # ---------------------------------------------------------------- prep ---
    if ($Only -ne 'restore') {
        if (-not (Test-Path -LiteralPath (Join-Path $PrimeFrom 'discproject.json'))) {
            throw ("no project to prime the game picker from. Build one into $D-prime, " +
                   "or pass -PrimeFrom. Nothing has been recorded.")
        }
        Write-Host ('  priming from ' + $PrimeFrom)
        Set-CtlText -Ctl (Get-BoxAfter $win '6)  Output folder*') -Text $PrimeFrom
        Beat 0.4
        Invoke-CtlNamed $win 'Open existing disc*' | Out-Null
        Complete-FolderDialogShown -Win $win -Expand 0 -Down 0 -Paint 0.8
        Beat 2
        if ((Get-EntryCount $win) -lt 1) { throw "no game came out of the project in $PrimeFrom" }
        $before = Get-EntryCount $win
        Invoke-CtlNamed $win 'Add game*' | Out-Null
        Complete-FolderDialogShown -Win $win -Expand 1 -Down $DOWN_PRIME -Paint 0.8
        Beat 2
        if ((Get-EntryCount $win) -le $before) {
            throw "priming added nothing - the step count probably missed $PrimeGame"
        }
        Invoke-CtlNamed $win 'New disc' | Out-Null
        $yes = Find-Ctl -Root $win -NameLike 'Yes' -TimeoutSec 8
        if ($yes) { Invoke-Ctl -Ctl $yes -SettleMs 1200 }
        Beat 1
        if ((Get-EntryCount $win) -ne 0) { throw 'the form did not come back empty' }
        Set-CtlText -Ctl (Get-BoxAfter $win '6)  Output folder*') -Text $out
        Beat 0.5
    }

    # ================================================= FILM 1: refused ======
    if ($Only -in @('refused', 'all')) {
        Write-Host '  film 1: a file bigger than the disc'
        $capture = Start-Capture 'set-refused'
        Beat 1.5

        Add-Game $DOWN_HOLLOWKNIGHT 'Hollow Knight'
        Set-CtlTextGlide -Ctl (Get-BoxAfter $win '2)  Disc label*') -Text 'HOLLOW KNIGHT'
        Beat 1.2

        Invoke-Glide -Ctl (Find-MediaTarget $win) -SettleMs 400
        Set-MediaTarget -Win $win -Index 1
        Beat 2.2
        if ((Get-MediaTargetText $win) -notmatch 'CD-R') { throw 'the target disc is not a CD' }

        Set-DiscSetBox $true
        Beat 1.8

        Invoke-Glide -Ctl (Find-Ctl -Root $win -NameLike 'BUILD ISO' -TimeoutSec 5) -SettleMs 1200
        $said = Show-Dialog
        Write-Host ('  it said: ' + $said)
        if ($said -notmatch 'setup_hollow_knight') { throw "the refusal did not name the file: $said" }
        if ($said -notmatch 'larger disc')         { throw "the refusal did not say what to do: $said" }
        Move-PointerAway
        Beat 2.5

        Stop-Capture $capture
        $capture = $null
        Set-DiscSetBox $false
        Clear-AllEntries -Win $win
        Beat 1
    }

    # =================================================== FILM 2: build ======
    if ($Only -in @('build', 'all')) {
        Write-Host '  film 2: a real multi-disc set'
        $capture = Start-Capture 'set-build'
        Beat 1.5

        Add-Game $DOWN_DEADSPACE 'Dead Space'
        Set-CtlTextGlide -Ctl (Get-BoxAfter $win '2)  Disc label*') -Text 'DEAD SPACE'
        Beat 1.2

        # Artwork, which the restore film depends on as much as the look of this
        # one. The menu's buttons are found by scanning three columns for their
        # flat colour, and the fallback artwork DiscWright draws itself is flat
        # enough to be mistaken for them: with no background image the scan
        # returned the sky as two buttons and missed the real ones.
        Invoke-Glide -Ctl (Find-BrowseForBox -Win $win -Box (Get-BoxAfter $win '3)  Disc icon*'))
        Complete-FileDialogShown -Win $win -TitleLike 'Pick the disc icon' -File (Join-Path (Join-Path $D 'artwork') 'witcher-icon-source.jpg')
        Beat 1.4
        Invoke-Glide -Ctl (Find-RowButton -Win $win -LabelLike 'Background image:')
        Complete-FileDialogShown -Win $win -TitleLike 'Pick the menu background' -File (Join-Path (Join-Path $D 'artwork') 'alanwake-background.jpg')
        Beat 1.6

        Invoke-Glide -Ctl (Find-MediaTarget $win) -SettleMs 400
        Set-MediaTarget -Win $win -Index 2
        Beat 2.2
        if ((Get-MediaTargetText $win) -notmatch 'DVD5') { throw 'the target disc is not a DVD5' }

        Set-DiscSetBox $true
        Beat 2

        Write-Host '  building. This is the long part: a real 8.13 GB build.'
        Invoke-Glide -Ctl (Find-Ctl -Root $win -NameLike 'BUILD ISO' -TimeoutSec 5) -SettleMs 1500
        $msg = Show-Dialog -Hold 5.5 -TimeoutSec 3600
        Write-Host ('  it said: ' + ($msg -replace "`r?`n", ' | '))

        # Checked against the ISOs that are actually there, not a number written
        # down here. A hardcoded three was wrong once: 8.13 GB over a 4.36 GB
        # disc is 1.86, so it is two.
        $wrote = @(Get-ChildItem $out -Filter '*.iso' -File | Sort-Object Name)
        Write-Host ('  it wrote {0}: {1}' -f $wrote.Count, (($wrote | ForEach-Object { $_.Name }) -join ', '))
        if ($wrote.Count -lt 2) { throw "a set should be more than one disc, and this wrote $($wrote.Count)" }
        if ($msg -notmatch ("{0} discs" -f $wrote.Count)) {
            throw ("the dialog and the folder disagree: it said '$msg' and wrote $($wrote.Count) ISOs")
        }
        Move-PointerAway
        Beat 3

        Stop-Capture $capture
        $capture = $null
        Set-DiscSetBox $false
    }

    # ================================================= FILM 3: restore ======
    # Each disc's own menu, run from the folder that disc holds, which is what
    # the disc itself would start. The status counts up as the discs go in.
    if ($Only -in @('restore', 'all')) {
        $discs = @(Get-ChildItem $out -Directory -Filter 'disc D*' | Sort-Object Name)
        if ($discs.Count -lt 2) { throw "there is no set in $out to restore - run -Only build first" }
        Write-Host ('  film 3: putting ' + $discs.Count + ' discs back together')

        # What each disc owes, read off the set file the discs carry. Waiting for
        # the folder to "stop growing" was wrong: robocopy runs one file at a
        # time and the gap between two of them is longer than any quiet period
        # worth waiting for, so the wait ended mid-copy, the menu was read while
        # it still showed the state before the copy, and the teardown then
        # killed robocopy with a 300 MB file still to go.
        $owed = @{}
        foreach ($line in (Get-Content (Join-Path $discs[0].FullName 'Disc set.txt'))) {
            if ($line -notmatch '^[0-9]') { continue }
            $d, $h, $b, $f = $line -split ' +', 4
            $k = [int]$d
            if (-not $owed.ContainsKey($k)) { $owed[$k] = [double]0 }
            $owed[$k] += [double]$b
        }
        $running = [double]0
        foreach ($k in ($owed.Keys | Sort-Object)) {
            $running += $owed[$k]
            Write-Host ('      disc {0} brings {1:N2} GB, running total {2:N2} GB' -f $k, ($owed[$k]/1GB), ($running/1GB))
        }

        # The camera points at the MENU for this film, not at the form. The menu
        # is 760 wide and the window is 700, so a region pinned to the form can
        # never hold one: the first take came back as a mostly empty form with a
        # sliver of menu down the right-hand edge. The menu is opened first, its
        # rectangle is measured, and every disc after this one is put in exactly
        # the same place.
        $probe = Join-Path $discs[0].FullName 'AUTORUN\menu.hta'
        Start-Process 'mshta.exe' -ArgumentList "`"$probe`""
        $first = Wait-MenuWindow -TimeoutSec 25
        if ($first -eq [IntPtr]::Zero) { throw 'the first disc menu never appeared' }
        Set-MenuInFrame $first
        $mr = Get-MenuRect $first
        $region = @{ X = $mr.L; Y = $mr.T; W = ($mr.R - $mr.L); H = ($mr.B - $mr.T) }
        Write-Host ('      filming the menu itself: {0}x{1} at {2},{3}' -f
                    $region.W, $region.H, $region.X, $region.Y)
        Close-Menu
        Beat 1

        $capture = Start-Capture 'set-restore' $region
        Beat 1.5

        for ($n = 0; $n -lt $discs.Count; $n++) {
            $isLast = ($n -eq ($discs.Count - 1))
            $hta = Join-Path $discs[$n].FullName 'AUTORUN\menu.hta'
            if (-not (Test-Path $hta)) { throw "no menu on $($discs[$n].Name)" }
            Write-Host ('    disc {0}: {1}' -f ($n + 1), $discs[$n].Name)
            Start-Process 'mshta.exe' -ArgumentList "`"$hta`""
            $menu = Wait-MenuWindow -TimeoutSec 25
            if ($menu -eq [IntPtr]::Zero) { throw "the menu on $($discs[$n].Name) never appeared" }
            Set-MenuInFrame $menu
            Beat 5          # long enough to read what the disc says about itself

            $rows = Get-MenuButtonRows $menu
            Write-Host ('      {0} buttons before copying, rows {1}' -f $rows.Count,
                        (($rows | ForEach-Object { '{0}..{1}' -f $_[0], $_[1] }) -join ' '))
            if ($rows.Count -lt 3) { throw "only $($rows.Count) buttons on $($discs[$n].Name)" }
            # Every button has to be ON the menu. The set panel used to push the
            # last one over the bottom edge, where nothing can reach it and a
            # film of it would be a film of a bug.
            $lowest = ($rows | ForEach-Object { $_[1] } | Measure-Object -Maximum).Maximum
            if ($lowest -ge 474) {
                throw ("a button runs to y=$lowest of a 480px menu, so it is off the edge. " +
                       'The panel does not fit.')
            }

            Invoke-MenuButton -Menu $menu -Index 0       # Copy this disc
            Beat 1.5

            # One robocopy per file, so the gaps between them would end a wait
            # that only watched for the process. Watched by size instead: when
            # the folder has stopped growing for four seconds, the disc is in.
            # Exactly what should be there once this disc is in.
            $want = [double]0
            foreach ($k in $owed.Keys) { if ($k -le ($n + 1)) { $want += $owed[$k] } }
            Write-Host ('      waiting for {0:N2} GB' -f ($want / 1GB))
            $lastSize = -1; $still = 0; $started = $false
            for ($w = 0; $w -lt 1200; $w++) {
                Start-Sleep -Seconds 1
                $now = 0
                if (Test-Path $RESTORE) {
                    $now = [double](Get-ChildItem $RESTORE -Recurse -File -EA SilentlyContinue |
                                    Measure-Object -Sum Length).Sum
                }
                if ($now -gt 0) { $started = $true }
                # Nothing copied at all after half a minute means the click did
                # not reach the button, not that the copy is slow. Treating the
                # two the same is what span for sixteen minutes on an empty
                # folder, so this stops and photographs whatever IS on screen.
                if (-not $started -and $w -ge 30) {
                    $shot = Join-Path $OutDir ('stuck-disc{0}.png' -f ($n + 1))
                    Add-Type -AssemblyName System.Drawing, System.Windows.Forms
                    $vs = [System.Windows.Forms.SystemInformation]::VirtualScreen
                    $bm = New-Object System.Drawing.Bitmap($vs.Width, $vs.Height)
                    $gr = [System.Drawing.Graphics]::FromImage($bm)
                    $gr.CopyFromScreen($vs.X, $vs.Y, 0, 0, $bm.Size); $gr.Dispose()
                    $bm.Save($shot, [System.Drawing.Imaging.ImageFormat]::Png); $bm.Dispose()
                    throw ("Copy this disc was pressed and nothing was copied. The screen at " +
                           "that moment is in $shot - most likely something took the foreground.")
                }
                $lastSize = $now
                # Done when the bytes are all there, not when they pause.
                if ($now -ge $want) { $still++ } else { $still = 0 }
                if ($still -ge 2) { break }
            }
            if ($lastSize -lt $want) {
                throw ('the copy stopped at {0:N2} GB of {1:N2} GB' -f ($lastSize/1GB), ($want/1GB))
            }
            Write-Host ('      the folder holds {0:N2} GB' -f ($lastSize / 1GB))

            # robocopy's console had the foreground; the menu needs it back
            # before anything else is clicked, and it has redrawn from the
            # folder in the meantime.
            Set-MenuInFrame $menu
            Beat 5          # long enough to read the status counting up

            $rows = Get-MenuButtonRows $menu
            Write-Host ('      {0} buttons after copying' -f $rows.Count)

            if ($isLast) {
                # The set is complete, so Copy is gone and the menu offers to
                # install it. This is the payoff, so the film sits on it.
                #
                # It does NOT press Install: that would start GOG's real
                # installer. Nor does it press Delete any more. Buttons are
                # found by scanning for their colour, and two of them whose gap
                # happens to fall on a dark part of the artwork come back as one
                # row - so an index is not a reliable way to reach a particular
                # button in every state. Copy is always the first and that one
                # is safe; the rest are left alone and the folder is tidied up
                # after the camera stops.
                if ($rows.Count -lt 4) {
                    throw ("a completed set should offer the install and more besides, " +
                           "and this offered $($rows.Count) buttons")
                }
                $done = Join-Path $RESTORE 'DEAD SPACE'
                $there = @(Get-ChildItem $done -Recurse -File -EA SilentlyContinue)
                Write-Host ('      the folder holds {0} files, {1:N2} GB' -f $there.Count,
                            (($there | Measure-Object -Sum Length).Sum / 1GB))
                if ($there.Count -lt 4) { throw "the set is not complete: only $($there.Count) files" }
                Beat 6      # sit on the finished state, which is the whole point
            }
            # The last disc's menu stays up: the finished state is the ending.
            if (-not $isLast) { Close-Menu; Beat 1.5 }
        }

        Stop-Capture $capture
        $capture = $null

        # Tidied up after the camera stops. The menu's own Delete does this for
        # a person; it is not driven here because reaching one particular
        # button by index is not reliable in every state.
        if (Test-Path $RESTORE) {
            Remove-Item $RESTORE -Recurse -Force -EA SilentlyContinue
            Write-Host '  cleared the restore folder'
        }
    }
}
finally {
    if ($capture) { Stop-Capture $capture }
    Stop-DiscWright $app
    Get-Process mshta -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue
}

Write-Host ''
Write-Host 'Assemble with:'
foreach ($t in 'set-refused', 'set-build', 'set-restore') {
    $f = Join-Path $OutDir "frames-$t"
    if (Test-Path $f) { Write-Host ("  python .claude\skills\demo-gifs\assemble.py `"$f`" docs\$t.gif") }
}

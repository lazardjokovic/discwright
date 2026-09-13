<#
.SYNOPSIS
    Records both README demonstrations by driving DiscWright, and writes the
    frames for Assemble.

.DESCRIPTION
    Both GIFs come out of ONE run of the application. The game picker only
    remembers where the demo folder is for the life of the process, and that
    memory is what lets the second recording add its games without anyone
    touching the machine. RECORDING-STEPS.md in the demo folder tells a human the
    same thing: do not restart between GIF 1 and GIF 2.

    Nobody has to touch the machine at all, as long as -PrimeFrom points at a
    folder holding a discproject.json. Loading a project puts a game on the form,
    which is what teaches the picker; New disc then clears the form and keeps
    that memory. Without one, this stops and asks for a single pick.

    LEAVE THE MACHINE ALONE while it runs. It drives the real pointer and the
    real foreground window, and anything else that takes the foreground both
    stops the run and gets photographed.

    Every Browse For Folder is marked for cutting. Seeded or not, its tree shows
    the OneDrive node and a personal folder, both of which carry a real name, and
    these GIFs go in a public README.

.PARAMETER DemoRoot
    The folder holding the games, artwork, media and music. Everything the window
    displays comes from here, which is why no path on camera carries a username.

.PARAMETER PrimeFrom
    A folder holding a discproject.json, used off camera to teach the game picker
    where DemoRoot is. Any previously built disc will do.

.PARAMETER Only
    demo, multi, or both.
#>
param(
    [string]$DemoRoot = 'F:\DWdemo',
    [string]$OutDir   = (Join-Path $env:TEMP 'discwright-gifs'),
    [string]$PrimeFrom = '',
    [ValidateSet('demo', 'multi', 'both')][string]$Only = 'both',
    [string]$AppPath = (Join-Path (Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent) 'DiscWright.ps1')
)
$ErrorActionPreference = 'Stop'
$SC  = $PSScriptRoot
$APP = $AppPath
$D   = $DemoRoot
$repo = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
Import-Module (Join-Path $repo 'tests\ui\UiDriver.psm1') -Force
. "$SC\demolib.ps1"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
if (-not $PrimeFrom) { $PrimeFrom = Join-Path $D 'out-previous' }

# Step counts into the demo folder's tree, measured by treemap.ps1 rather than
# assumed from an alphabetical sort - the shell sorts its own way and the folder
# holds working directories as well as games. Re-measure after adding or
# removing anything in there.
$DOWN_ALANWAKE     = 1
$DOWN_HOLLOWKNIGHT = 4
$DOWN_WITCHER      = 9
# The game used off camera to teach the picker, and so the one neither recording
# adds. Checked by name afterwards, because a wrong step count picks a folder
# with no installer in it and the run should stop there rather than record it.
$DOWN_PRIME        = 3
$PrimeGame         = 'Dead Space'

$capture = $null
function Start-Capture([string]$Tag) {
    $frames = Join-Path $OutDir "frames-$Tag"
    $stop   = Join-Path $OutDir "stop-$Tag.flag"
    Remove-Item $stop -Force -ErrorAction SilentlyContinue
    Start-CutLog (Join-Path $OutDir "cuts-$Tag.txt")
    Start-Process 'powershell.exe' -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File',
        "`"$SC\capture.ps1`"",'-OutDir',"`"$frames`"",'-StopFile',"`"$stop`"",'-IntervalMs','100' -WindowStyle Hidden
    Start-Sleep -Seconds 2
    return [pscustomobject]@{ Tag = $Tag; Frames = $frames; Stop = $stop }
}
function Stop-Capture($Cap) {
    if (-not $Cap) { return }
    New-Item -ItemType File -Path $Cap.Stop -Force | Out-Null
    Start-Sleep -Seconds 2
    $n = (Get-ChildItem $Cap.Frames -Filter *.png -ErrorAction SilentlyContinue | Measure-Object).Count
    Write-Host ('  [{0}] {1} frames' -f $Cap.Tag, $n)
}

$app = Start-DiscWright -AppPath $APP
$win = $app.Window
$wr  = $win.Current.BoundingRectangle
Beat 1.5

try {
    # ---------------------------------------------------------------- prep ---
    # Teach the game picker where the demo folder is, off camera.
    #
    # With nothing to go on it opens at GOG Galaxy's download folder, or at
    # whatever the shell feels like, and the step counts below are measured from
    # the demo folder's own node. One pick from anywhere inside it fixes that for
    # the life of the process, and it survives New disc and an emptied list.
    #
    # Opening a project is the way to do that without a person: the project puts
    # a game on the form, the picker learns that game's parent, and New disc then
    # takes the form back to empty while keeping what it learned.
    if (Test-Path -LiteralPath (Join-Path $PrimeFrom 'discproject.json')) {
        Write-Host ('  priming from ' + $PrimeFrom)
        Set-CtlText -Ctl (Get-BoxAfter $win '6)  Output folder*') -Text $PrimeFrom
        Beat 0.4
        Invoke-CtlNamed $win 'Open existing disc*' | Out-Null
        Complete-FolderDialogShown -Win $win -Expand 0 -Down 0 -Paint 0.8
        Beat 2
        if ((Get-EntryCount $win) -lt 1) { throw "no game came out of the project in $PrimeFrom" }

        # Opening the project is not enough on its own. It puts a game on the
        # form, which makes the NEXT Add game open beside that game - but only a
        # completed pick writes the folder down where it survives New disc and an
        # emptied list. Without this the first recorded pick opens at GOG
        # Galaxy's own download folder instead, and the step counts below are
        # measured from the demo folder.
        #
        # Picked here: the game neither recording uses, so nothing is added twice.
        #
        # Checked by counting rather than by name. The status line names a game
        # only while there is exactly one on the disc, and the project this primes
        # from already has several - so the name is not there to read. What
        # matters at this point is that the pick landed on a folder with an
        # installer in it, which is what a rise in the count proves. Whether it
        # landed on the RIGHT folder is checked where it matters, by the two
        # recordings, which assert the game they each asked for.
        $before = Get-EntryCount $win
        Invoke-CtlNamed $win 'Add game*' | Out-Null
        Complete-FolderDialogShown -Win $win -Expand 1 -Down $DOWN_PRIME -Paint 0.8
        Beat 2
        if ((Get-EntryCount $win) -le $before) {
            throw ("priming added nothing - the step count probably missed $PrimeGame. Status reads: " +
                   (Get-StatusText $win))
        }

        Invoke-CtlNamed $win 'New disc' | Out-Null
        $yes = Find-Ctl -Root $win -NameLike 'Yes' -TimeoutSec 8
        if (-not $yes) { throw 'New disc did not ask before clearing' }
        Invoke-Ctl -Ctl $yes -SettleMs 1200
        Beat 1
    }
    else {
        Write-Host ''
        Write-Host '  >>> ONE PICK NEEDED <<<'
        Write-Host ("      Add game...  ->  $D\Alan Wake  ->  OK")
        Write-Host ('      No project to prime from at ' + $PrimeFrom + '.')
        Write-Host '      Nothing is recording yet.'
        Write-Host ''
        $got = $false
        for ($i = 0; $i -lt 300; $i++) {
            Start-Sleep -Seconds 1
            try { if ((Get-EntryCount $win) -ge 1) { $got = $true; break } } catch { }
        }
        if (-not $got) { throw 'no game was added within five minutes' }
        $null = Set-WindowFocus $win
        Beat 1
        Clear-AllEntries -Win $win
        Beat 1
    }
    if ((Get-EntryCount $win) -ne 0) { throw 'the form did not come back empty' }

    # Back to the real output folder, and off camera, so adding the first game
    # never auto-fills it with a Desktop path carrying the username.
    Set-CtlText -Ctl (Get-BoxAfter $win '6)  Output folder*') -Text "$D\out"
    Beat 0.5

    # ================================================== GIF 1: one game =====
    # The build at the end is real and takes minutes. The progress bar and the
    # finishing time are the point of this one.
    if ($Only -eq 'demo' -or $Only -eq 'both') {
        $capture = Start-Capture 'demo'
        Beat 1.5

        Invoke-Glide -Ctl (Find-Ctl -Root $win -NameLike 'Add game*' -TimeoutSec 5)
        $c0 = Get-Date
        Complete-FolderDialogShown -Win $win -Expand 1 -Down $DOWN_ALANWAKE
        Add-Cut $c0 (Get-Date).AddMilliseconds(250)
        Beat 2.5
        if ((Get-StatusText $win) -notmatch 'Alan Wake') { throw 'the driven pick did not land on Alan Wake' }

        Set-CtlTextGlide -Ctl (Get-BoxAfter $win '2)  Disc label*') -Text 'ALAN WAKE'
        Beat 1.4

        Invoke-Glide -Ctl (Find-BrowseForBox -Win $win -Box (Get-BoxAfter $win '3)  Disc icon*'))
        Complete-FileDialogShown -Win $win -TitleLike 'Pick the disc icon' -File "$D\artwork\alanwake-icon.ico"
        Beat 1.6

        Invoke-Glide -Ctl (Find-RowButton -Win $win -LabelLike 'Background image:')
        Complete-FileDialogShown -Win $win -TitleLike 'Pick the menu background' -File "$D\artwork\alanwake-background.jpg"
        Beat 1.6

        Invoke-Glide -Ctl (Find-Exact $win 'Show title')
        Beat 1.4

        Invoke-Glide -Ctl (Find-Exact $win 'Manual')
        Beat 0.5
        Invoke-Glide -Ctl (Find-RowButton -Win $win -LabelLike 'Manual file:')
        Complete-FileDialogShown -Win $win -TitleLike 'Pick the manual for this disc' `
            -File "$D\media\AlanWake_manual\alan_wake_manual\Alan Wake manual.pdf"
        Beat 1.4

        # Extras. Typed into the box first, which seeds its own Browse at that folder
        # so the dialog opens on it - the alternative is walking the tree up from
        # wherever the manual was picked. The dialog still has to be clicked through:
        # typing sets the text, but only the dialog sets the path the build reads.
        Invoke-Glide -Ctl (Find-Exact $win 'Extras')
        Beat 0.5
        Set-CtlTextGlide -Ctl (Find-BoxOnRow $win 'Extras folder:') -Text "$D\media\DiscExtras"
        Beat 0.6
        Invoke-Glide -Ctl (Find-RowButton -Win $win -LabelLike 'Extras folder:')
        $c0 = Get-Date
        Complete-FolderDialogShown -Win $win -Expand 0 -Down 0
        Add-Cut $c0 (Get-Date).AddMilliseconds(250)
        Beat 1.6

        # The two boxes that did not exist at 0.4.0, which is why these are being
        # remade at all.
        Invoke-Glide -Ctl (Find-Exact $win 'named on Linux')
        Beat 1.2

        # The XP box is greyed for a game that came in parts, because the older
        # filesystems stop at 2 GiB and GOG splits at twice that. Clicking a
        # greyed box would record a click that does nothing, so rest on it
        # instead and let its tooltip say why - which is the more useful shot.
        $xp = Find-Exact $win 'readable on Windows XP and older'
        if ($xp.Current.IsEnabled) {
            Invoke-Glide -Ctl $xp
            Beat 1.8
        } else {
            Move-ToCtl $xp
            Beat 3.5
        }

        # The menu, built from what is on the form.
        Invoke-Glide -Ctl (Find-Ctl -Root $win -NameLike 'Preview menu' -TimeoutSec 5) -SettleMs 1200
        $menu = Wait-MenuWindow
        if ($menu -ne [IntPtr]::Zero) {
            Move-MenuTo -Menu $menu -X ([int]($wr.X + (700 - 760) / 2)) -Y ([int]($wr.Y + 250))
            Beat 1.2
            Move-To -X ([int]($wr.X + 460)) -Y ([int]($wr.Y + 330))
            Beat 3.5
            Close-Menu
        } else {
            Write-Host '  the preview never appeared'
        }
        Beat 1.5

        # The build. The progress bar and the clock are the point of this GIF, so it
        # runs for real - 7.79 GB of it - and the middle gets thinned out afterwards.
        Write-Host '  building. This is the long part.'
        Invoke-Glide -Ctl (Find-Ctl -Root $win -NameLike 'BUILD ISO' -TimeoutSec 5) -SettleMs 1500
        $done = $null
        for ($i = 0; $i -lt 1800; $i++) {          # up to half an hour
            Start-Sleep -Seconds 1
            $done = Find-Ctl -Root $win -NameLike 'DiscWright' -TimeoutSec 1
            if ($done) { break }
        }
        if (-not $done) { throw 'the build never finished' }
        Write-Host '  build finished'
        Beat 3
        Invoke-Glide -Ctl (Find-Ctl -Root $done -NameLike 'OK' -TimeoutSec 5) -SettleMs 1200
        Beat 3.5

        Stop-Capture $capture
        $capture = $null
    }

    # ============================================= GIF 2: several games =====
    # No build in this one, so it is quick.
    if ($Only -eq 'multi' -or $Only -eq 'both') {
        # This recording opens by clearing the finished one-game disc, which is
        # what the form is holding when it follows GIF 1. Run on its own it would
        # open on an empty form, where New disc is greyed because there is
        # nothing to clear and clicking it does nothing at all - so put the disc
        # back first, off camera, out of what GIF 1 built.
        if ((Get-EntryCount $win) -eq 0) {
            $built = Join-Path $D 'out'
            if (-not (Test-Path -LiteralPath (Join-Path $built 'discproject.json'))) {
                throw ("nothing to open at $built - record the demo GIF first, or pass -Only both")
            }
            Write-Host '  putting the finished one-game disc back on the form'
            Set-CtlText -Ctl (Get-BoxAfter $win '6)  Output folder*') -Text $built
            Beat 0.4
            Invoke-CtlNamed $win 'Open existing disc*' | Out-Null
            Complete-FolderDialogShown -Win $win -Expand 0 -Down 0 -Paint 0.8
            Beat 2
            if ((Get-EntryCount $win) -lt 1) { throw "no game came out of the project in $built" }
        }

        $capture = Start-Capture 'multi'
        Beat 1.5

        # The confirmation is a message box titled "New disc", the same name as the
        # button that raised it, so it is found by its Yes button instead.
        Invoke-Glide -Ctl (Find-Ctl -Root $win -NameLike 'New disc' -TimeoutSec 5) -SettleMs 1000
        $yes = Find-Ctl -Root $win -NameLike 'Yes' -TimeoutSec 8
        if (-not $yes) { throw 'New disc did not ask before clearing' }
        Beat 1.6
        Invoke-Glide -Ctl $yes -SettleMs 1200
        Beat 1.8

        Invoke-Glide -Ctl (Find-Ctl -Root $win -NameLike 'Add game*' -TimeoutSec 5)
        $c0 = Get-Date
        Complete-FolderDialogShown -Win $win -Expand 1 -Down $DOWN_WITCHER
        Add-Cut $c0 (Get-Date).AddMilliseconds(250)
        Beat 2.2
        if ((Get-StatusText $win) -notmatch 'Witcher') { throw 'the first pick was not The Witcher' }

        Invoke-Glide -Ctl (Find-Ctl -Root $win -NameLike 'Add game*' -TimeoutSec 5)
        $c0 = Get-Date
        Complete-FolderDialogShown -Win $win -Expand 1 -Down $DOWN_HOLLOWKNIGHT
        Add-Cut $c0 (Get-Date).AddMilliseconds(250)
        Beat 2.5

        # Two patches, filed under Hollow Knight through the Which game? dialog.
        $hk = Join-Path $D 'Hollow Knight'
        $patches = @(
            (Join-Path $hk 'patch_hollow_knight_1.5.78.11833a_(85515)_to_1.5.12459_(88294).exe'),
            (Join-Path $hk 'patch_hollow_knight_1.5.12459_(88294)_to_1.5.12618_(89712).exe')
        )
        foreach ($p in $patches) {
            if (-not (Test-Path -LiteralPath $p)) { throw "missing patch: $p" }
            Invoke-Glide -Ctl (Find-Ctl -Root $win -NameLike 'Add-on*' -TimeoutSec 5)
            Complete-FileDialogShown -Win $win -TitleLike 'Pick one or more add-on*' -File $p -Paint 1.4
            Complete-ParentPicker -Win $win -Name 'Hollow Knight'
            Beat 1.6
        }
        if ((Get-StatusText $win) -notmatch '2 add-ons') {
            Write-Host ('  status reads: ' + (Get-StatusText $win))
        }
        Beat 1.5

        Set-CtlTextGlide -Ctl (Get-BoxAfter $win '2)  Disc label*') -Text 'WITCHER + HK'
        Beat 1.4

        # A .jpg this time, so the line under the box shows DiscWright building the
        # multi-size icon itself.
        Invoke-Glide -Ctl (Find-BrowseForBox -Win $win -Box (Get-BoxAfter $win '3)  Disc icon*'))
        Complete-FileDialogShown -Win $win -TitleLike 'Pick the disc icon' -File "$D\artwork\witcher-icon-source.jpg"
        Beat 2

        Invoke-Glide -Ctl (Find-RowButton -Win $win -LabelLike 'Background image:')
        Complete-FileDialogShown -Win $win -TitleLike 'Pick the menu background' -File "$D\artwork\witcher-background.jpg"
        Beat 1.6

        Invoke-Glide -Ctl (Find-Exact $win 'Background music')
        Beat 0.5
        Invoke-Glide -Ctl (Find-RowButton -Win $win -LabelLike 'Background music')
        Complete-FileDialogShown -Win $win -TitleLike 'Pick the background music' `
            -File "$D\music\Dusk of a Northern Kingdom.mp3"
        Beat 2

        Invoke-Glide -Ctl (Find-Ctl -Root $win -NameLike 'Preview menu' -TimeoutSec 5) -SettleMs 1200
        $menu = Wait-MenuWindow
        if ($menu -ne [IntPtr]::Zero) {
            Move-MenuTo -Menu $menu -X ([int]($wr.X + (700 - 760) / 2)) -Y ([int]($wr.Y + 250))
            Beat 2.5                                   # the chooser: two games and Exit
            Invoke-MenuButton -Menu $menu -Index 1      # Hollow Knight
            Beat 3                                     # its two patches, greyed out
            $rows = Get-MenuButtonRows $menu
            Write-Host ('  game screen shows {0} buttons' -f $rows.Count)
            Invoke-MenuButton -Menu $menu -Index ($rows.Count - 2)   # Back, above Exit
            Beat 2.5
            Invoke-MenuButton -Menu $menu -Index 0      # The Witcher
            Beat 4
        } else {
            Write-Host '  the preview never appeared'
        }

        # Stopped while the menu is still up, so the recording ends on it the way
        # the 0.4.0 one did. Closing first ends the GIF on the form with the menu
        # gone, which is the least interesting frame of the lot.
        Stop-Capture $capture
        $capture = $null
        Close-Menu
    }
}
catch {
    Write-Host ('  FAILED: ' + $_.Exception.Message)
    throw
}
finally {
    Stop-Capture $capture
    Close-Menu
    Stop-DiscWright $app
}

Write-Host '  session done'
